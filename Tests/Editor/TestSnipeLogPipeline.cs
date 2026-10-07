using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniIT.Http;
using MiniIT.Snipe.Api;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestSnipeLogPipeline
	{
		private readonly List<string> _temporaryDirectories = new List<string>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < _temporaryDirectories.Count; i++)
			{
				string directory = _temporaryDirectories[i];
				if (Directory.Exists(directory))
				{
					Directory.Delete(directory, true);
				}
			}

			_temporaryDirectories.Clear();
		}

		[Test]
		public void ContextFactory_UsesDefaultReporterAndLocksConfiguration()
		{
			var factory = new ContextFactoryStub();
			ILogReporter reporter = factory.CreateReporter();

			Assert.IsInstanceOf<LogReporter>(reporter);
			Assert.Throws<InvalidOperationException>(() => factory.SetLogReporterFactory(new ReporterFactoryStub(new ReporterStub())));

			reporter.Dispose();
		}

		[Test]
		public void ContextFactory_RejectsNullFactoriesAndReporters()
		{
			var factory = new ContextFactoryStub();

			Assert.Throws<ArgumentNullException>(() => factory.SetLogReporterFactory(null));
			factory.SetLogReporterFactory(new ReporterFactoryStub(null));
			Assert.Throws<InvalidOperationException>(() => factory.CreateReporter());
		}

		[Test]
		public async Task ContextFactory_CustomReporterReceivesLifecycleCalls()
		{
			var reporter = new ReporterStub();
			var factory = new ContextFactoryStub();
			factory.SetLogReporterFactory(new ReporterFactoryStub(reporter));

			Assert.AreSame(reporter, factory.CreateReporter());

			SnipeOptions options = CreateOptions();
			var context = new ContextStub(options, reporter);
			context.Reinitialize(options);
			bool sent = await context.LogReporter.SendAsync();
			context.Dispose();

			Assert.IsTrue(sent);
			Assert.AreEqual(2, reporter.InitializeCount);
			Assert.AreSame(context, reporter.LastContext);
			Assert.AreSame(options, reporter.LastOptions);
			Assert.AreEqual(1, reporter.SendCount);
			Assert.AreEqual(1, reporter.DisposeCount);
		}

		[Test]
		public async Task DefaultReporter_DelegatesLifecycleToPipeline()
		{
			var pipeline = new PipelineStub();
			var reporter = new LogReporter(pipeline);

			reporter.Initialize(null, null);
			Assert.IsTrue(await reporter.SendAsync());
			reporter.Dispose();
			reporter.Dispose();

			Assert.AreEqual(1, pipeline.InitializeCount);
			Assert.AreEqual(1, pipeline.SendCount);
			Assert.AreEqual(1, pipeline.DisposeCount);
		}

		[Test]
		public void SerializeRecord_EscapesJsonAndKeepsUnicodeUtf8()
		{
			var record = new SnipeLogRecord(12, LogType.Warning, "quote\" slash\\ line\n snowman \u2603 control \u0001", "stack\tvalue");

			string json = SnipeLogPipeline.SerializeRecord(record);

			Assert.AreEqual(
				"{\"time\":12,\"level\":\"Warning\",\"msg\":\"quote\\\" slash\\\\ line\\n snowman \u2603 control \\u0001\",\"stack\":\"stack\\tvalue\"}",
				json);
		}

		[Test]
		public void BuildBatchPrefix_AddsOnlyRequestedSessionId()
		{
			Assert.AreEqual(
				"{\"connectionID\":1,\"userID\":3,\"version\":\"v\",\"platform\":\"p\",\"list\":[",
				LogSender.BuildBatchPrefix(1, null, 3, "v", "p"));
			Assert.AreEqual(
				"{\"connectionID\":1,\"sessionID\":2,\"userID\":3,\"version\":\"v\",\"platform\":\"p\",\"list\":[",
				LogSender.BuildBatchPrefix(1, 2, 3, "v", "p"));
		}

		[Test]
		public void BuildBatchContent_UsesUtf8BytesAndCarriesNextRecord()
		{
			const string first = "{\"msg\":\"Привет\"}";
			const string second = "{\"msg\":\"мир\"}";
			string prefix = LogSender.BuildBatchPrefix(1, 2, 3, "v", "p");
			int maxBytes = Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(first) + Encoding.UTF8.GetByteCount("]}");

			using (StreamReader reader = CreateReader(first + "\n" + second + "\n"))
			{
				string carry = null;
				LogBatchContent batch = LogSender.BuildBatchContent(reader, ref carry, 1, 2, 3, "v", "p", maxBytes);

				Assert.AreEqual(1, batch.RecordCount);
				Assert.AreEqual(maxBytes, batch.PayloadBytes);
				Assert.AreEqual(second, carry);
				StringAssert.Contains(first, batch.Content);
				StringAssert.DoesNotContain(second, batch.Content);
			}
		}

		[Test]
		public void BuildBatchContent_OversizedRecordIsSentAlone()
		{
			const string oversized = "{\"msg\":\"0123456789\"}";
			const string next = "{\"msg\":\"next\"}";
			string prefix = LogSender.BuildBatchPrefix(1, 2, 3, "v", "p");
			int maxBytes = Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(oversized) + Encoding.UTF8.GetByteCount("]}") - 1;

			using (StreamReader reader = CreateReader(oversized + "\n" + next + "\n"))
			{
				string carry = null;
				LogBatchContent batch = LogSender.BuildBatchContent(reader, ref carry, 1, 2, 3, "v", "p", maxBytes);

				Assert.AreEqual(1, batch.RecordCount);
				Assert.IsTrue(batch.HasOversizedRecord);
				Assert.AreEqual(Encoding.UTF8.GetByteCount(oversized), batch.OversizedRecordBytes);
				StringAssert.Contains(oversized, batch.Content);
			}
		}

		[Test]
		public void GetSendProfile_UsesWebGlLimits()
		{
			LogSendProfile defaultProfile = LogSender.GetSendProfile(RuntimePlatform.WindowsEditor);
			LogSendProfile webGlProfile = LogSender.GetSendProfile(RuntimePlatform.WebGLPlayer);

			Assert.AreEqual(200 * 1024, defaultProfile.MaxChunkBytes);
			Assert.AreEqual(TimeSpan.FromSeconds(5), defaultProfile.RequestTimeout);
			Assert.AreEqual(4 * 1024, webGlProfile.MaxChunkBytes);
			Assert.AreEqual(TimeSpan.FromSeconds(20), webGlProfile.RequestTimeout);
		}

		[Test]
		public async Task SendAsync_RetriesFilesInOrderAndDeletesOnlyAfterSuccess()
		{
			var sender = new RecordingSender(false, true, true);
			string directory = CreateTemporaryDirectory();
			using (var pipeline = new SnipeLogPipeline(42, directory, sender))
			{
				pipeline.Append(new SnipeLogRecord(1, LogType.Log, "first", string.Empty));
				Assert.IsFalse(await pipeline.SendAsync());
				Assert.AreEqual(1, CountNonEmptyLogFiles(directory));

				pipeline.Append(new SnipeLogRecord(2, LogType.Warning, "second", string.Empty));
				Assert.IsTrue(await pipeline.SendAsync());

				Assert.AreEqual(3, sender.Contents.Count);
				StringAssert.Contains("first", sender.Contents[0]);
				StringAssert.Contains("first", sender.Contents[1]);
				StringAssert.Contains("second", sender.Contents[2]);
				Assert.AreEqual(0, CountNonEmptyLogFiles(directory));
			}
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task SendAsync_ResumesAfterAcknowledgedPortionsAndKeepsNewFilesInOrder(bool throwOnFailure)
		{
			object failure = throwOnFailure ? new IOException("Simulated send failure") : (object)false;
			var sender = new BatchedSender(true, true, failure, false, true, true, true);
			string directory = CreateTemporaryDirectory();
			using (var pipeline = new SnipeLogPipeline(null, directory, sender))
			{
				AppendRecords(pipeline, "A", "B", "C", "D");
				Assert.IsFalse(await pipeline.SendAsync());
				string filePath = Directory.GetFiles(directory, "*.ndjson")[0];
				string originalContent = File.ReadAllText(filePath);
				Assert.AreEqual(4, CountRecords(originalContent));

				AppendRecords(pipeline, "E");
				Assert.IsFalse(await pipeline.SendAsync());
				Assert.AreEqual(originalContent, File.ReadAllText(filePath));
				Assert.IsTrue(await pipeline.SendAsync());

				CollectionAssert.AreEqual(new[]
				{
					BuildExpectedBatch("A"), BuildExpectedBatch("B"), BuildExpectedBatch("C"),
					BuildExpectedBatch("C"), BuildExpectedBatch("C"), BuildExpectedBatch("D"), BuildExpectedBatch("E")
				}, sender.Contents);
				Assert.AreEqual(0, CountNonEmptyLogFiles(directory));
			}
		}

		[Test]
		public async Task SendAsync_PreservesProgressWhenPipelineIsReinitialized()
		{
			var sender = new BatchedSender(true, false);
			var client = new HttpClientStub(new HttpResponseStub(200, true));
			var defaults = new NullSnipeServices();
			var services = new NullSnipeServices(defaults.SharedPrefs, defaults.LoggerFactory, defaults.Analytics,
				defaults.MainThreadRunner, defaults.ApplicationInfo, defaults.FuzzyStopwatchFactory,
				new HttpClientFactoryStub(client), defaults.InternetReachability, defaults.Ticker);
			SnipeOptions options = new SnipeOptionsBuilder()
				.SetProjectInfo(new SnipeProjectInfo { ClientKey = "test-key" })
				.SetLogReporterUrl("https://example.com/log")
				.Build(0, services);
			var communicator = new SnipeCommunicator(options, null, services);
			using (var context = new ContextStub(options, new ReporterStub(), communicator))
			using (var pipeline = new SnipeLogPipeline(null, CreateTemporaryDirectory(), sender))
			{
				AppendRecords(pipeline, "A", "B");
				Assert.IsFalse(await pipeline.SendAsync());
				pipeline.Initialize(context, options);
				Assert.IsTrue(await pipeline.SendAsync());
				Assert.AreEqual(1, client.Contents.Count);
				StringAssert.DoesNotContain("\"msg\":\"A\"", client.Contents[0]);
				StringAssert.Contains("\"msg\":\"B\"", client.Contents[0]);
			}
			communicator.Dispose();
		}

		[Test]
		public async Task SendAsync_ResumesWithEmptyLinesAndUnicodeRecords()
		{
			var sender = new BatchedSender(false, true, false, true);
			string directory = CreateTemporaryDirectory();
			using (var pipeline = new SnipeLogPipeline(null, directory, sender))
			{
				AppendRecords(pipeline, "А", "Б");
				Assert.IsFalse(await pipeline.SendAsync());
				string filePath = Directory.GetFiles(directory, "*.ndjson")[0];
				string content = File.ReadAllText(filePath);
				File.WriteAllText(filePath, "\n" + content.Replace("\n", "\n\n") + "\n", new UTF8Encoding(false));

				Assert.IsFalse(await pipeline.SendAsync());
				Assert.IsTrue(await pipeline.SendAsync());
				CollectionAssert.AreEqual(new[]
				{
					BuildExpectedBatch("А"), BuildExpectedBatch("А"), BuildExpectedBatch("Б"), BuildExpectedBatch("Б")
				}, sender.Contents);
				Assert.AreEqual(0, CountNonEmptyLogFiles(directory));
			}
		}

		[Test]
		[Platform("Win")]
		public async Task SendAsync_PreservesAcknowledgementsWhenDeletingFileFails()
		{
			var sender = new BatchedSender(true);
			string directory = CreateTemporaryDirectory();
			FileStream deletionBlocker = null;
			try
			{
				using (var pipeline = new SnipeLogPipeline(null, directory, sender))
				{
					sender.BeforeSend = () => deletionBlocker = new FileStream(
						Directory.GetFiles(directory, "*.ndjson")[0], FileMode.Open, FileAccess.Read, FileShare.Read);
					AppendRecords(pipeline, "A");
					Assert.IsFalse(await pipeline.SendAsync());
					Assert.AreEqual(1, CountNonEmptyLogFiles(directory));

					deletionBlocker.Dispose();
					deletionBlocker = null;
					sender.BeforeSend = null;
					Assert.IsTrue(await pipeline.SendAsync());
					CollectionAssert.AreEqual(new[] { BuildExpectedBatch("A") }, sender.Contents);
					Assert.AreEqual(0, CountNonEmptyLogFiles(directory));
				}
			}
			finally
			{
				deletionBlocker?.Dispose();
			}
		}

		[TestCase(0)]
		[TestCase(1)]
		public async Task SendAsync_RejectsFileShorterThanAcknowledgedProgress(int remainingRecords)
		{
			var sender = new BatchedSender(true, true, false);
			string directory = CreateTemporaryDirectory();
			using (var pipeline = new SnipeLogPipeline(null, directory, sender))
			{
				AppendRecords(pipeline, "A", "B", "C");
				Assert.IsFalse(await pipeline.SendAsync());
				string filePath = Directory.GetFiles(directory, "*.ndjson")[0];
				string shortenedContent = remainingRecords == 0 ? string.Empty : SnipeLogPipeline.SerializeRecord(CreateRecord("A")) + "\n";
				File.WriteAllText(filePath, shortenedContent, new UTF8Encoding(false));
				Assert.IsFalse(await pipeline.SendAsync());
				Assert.IsTrue(File.Exists(filePath));
				Assert.AreEqual(3, sender.Contents.Count);
			}
		}

		[Test]
		public async Task SendAsync_BarrierFlushesPriorRecordsInFifoOrder()
		{
			var sender = new RecordingSender(true);
			using (var pipeline = new SnipeLogPipeline(null, CreateTemporaryDirectory(), sender))
			{
				pipeline.Append(new SnipeLogRecord(1, LogType.Log, "first", string.Empty));
				pipeline.Append(new SnipeLogRecord(2, LogType.Warning, "second", string.Empty));
				pipeline.Append(new SnipeLogRecord(3, LogType.Error, "third", string.Empty));

				Assert.IsTrue(await pipeline.SendAsync());
				Assert.AreEqual(1, sender.Contents.Count);

				string content = sender.Contents[0];
				int firstIndex = content.IndexOf("first", StringComparison.Ordinal);
				int secondIndex = content.IndexOf("second", StringComparison.Ordinal);
				int thirdIndex = content.IndexOf("third", StringComparison.Ordinal);
				Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0));
				Assert.That(secondIndex, Is.GreaterThan(firstIndex));
				Assert.That(thirdIndex, Is.GreaterThan(secondIndex));
			}
		}

		[Test]
		public async Task SendAsync_SerializesSendsWithoutBlockingAppend()
		{
			var sender = new BlockingSender();
			using (var pipeline = new SnipeLogPipeline(null, CreateTemporaryDirectory(), sender))
			{
				pipeline.Append(new SnipeLogRecord(1, LogType.Log, "first", string.Empty));
				Task<bool> firstSend = pipeline.SendAsync().AsTask();
				await sender.FirstSendStarted;

				Task<bool> secondSend = pipeline.SendAsync().AsTask();
				pipeline.Append(new SnipeLogRecord(2, LogType.Log, "second", string.Empty));
				Assert.AreEqual(1, sender.SendCount);

				sender.ReleaseFirst();
				Assert.IsTrue(await firstSend);
				Assert.IsTrue(await secondSend);
				Assert.AreEqual(2, sender.SendCount);
				StringAssert.Contains("second", sender.Contents[1]);
			}
		}

		[Test]
		public async Task SendBatchesAsync_StopsAfterFirstFailedPortion()
		{
			const string first = "{\"id\":1}";
			const string second = "{\"id\":2}";
			const string third = "{\"id\":3}";
			string prefix = LogSender.BuildBatchPrefix(1, null, 3, "v", "p");
			int maxBytes = Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(first) + Encoding.UTF8.GetByteCount("]}");
			var portions = new List<int>();
			var acknowledged = new List<int>();

			using (StreamReader reader = CreateReader(string.Join("\n", first, second, third)))
			{
				bool result = await LogSender.SendBatchesAsync(
					reader,
					1,
					null,
					3,
					"v",
					"p",
					maxBytes,
					(batch, portionIndex) =>
					{
						portions.Add(portionIndex);
						return UniTask.FromResult(portionIndex < 2);
					}, acknowledged.Add);

				Assert.IsFalse(result);
				CollectionAssert.AreEqual(new[] { 1, 2 }, portions);
				CollectionAssert.AreEqual(new[] { 1 }, acknowledged);
			}
		}

		[Test]
		public async Task SendBatchesAsync_AcknowledgesEveryRecordInSuccessfulPortions()
		{
			const string first = "{\"msg\":\"А\"}";
			const string second = "{\"msg\":\"Б\"}";
			const string third = "{\"msg\":\"В\"}";
			string prefix = LogSender.BuildBatchPrefix(1, null, 3, "v", "p");
			int maxBytes = Encoding.UTF8.GetByteCount(prefix + first + "," + second + "]}");
			var acknowledged = new List<int>();
			var contents = new List<string>();
			using (StreamReader reader = CreateReader("\n" + first + "\n\n" + second + "\n" + third + "\n\n"))
			{
				Assert.IsTrue(await LogSender.SendBatchesAsync(reader, 1, null, 3, "v", "p", maxBytes,
					(batch, portionIndex) =>
					{
						contents.Add(batch.Content);
						return UniTask.FromResult(true);
					}, acknowledged.Add));
			}
			CollectionAssert.AreEqual(new[] { 2, 1 }, acknowledged);
			CollectionAssert.AreEqual(new[] { prefix + first + "," + second + "]}", prefix + third + "]}" }, contents);
		}

		[Test]
		public void Dispose_DrainsQueuedRecordsAndIsIdempotent()
		{
			string directory = CreateTemporaryDirectory();
			var pipeline = new SnipeLogPipeline(null, directory, new RecordingSender(true));
			for (int i = 0; i < 100; i++)
			{
				pipeline.Append(new SnipeLogRecord(i, LogType.Log, $"record-{i:D3}", string.Empty));
			}

			pipeline.Dispose();
			pipeline.Dispose();

			string[] files = Directory.GetFiles(directory, "*.ndjson");
			Assert.AreEqual(1, files.Length);
			string content = File.ReadAllText(files[0]);
			for (int i = 0; i < 100; i++)
			{
				StringAssert.Contains($"record-{i:D3}", content);
			}
		}

		[TestCase(200, true, true)]
		[TestCase(299, true, true)]
		[TestCase(200, false, false)]
		[TestCase(199, true, false)]
		[TestCase(300, true, false)]
		[TestCase(400, false, false)]
		[TestCase(401, false, false)]
		[TestCase(403, false, false)]
		[TestCase(408, true, false)]
		[TestCase(429, false, false)]
		[TestCase(500, false, false)]
		public async Task PostJsonAsync_StopsOnSuccessOrNonRetryableResponse(int code, bool isSuccess, bool expected)
		{
			var response = new HttpResponseStub(code, isSuccess);
			var client = new HttpClientStub(response);
			var delays = new List<int>();

			bool result = await LogSender.PostJsonAsync(client, new Uri("https://example.com/log"), "{}",
				TimeSpan.FromSeconds(5), delay => RecordDelay(delays, delay));

			Assert.AreEqual(expected, result);
			Assert.AreEqual(1, client.Contents.Count);
			Assert.IsEmpty(delays);
			Assert.AreEqual(1, response.DisposeCount);
		}

		[TestCase("exception")]
		[TestCase("null")]
		[TestCase("zero")]
		[TestCase("negative")]
		[TestCase("timeout")]
		public async Task PostJsonAsync_RetriesTransientFailuresWithSamePayloadAndTimeout(string failure)
		{
			object first = CreateHttpFailure(failure);
			object second = CreateHttpFailure(failure);
			var success = new HttpResponseStub(200, true);
			var client = new HttpClientStub(first, second, success);
			var delays = new List<int>();
			var url = new Uri("https://example.com/log");
			TimeSpan timeout = LogSender.GetSendProfile(RuntimePlatform.WebGLPlayer).RequestTimeout;
			const string content = "{\"msg\":\"Привет\"}";

			bool result = await LogSender.PostJsonAsync(client, url, content, timeout, delay =>
			{
				AssertDisposed(first);
				if (delays.Count > 0)
				{
					AssertDisposed(second);
				}
				return RecordDelay(delays, delay);
			});

			Assert.IsTrue(result);
			CollectionAssert.AreEqual(new[] { 500, 1000 }, delays);
			CollectionAssert.AreEqual(new[] { content, content, content }, client.Contents);
			CollectionAssert.AreEqual(new[] { url, url, url }, client.Urls);
			CollectionAssert.AreEqual(new[] { timeout, timeout, timeout }, client.Timeouts);
			AssertDisposed(first);
			AssertDisposed(second);
			AssertDisposed(success);
		}

		[TestCase("exception")]
		[TestCase("null")]
		[TestCase("zero")]
		[TestCase("negative")]
		[TestCase("timeout")]
		public async Task PostJsonAsync_StopsAfterThreeFailedAttempts(string failure)
		{
			object first = CreateHttpFailure(failure);
			object second = CreateHttpFailure(failure);
			object third = CreateHttpFailure(failure);
			var client = new HttpClientStub(first, second, third);
			var delays = new List<int>();

			Assert.IsFalse(await LogSender.PostJsonAsync(client, new Uri("https://example.com/log"), "{}",
				TimeSpan.FromSeconds(5), delay => RecordDelay(delays, delay)));

			Assert.AreEqual(3, client.Contents.Count);
			CollectionAssert.AreEqual(new[] { 500, 1000 }, delays);
			AssertDisposed(first);
			AssertDisposed(second);
			AssertDisposed(third);
		}

		[Test]
		public async Task PostJsonAsync_StopsRetryingWhenServerRejectsNextAttempt()
		{
			var failure = new HttpResponseStub(0, false);
			var rejection = new HttpResponseStub(401, false);
			var client = new HttpClientStub(failure, rejection);
			var delays = new List<int>();

			Assert.IsFalse(await LogSender.PostJsonAsync(client, new Uri("https://example.com/log"), "{}",
				TimeSpan.FromSeconds(5), delay => RecordDelay(delays, delay)));

			Assert.AreEqual(2, client.Contents.Count);
			CollectionAssert.AreEqual(new[] { 500 }, delays);
			AssertDisposed(failure);
			AssertDisposed(rejection);
		}

		[UnityTest]
		public IEnumerator PostJsonAsync_RetriesWhileTimeScaleIsZero()
		{
			float previousTimeScale = Time.timeScale;
			var first = new HttpResponseStub(0, false);
			var second = new HttpResponseStub(408, false);
			var success = new HttpResponseStub(200, true);
			var client = new HttpClientStub(first, second, success);
			try
			{
				Time.timeScale = 0;
				Task<bool> send = LogSender.PostJsonAsync(client, new Uri("https://example.com/log"), "{}",
					TimeSpan.FromSeconds(5)).AsTask();
				var elapsed = System.Diagnostics.Stopwatch.StartNew();
				while (!send.IsCompleted && elapsed.Elapsed < TimeSpan.FromSeconds(10))
				{
					yield return null;
				}

				Assert.IsTrue(send.IsCompleted, "Retries stalled with timeScale = 0.");
				Assert.IsTrue(send.GetAwaiter().GetResult());
				Assert.AreEqual(3, client.Contents.Count);
				AssertDisposed(first);
				AssertDisposed(second);
				AssertDisposed(success);
			}
			finally
			{
				Time.timeScale = previousTimeScale;
			}
		}

		private static UniTask RecordDelay(List<int> delays, int delay)
		{
			delays.Add(delay);
			return UniTask.CompletedTask;
		}

		private static object CreateHttpFailure(string failure)
		{
			switch (failure)
			{
				case "exception": return new IOException("Transient network failure");
				case "null": return null;
				case "zero": return new HttpResponseStub(0, false);
				case "negative": return new HttpResponseStub(-1, false);
				case "timeout": return new HttpResponseStub(408, false);
				default: throw new ArgumentOutOfRangeException(nameof(failure));
			}
		}

		private static void AssertDisposed(object response)
		{
			if (response is HttpResponseStub stub)
			{
				Assert.AreEqual(1, stub.DisposeCount);
			}
		}

		private sealed class HttpClientFactoryStub : IHttpClientFactory
		{
			private readonly IHttpClient _client;
			internal HttpClientFactoryStub(IHttpClient client) => _client = client;
			public IHttpClient CreateHttpClient() => _client;
		}

		private sealed class HttpClientStub : IHttpClient
		{
			private readonly Queue<object> _results;
			internal readonly List<string> Contents = new List<string>();
			internal readonly List<Uri> Urls = new List<Uri>();
			internal readonly List<TimeSpan> Timeouts = new List<TimeSpan>();

			internal HttpClientStub(params object[] results)
			{
				_results = new Queue<object>(results);
			}

			public UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout, CancellationToken cancellationToken = default)
			{
				Contents.Add(content);
				Urls.Add(uri);
				Timeouts.Add(timeout);
				Assert.IsNotEmpty(_results, "Unexpected extra HTTP attempt.");
				object result = _results.Dequeue();
				if (result is Exception exception)
				{
					return UniTask.FromException<IHttpClientResponse>(exception);
				}
				return UniTask.FromResult((IHttpClientResponse)result);
			}

			public void Reset() { }
			public void SetAuthToken(string token) { }
			public void SetPersistentClientId(string token) { }
			public UniTask<IHttpClientResponse> Get(Uri uri, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		}

		private sealed class HttpResponseStub : IHttpClientResponse
		{
			public long ResponseCode { get; }
			public bool IsSuccess { get; }
			public string Error => IsSuccess ? null : "HTTP failure";
			internal int DisposeCount;

			internal HttpResponseStub(long responseCode, bool isSuccess)
			{
				ResponseCode = responseCode;
				IsSuccess = isSuccess;
			}

			public void Dispose() => DisposeCount++;
			public UniTask<string> GetStringContentAsync() => throw new NotSupportedException();
			public UniTask<byte[]> GetBinaryContentAsync() => throw new NotSupportedException();
		}

		private string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "snipe-log-pipeline-tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			_temporaryDirectories.Add(directory);
			return directory;
		}

		private static SnipeOptions CreateOptions()
		{
			return new SnipeOptionsBuilder().Build(0, new NullSnipeServices());
		}

		private static SnipeLogRecord CreateRecord(string message)
		{
			return new SnipeLogRecord(1, LogType.Log, message, string.Empty);
		}

		private static void AppendRecords(SnipeLogPipeline pipeline, params string[] messages)
		{
			foreach (string message in messages)
			{
				pipeline.Append(CreateRecord(message));
			}
		}

		private static string BuildExpectedBatch(string message)
		{
			return LogSender.BuildBatchPrefix(1, null, 3, "v", "p") +
				SnipeLogPipeline.SerializeRecord(CreateRecord(message)) + "]}";
		}

		private static int CountRecords(string content)
		{
			int count = 0;
			using (StreamReader reader = CreateReader(content))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					if (line.Length > 0)
					{
						count++;
					}
				}
			}
			return count;
		}

		private static StreamReader CreateReader(string content)
		{
			byte[] bytes = new UTF8Encoding(false).GetBytes(content);
			return new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false));
		}

		private static int CountNonEmptyLogFiles(string directory)
		{
			int count = 0;
			string[] files = Directory.GetFiles(directory, "*.ndjson");
			for (int i = 0; i < files.Length; i++)
			{
				if (new FileInfo(files[i]).Length > 0)
				{
					count++;
				}
			}

			return count;
		}

		private sealed class ContextFactoryStub : AbstractSnipeApiContextFactory
		{
			internal ContextFactoryStub()
				: base(null, null, null)
			{
			}

			internal ILogReporter CreateReporter()
			{
				return CreateLogReporter();
			}

			public override TimeSpan GetServerTimeZoneOffset()
			{
				return TimeSpan.Zero;
			}

			public override AbstractSnipeApiService CreateSnipeApiService(ISnipeCommunicator communicator, AuthSubsystem auth)
			{
				return null;
			}
		}

		private sealed class ContextStub : SnipeContext
		{
			internal ContextStub(SnipeOptions options, ILogReporter reporter, ISnipeCommunicator communicator = null)
				: base(0, options, communicator, null, reporter)
			{
			}

			internal void Reinitialize(SnipeOptions options)
			{
				Reconfigure(options);
			}

			public override void Dispose()
			{
				LogReporter.Dispose();
			}
		}

		private sealed class ReporterFactoryStub : ILogReporterFactory
		{
			private readonly ILogReporter _reporter;

			internal ReporterFactoryStub(ILogReporter reporter)
			{
				_reporter = reporter;
			}

			public ILogReporter CreateLogReporter()
			{
				return _reporter;
			}
		}

		private sealed class ReporterStub : ILogReporter
		{
			internal int InitializeCount { get; private set; }
			internal int SendCount { get; private set; }
			internal int DisposeCount { get; private set; }
			internal SnipeContext LastContext { get; private set; }
			internal SnipeOptions LastOptions { get; private set; }

			public void Initialize(SnipeContext context, SnipeOptions options)
			{
				InitializeCount++;
				LastContext = context;
				LastOptions = options;
			}

			public UniTask<bool> SendAsync()
			{
				SendCount++;
				return UniTask.FromResult(true);
			}

			public void Dispose()
			{
				DisposeCount++;
			}
		}

		private sealed class PipelineStub : ISnipeLogPipeline
		{
			internal int InitializeCount { get; private set; }
			internal int SendCount { get; private set; }
			internal int DisposeCount { get; private set; }

			public void Initialize(SnipeContext context, SnipeOptions options)
			{
				InitializeCount++;
			}

			public void Append(SnipeLogRecord record)
			{
			}

			public UniTask<bool> SendAsync()
			{
				SendCount++;
				return UniTask.FromResult(true);
			}

			public void Dispose()
			{
				DisposeCount++;
			}
		}

		private sealed class BatchedSender : ILogFileSender
		{
			private readonly Queue<object> _results;
			internal List<string> Contents { get; } = new List<string>();
			internal Action BeforeSend;

			internal BatchedSender(params object[] results)
			{
				_results = new Queue<object>(results);
			}

			public UniTask<bool> SendAsync(StreamReader file, Action<int> acknowledgeRecords)
			{
				return LogSender.SendBatchesAsync(file, 1, null, 3, "v", "p",
					Encoding.UTF8.GetByteCount(BuildExpectedBatch("A")),
					(batch, portionIndex) =>
					{
						BeforeSend?.Invoke();
						Contents.Add(batch.Content);
						object result = _results.Count > 0 ? _results.Dequeue() : true;
						if (result is Exception exception)
						{
							throw exception;
						}
						return UniTask.FromResult((bool)result);
					}, acknowledgeRecords);
			}
		}

		private sealed class RecordingSender : ILogFileSender
		{
			private readonly Queue<bool> _results;

			internal List<string> Contents { get; } = new List<string>();

			internal RecordingSender(params bool[] results)
			{
				_results = new Queue<bool>(results);
			}

			public UniTask<bool> SendAsync(StreamReader file, Action<int> acknowledgeRecords)
			{
				string content = file.ReadToEnd();
				Contents.Add(content);
				bool success = _results.Count == 0 || _results.Dequeue();
				if (success)
				{
					acknowledgeRecords(CountRecords(content));
				}
				return UniTask.FromResult(success);
			}
		}

		private sealed class BlockingSender : ILogFileSender
		{
			private readonly UniTaskCompletionSource<bool> _firstSend = new UniTaskCompletionSource<bool>();
			private readonly TaskCompletionSource<bool> _firstSendStarted =
				new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			internal Task FirstSendStarted => _firstSendStarted.Task;
			internal int SendCount { get; private set; }
			internal List<string> Contents { get; } = new List<string>();

			public async UniTask<bool> SendAsync(StreamReader file, Action<int> acknowledgeRecords)
			{
				string content = file.ReadToEnd();
				Contents.Add(content);
				SendCount++;
				if (SendCount == 1)
				{
					_firstSendStarted.TrySetResult(true);
					bool success = await _firstSend.Task;
					if (!success)
					{
						return false;
					}
				}

				acknowledgeRecords(CountRecords(content));
				return true;
			}

			internal void ReleaseFirst()
			{
				_firstSend.TrySetResult(true);
			}
		}
	}
}
