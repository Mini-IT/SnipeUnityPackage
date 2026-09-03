using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe.Api;
using MiniIT.Snipe.Configuration;
using MiniIT.Snipe.Internal;
using NUnit.Framework;
using UnityEngine;

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
			using (var pipeline = new SnipeLogPipeline(42, CreateTemporaryDirectory(), sender))
			{
				pipeline.Append(new SnipeLogRecord(1, LogType.Log, "first", string.Empty));
				Assert.IsFalse(await pipeline.SendAsync());
				Assert.AreEqual(1, pipeline.GetFilesReadyToSend().Length);

				pipeline.Append(new SnipeLogRecord(2, LogType.Warning, "second", string.Empty));
				Assert.IsTrue(await pipeline.SendAsync());

				Assert.AreEqual(3, sender.Contents.Count);
				StringAssert.Contains("first", sender.Contents[0]);
				StringAssert.Contains("first", sender.Contents[1]);
				StringAssert.Contains("second", sender.Contents[2]);
				Assert.AreEqual(0, pipeline.GetFilesReadyToSend().Length);
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
				await WaitUntilAsync(() => sender.SendCount == 1);

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
					});

				Assert.IsFalse(result);
				CollectionAssert.AreEqual(new[] { 1, 2 }, portions);
			}
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

		private static StreamReader CreateReader(string content)
		{
			byte[] bytes = new UTF8Encoding(false).GetBytes(content);
			return new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false));
		}

		private static async Task WaitUntilAsync(Func<bool> condition)
		{
			for (int i = 0; i < 100 && !condition(); i++)
			{
				await Task.Yield();
			}

			Assert.IsTrue(condition());
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
			internal ContextStub(SnipeOptions options, ILogReporter reporter)
				: base(0, options, null, null, reporter)
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

			public ILogReporter Create()
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

		private sealed class RecordingSender : ILogFileSender
		{
			private readonly Queue<bool> _results;

			internal List<string> Contents { get; } = new List<string>();

			internal RecordingSender(params bool[] results)
			{
				_results = new Queue<bool>(results);
			}

			public UniTask<bool> SendAsync(StreamReader file)
			{
				Contents.Add(file.ReadToEnd());
				return UniTask.FromResult(_results.Count == 0 || _results.Dequeue());
			}
		}

		private sealed class BlockingSender : ILogFileSender
		{
			private readonly UniTaskCompletionSource<bool> _firstSend = new UniTaskCompletionSource<bool>();

			internal int SendCount { get; private set; }
			internal List<string> Contents { get; } = new List<string>();

			public async UniTask<bool> SendAsync(StreamReader file)
			{
				Contents.Add(file.ReadToEnd());
				SendCount++;
				if (SendCount == 1)
				{
					return await _firstSend.Task;
				}

				return true;
			}

			internal void ReleaseFirst()
			{
				_firstSend.TrySetResult(true);
			}
		}
	}
}
