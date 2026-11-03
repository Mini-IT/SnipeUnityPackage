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

		[TestCase(0, 20, 20, 30, 2)]
		[TestCase(10, 9, 20, 30, 2)]
		[TestCase(10, 20, 9, 30, 2)]
		[TestCase(10, 20, 20, 19, 2)]
		[TestCase(10, 20, 20, 30, 0)]
		public void Limits_RejectInvalidValues(int record, int queue, int file, long cache, int files)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new SnipeLogBufferLimits(record, queue, file, cache, files));
		}

		[Test]
		public void Limits_RejectConflictingDirectoryBudgets()
		{
			string directory = CreateTemporaryDirectory();
			using (var first = new SnipeLogFileBuffer(directory, limits: new SnipeLogBufferLimits(10, 20, 20, 30, 2), startWriter: false))
			{
				Assert.Throws<ArgumentException>(() => new SnipeLogFileBuffer(Path.Combine(directory, "."),
					limits: new SnipeLogBufferLimits(10, 20, 20, 40, 2), startWriter: false));
			}
		}

		[Test]
		public void SerializedSize_UsesExactUtf8BytesAndPreservesEnvelope()
		{
			var empty = CreateRecord("");
			Assert.IsNull(SnipeLogPipeline.SerializeLimitedRecord(empty, 44, out _));
			Assert.AreEqual(45, SnipeLogPipeline.SerializeLimitedRecord(empty, 45, out bool emptyTruncated).Length);
			Assert.IsFalse(emptyTruncated);
			var record = new SnipeLogRecord(long.MinValue, LogType.Exception, "\"\\\b\f\n\r\t\u0001А😀\ud800x\udc00", "stack☃");
			byte[] expected = Encoding.UTF8.GetBytes(SnipeLogPipeline.SerializeRecord(record) + "\n");
			for (int limit = 45; limit <= expected.Length + 1; limit++)
			{
				byte[] data = SnipeLogPipeline.SerializeLimitedRecord(record, limit, out bool truncated);
				if (data == null) continue; // The longer timestamp and level also belong to the envelope.
				Assert.LessOrEqual(data.Length, limit);
				string json = new UTF8Encoding(false, true).GetString(data);
				Assert.AreEqual('\n', json[json.Length - 1]);
				var decoded = UnityEngine.JsonUtility.FromJson<DecodedRecord>(json);
				Assert.AreEqual(long.MinValue, decoded.time);
				Assert.AreEqual("Exception", decoded.level);
				Assert.AreEqual(limit < expected.Length, truncated);
				if (!truncated) CollectionAssert.AreEqual(expected, data);
			}
		}

		[Test]
		public async Task Append_TruncatesOversizedRecordToValidNdjson()
		{
			var sender = new RecordingSender(true);
			using (var pipeline = new SnipeLogPipeline(null, CreateTemporaryDirectory(), sender,
				new SnipeLogBufferLimits(54, 108, 108, 216, 4)))
			{
				pipeline.Append(new SnipeLogRecord(1, LogType.Log, "А😀\"" + new string('x', 1000000), "stack"));
				Assert.IsTrue(await pipeline.SendAsync());
				Assert.AreEqual(1, sender.Contents.Count);
				Assert.AreEqual(54, Encoding.UTF8.GetByteCount(sender.Contents[0]));
				var decoded = UnityEngine.JsonUtility.FromJson<DecodedRecord>(sender.Contents[0]);
				Assert.AreEqual("А😀\"x", decoded.msg);
				Assert.AreEqual("", decoded.stack);
			}
			byte[] split = SnipeLogPipeline.SerializeLimitedRecord(new SnipeLogRecord(1, LogType.Log, "A😀B", "XY"), 49, out bool truncated);
			Assert.IsTrue(truncated);
			var prefix = UnityEngine.JsonUtility.FromJson<DecodedRecord>(Encoding.UTF8.GetString(split));
			Assert.AreEqual("A", prefix.msg);
			Assert.AreEqual("XY", prefix.stack);
		}

		[Test]
		public async Task QueueLimit_RejectsNewestAndCountsEntireActiveArray()
		{
			using (var buffer = new SnipeLogFileBuffer(CreateTemporaryDirectory(),
				limits: new SnipeLogBufferLimits(10, 20, 20, 40, 4), startWriter: false))
			{
				Assert.IsTrue(buffer.Append(Encoding.UTF8.GetBytes("111111111\n")));
				Assert.IsTrue(buffer.Append(Encoding.UTF8.GetBytes("222222222\n")));
				Assert.IsFalse(buffer.Append(Encoding.UTF8.GetBytes("discarded\n")));
				buffer.ProcessFrame(3, 64);
				Assert.AreEqual(20, buffer.QueuedBytes);
				Assert.IsFalse(buffer.Append(Encoding.UTF8.GetBytes("discarded\n")));
				buffer.ProcessFrame(7, 64);
				Assert.AreEqual(10, buffer.QueuedBytes);
				Assert.IsTrue(buffer.Append(Encoding.UTF8.GetBytes("333333333\n")));
				Task<SnipeLogCacheDirectory.SendBatch> seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(100, 64);
				using (var batch = await seal)
				{
					Assert.AreEqual(2, batch.Files.Length);
					Assert.AreEqual("111111111\n222222222\n", File.ReadAllText(batch.Files[0].Path));
					Assert.AreEqual("333333333\n", File.ReadAllText(batch.Files[1].Path));
					Assert.AreEqual(20, new FileInfo(batch.Files[0].Path).Length);
					Assert.AreEqual(10, new FileInfo(batch.Files[1].Path).Length);
				}
				Assert.AreEqual(0, buffer.QueuedBytes);
			}
		}

		[Test]
		public async Task QueueLimit_ReleasesBytesAfterFailureAndDispose()
		{
			string directory = Path.Combine(CreateTemporaryDirectory(), "blocked");
			File.WriteAllText(directory, "not a directory");
			using (var buffer = new SnipeLogFileBuffer(directory,
				limits: new SnipeLogBufferLimits(10, 20, 10, 10, 1), startWriter: false))
			{
				Assert.IsTrue(buffer.Append(new byte[10]));
				buffer.ProcessFrame(100, 64);
				Assert.AreEqual(0, buffer.QueuedBytes);
				File.Delete(directory);
				Directory.CreateDirectory(directory);
				Assert.IsTrue(buffer.Append(Encoding.UTF8.GetBytes("recovered\n")));
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(100, 64);
				using (var batch = await seal) Assert.AreEqual("recovered\n", File.ReadAllText(batch.Files[0].Path));
				Assert.IsTrue(buffer.Append(new byte[10]));
				buffer.Dispose();
				Assert.AreEqual(0, buffer.QueuedBytes);
				Assert.AreEqual(0, buffer.Cache.UsedBytes);
			}
		}

		[Test]
		public async Task CacheLimit_EvictsOwnOldestClosedFileAndBoundsFileCount()
		{
			using (var buffer = new SnipeLogFileBuffer(CreateTemporaryDirectory(),
				limits: new SnipeLogBufferLimits(10, 40, 10, 30, 3), startWriter: false))
			{
				foreach (string record in new[] { "111111111\n", "222222222\n", "333333333\n", "444444444\n" })
					Assert.IsTrue(buffer.Append(Encoding.UTF8.GetBytes(record)));
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(100, 64);
				using (var batch = await seal)
				{
					Assert.AreEqual(3, batch.Files.Length);
					Assert.AreEqual("222222222\n", File.ReadAllText(batch.Files[0].Path));
					Assert.AreEqual("444444444\n", File.ReadAllText(batch.Files[2].Path));
				}
				Assert.AreEqual(30, buffer.Cache.UsedBytes);
				Assert.AreEqual(3, buffer.Cache.FileCount);
			}
		}

		[Test]
		public async Task CacheLimit_ReservesAcrossOwnersWithoutDeletingLiveOwnersFiles()
		{
			string directory = CreateTemporaryDirectory();
			var limits = new SnipeLogBufferLimits(20, 40, 20, 30, 4);
			using (var first = new SnipeLogFileBuffer(directory, limits: limits, startWriter: false))
			using (var second = new SnipeLogFileBuffer(Path.Combine(directory, "."), limits: limits, startWriter: false))
			{
				first.Append(new byte[20]);
				second.Append(new byte[20]);
				await Task.WhenAll(Task.Run(() => first.ProcessFrame(100, 64)), Task.Run(() => second.ProcessFrame(100, 64)));
				Assert.AreSame(first.Cache, second.Cache);
				Assert.AreEqual(20, first.Cache.UsedBytes);
				Assert.AreEqual(1, first.Cache.FileCount);
				var firstSeal = first.SealCurrentFileAsync().AsTask();
				var secondSeal = second.SealCurrentFileAsync().AsTask();
				first.ProcessFrame(100, 64);
				second.ProcessFrame(100, 64);
				using (var a = await firstSeal)
				using (var b = await secondSeal)
				{
					Assert.AreEqual(1, a.Files.Length + b.Files.Length);
					var loser = a.Files.Length == 0 ? first : second;
					var winner = a.Files.Length == 0 ? b : a;
					loser.Append(new byte[20]);
					loser.ProcessFrame(100, 64);
					Assert.IsTrue(File.Exists(winner.Files[0].Path));
					Assert.AreEqual(20, first.Cache.UsedBytes);
				}
			}
		}

		[Test]
		public async Task Seal_PinsAllSelectedFilesAndExcludesLaterRotations()
		{
			using (var buffer = new SnipeLogFileBuffer(CreateTemporaryDirectory(),
				limits: new SnipeLogBufferLimits(10, 40, 10, 30, 3), startWriter: false))
			{
				buffer.Append(Encoding.UTF8.GetBytes("111111111\n"));
				buffer.Append(Encoding.UTF8.GetBytes("222222222\n"));
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.Append(Encoding.UTF8.GetBytes("333333333\n"));
				buffer.Append(Encoding.UTF8.GetBytes("444444444\n"));
				buffer.ProcessFrame(100, 64);
				using (var batch = await seal)
				{
					Assert.AreEqual(2, batch.Files.Length);
					Assert.AreEqual("111111111\n", File.ReadAllText(batch.Files[0].Path));
					Assert.AreEqual("222222222\n", File.ReadAllText(batch.Files[1].Path));
					Assert.AreEqual(30, buffer.Cache.UsedBytes);
					foreach (var file in batch.Files) Assert.IsTrue(batch.DeleteSentFile(file));
				}
				var nextSeal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(100, 64);
				using (var next = await nextSeal)
				{
					Assert.AreEqual(1, next.Files.Length);
					Assert.AreEqual("444444444\n", File.ReadAllText(next.Files[0].Path));
				}
			}
		}

		[Test]
		public async Task CacheLimit_DropsIncomingWhenAllSelectedFilesFillCache()
		{
			var sender = new BlockingSender();
			string directory = CreateTemporaryDirectory();
			using (var pipeline = new SnipeLogPipeline(null, directory, sender, new SnipeLogBufferLimits(46, 184, 46, 92, 2)))
			{
				AppendRecords(pipeline, "A", "B");
				Task<bool> sending = pipeline.SendAsync().AsTask();
				await sender.FirstSendStarted;
				AppendRecords(pipeline, "C", "D");
				sender.ReleaseFirst();
				Assert.IsTrue(await sending);
				Assert.IsTrue(await pipeline.SendAsync());
				// Selected files survive; any new disk writes may be dropped or admitted
				// after successful deletion releases space, depending on writer scheduling.
				Assert.GreaterOrEqual(sender.Contents.Count, 2);
				StringAssert.Contains("\"msg\":\"A\"", sender.Contents[0]);
				StringAssert.Contains("\"msg\":\"B\"", sender.Contents[1]);
				Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
			}
		}

		[Test]
		public async Task CacheLimit_EvictionForgetsPartialAcknowledgement()
		{
			var sender = new BatchedSender(true, false, true, true);
			string directory = CreateTemporaryDirectory();
			using (var pipeline = new SnipeLogPipeline(null, directory, sender, new SnipeLogBufferLimits(46, 184, 92, 92, 2)))
			{
				AppendRecords(pipeline, "A", "B");
				Assert.IsFalse(await pipeline.SendAsync());
				string evictedPath = Directory.GetFiles(directory, "*.ndjson")[0];
				AppendRecords(pipeline, "C", "D");
				Assert.IsTrue(await pipeline.SendAsync());
				Assert.IsFalse(File.Exists(evictedPath));
				CollectionAssert.AreEqual(new[] { BuildExpectedBatch("A"), BuildExpectedBatch("B"), BuildExpectedBatch("C"), BuildExpectedBatch("D") }, sender.Contents);
			}
		}

		[Test]
		[Platform("Win")]
		public async Task CacheLimit_KeepsChargeAndProgressWhenDeleteFails()
		{
			using (var buffer = new SnipeLogFileBuffer(CreateTemporaryDirectory(),
				limits: new SnipeLogBufferLimits(10, 20, 10, 10, 1), startWriter: false))
			{
				buffer.Append(new byte[10]);
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(100, 64);
				SnipeLogCacheDirectory.FileEntry entry;
				using (var batch = await seal)
				{
					entry = batch.Files[0];
					batch.Acknowledge(entry, 1);
				}
				using (var blocker = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					buffer.Append(new byte[10]);
					buffer.ProcessFrame(100, 64);
					Assert.AreEqual(10, buffer.Cache.UsedBytes);
					Assert.AreEqual(1, buffer.Cache.FileCount);
					Assert.AreEqual(1, entry.AcknowledgedRecords);
					Assert.IsTrue(File.Exists(entry.Path));
				}
				buffer.Append(new byte[10]);
				buffer.ProcessFrame(100, 64);
				Assert.IsFalse(File.Exists(entry.Path));
				Assert.AreEqual(10, buffer.Cache.UsedBytes);
			}
		}

		[Test]
		[Platform("Win")]
		public async Task CacheLimit_CountsUndeletableOrphans()
		{
			string directory = CreateTemporaryDirectory();
			string orphan = Path.Combine(directory, "snipe-log-orphan.ndjson");
			File.WriteAllBytes(orphan, new byte[30]);
			using (var blocker = new FileStream(orphan, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (var buffer = new SnipeLogFileBuffer(directory,
				limits: new SnipeLogBufferLimits(10, 20, 10, 20, 2), startWriter: false))
			{
				Assert.AreEqual(30, buffer.Cache.UsedBytes);
				buffer.Append(new byte[10]);
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(100, 64);
				using (var batch = await seal) Assert.IsEmpty(batch.Files);
				Assert.AreEqual(1, Directory.GetFiles(directory).Length);
			}
			using (var replacement = new SnipeLogFileBuffer(directory, startWriter: false))
				Assert.AreEqual(0, replacement.Cache.UsedBytes);
		}

		[Test]
		public async Task CacheLimit_FileSlotsAndEmptySealsDoNotRequireReplacementFiles()
		{
			using (var buffer = new SnipeLogFileBuffer(CreateTemporaryDirectory(),
				limits: new SnipeLogBufferLimits(10, 30, 10, 100, 2), startWriter: false))
			{
				buffer.Append(new byte[10]);
				buffer.Append(new byte[10]);
				buffer.Append(new byte[10]);
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(100, 64);
				using (var batch = await seal)
				{
					Assert.AreEqual(2, batch.Files.Length);
					Assert.AreEqual(20, buffer.Cache.UsedBytes);
					foreach (var file in batch.Files) Assert.IsTrue(batch.DeleteSentFile(file));
				}
				for (int i = 0; i < 3; i++)
				{
					var emptySeal = buffer.SealCurrentFileAsync().AsTask();
					buffer.ProcessFrame(100, 64);
					using (var batch = await emptySeal) Assert.IsEmpty(batch.Files);
				}
				Assert.AreEqual(0, buffer.Cache.FileCount);
			}
		}

		[Test]
		public async Task ProcessFrame_SharesByteBudgetAcrossCommandsAndKeepsFifo()
		{
			using (var buffer = new SnipeLogFileBuffer(CreateTemporaryDirectory(),
				limits: new SnipeLogBufferLimits(6, 20, 20, 40, 4), startWriter: false))
			{
				buffer.Append(Encoding.UTF8.GetBytes("123456"));
				buffer.Append(Encoding.UTF8.GetBytes("78"));
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(4, 64);
				Assert.AreEqual(8, buffer.QueuedBytes);
				Assert.AreEqual(6, buffer.Cache.UsedBytes); // Full reservation, including unwritten bytes.
				buffer.ProcessFrame(4, 64);
				Assert.AreEqual(0, buffer.QueuedBytes);
				Assert.IsFalse(seal.IsCompleted);
				buffer.ProcessFrame(4, 64);
				using (var batch = await seal) Assert.AreEqual("12345678", File.ReadAllText(batch.Files[0].Path));
			}
		}

		[Test]
		public async Task ProcessFrame_WritesSeveralShortRecordsInOneFrame()
		{
			using (var buffer = new SnipeLogFileBuffer(CreateTemporaryDirectory(), startWriter: false))
			{
				for (int i = 0; i < 3; i++) buffer.Append(new byte[200]);
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.ProcessFrame(4096, 64);
				Assert.AreEqual(0, buffer.QueuedBytes);
				using (var batch = await seal) Assert.AreEqual(600, new FileInfo(batch.Files[0].Path).Length);
			}
		}

		[Test]
		public void ProcessFrame_BoundsCommandsWhenNoBytesCanBeWrittenAndStopsOnDispose()
		{
			string directory = CreateTemporaryDirectory();
			var limits = new SnipeLogBufferLimits(10, 40, 10, 10, 1);
			using (var first = new SnipeLogFileBuffer(directory, limits: limits, startWriter: false))
			using (var second = new SnipeLogFileBuffer(directory, limits: limits, startWriter: false))
			{
				first.Append(new byte[10]);
				first.ProcessFrame(3, 64);
				for (int i = 0; i < 3; i++) second.Append(new byte[10]);
				second.ProcessFrame(4096, 2);
				Assert.AreEqual(10, second.QueuedBytes);
				first.Dispose();
				Assert.AreEqual(0, first.QueuedBytes);
				Assert.AreEqual(0, first.Cache.UsedBytes);
				first.ProcessFrame(4096, 64);
				Assert.AreEqual(0, Directory.GetFiles(directory).Length);
				second.ProcessFrame(4096, 64);
				Assert.AreEqual(10, second.Cache.UsedBytes);
			}
		}

		[Test]
		public async Task CacheLimit_FullPinnedSnapshotRejectsNewDiskRecords()
		{
			using (var buffer = new SnipeLogFileBuffer(CreateTemporaryDirectory(),
				limits: new SnipeLogBufferLimits(10, 40, 10, 20, 2), startWriter: false))
			{
				buffer.Append(Encoding.UTF8.GetBytes("111111111\n"));
				buffer.Append(Encoding.UTF8.GetBytes("222222222\n"));
				var seal = buffer.SealCurrentFileAsync().AsTask();
				buffer.Append(Encoding.UTF8.GetBytes("333333333\n"));
				buffer.Append(Encoding.UTF8.GetBytes("444444444\n"));
				buffer.ProcessFrame(100, 64);
				using (var batch = await seal)
				{
					Assert.AreEqual(2, batch.Files.Length);
					Assert.AreEqual("111111111\n", File.ReadAllText(batch.Files[0].Path));
					Assert.AreEqual("222222222\n", File.ReadAllText(batch.Files[1].Path));
					Assert.AreEqual(20, buffer.Cache.UsedBytes);
					Assert.AreEqual(0, buffer.QueuedBytes);
					buffer.Dispose();
					Assert.IsTrue(File.Exists(batch.Files[0].Path));
					Assert.IsTrue(File.Exists(batch.Files[1].Path));
					Assert.AreEqual(20, buffer.Cache.UsedBytes);
				}
				Assert.AreEqual(0, buffer.Cache.UsedBytes);
				Assert.AreEqual(0, buffer.Cache.FileCount);
			}
		}

		[Test]
		[Platform("Win")]
		public void CacheLimit_FailedPartialFileRetainsReservationAndCannotBeSent()
		{
			string directory = CreateTemporaryDirectory();
			const string owner = "snipe-log-partial-";
			var cache = SnipeLogCacheDirectory.Register(directory, owner, new SnipeLogBufferLimits(10, 20, 20, 20, 2));
			try
			{
				string partial = Path.Combine(directory, owner + "0.ndjson");
				Assert.IsTrue(cache.TryReserve(owner, partial, null, 20, out var file, out _));
				File.WriteAllBytes(partial, new byte[3]);
				using (var blocker = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					cache.DiscardFailedFile(file);
					Assert.AreEqual(20, cache.UsedBytes);
					Assert.AreEqual(1, cache.FileCount);
					using (var batch = cache.AcquireClosedFiles(owner)) Assert.IsEmpty(batch.Files);
					Assert.IsFalse(cache.TryReserve(owner, Path.Combine(directory, owner + "1.ndjson"), null, 10, out _, out _));
				}
				Assert.IsTrue(cache.TryReserve(owner, Path.Combine(directory, owner + "1.ndjson"), null, 10, out _, out _));
				Assert.AreEqual(10, cache.UsedBytes);
				Assert.IsFalse(File.Exists(partial));
			}
			finally { cache.ReleaseOwner(owner); }
		}

		[Serializable]
		private sealed class DecodedRecord
		{
#pragma warning disable CS0649
			public long time;
			public string level;
			public string msg;
			public string stack;
#pragma warning restore CS0649
		}

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
		public async Task Dispose_DeletesOwnFilesAndIsIdempotent()
		{
			string directory = CreateTemporaryDirectory();
			var sender = new RecordingSender(false);
			using var pipeline = new SnipeLogPipeline(null, directory, sender);
			pipeline.Append(CreateRecord("retained"));
			Assert.IsFalse(await pipeline.SendAsync());
			Assert.AreEqual(1, CountNonEmptyLogFiles(directory));
			for (int i = 0; i < 100; i++)
			{
				pipeline.Append(new SnipeLogRecord(i, LogType.Log, $"record-{i:D3}", string.Empty));
			}

			pipeline.Dispose();
			pipeline.Dispose();
			pipeline.Append(CreateRecord("ignored"));
			Assert.IsFalse(await pipeline.SendAsync());
			using (var context = new ContextStub(CreateOptions(), new ReporterStub()))
			{
				Assert.Throws<ObjectDisposedException>(() => pipeline.Initialize(context, CreateOptions()));
			}
			Assert.AreEqual(1, sender.Contents.Count);
			Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Dispose_HandlesEmptyAndUninitializedPipeline(bool append)
		{
			string directory = CreateTemporaryDirectory();
			using var pipeline = new SnipeLogPipeline(null, directory, null);
			if (append)
			{
				pipeline.Append(CreateRecord("never-initialized"));
			}
			pipeline.Dispose();
			pipeline.Dispose();
			Assert.IsFalse(await pipeline.SendAsync());
			Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
		}

		[Test]
		public async Task Dispose_DeletesPartiallyAcknowledgedFiles()
		{
			string directory = CreateTemporaryDirectory();
			var sender = new BatchedSender(true, false);
			using (var pipeline = new SnipeLogPipeline(null, directory, sender))
			{
				AppendRecords(pipeline, "A", "B");
				Assert.IsFalse(await pipeline.SendAsync());
				Assert.AreEqual(1, CountNonEmptyLogFiles(directory));
			}
			Assert.AreEqual(2, sender.Contents.Count);
			Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
		}

		[Test]
		public async Task ConstructorCleanup_DeletesOrphansWithoutSendingOrTouchingOtherFiles()
		{
			string directory = CreateTemporaryDirectory();
			for (int i = 0; i < 5; i++)
			{
				File.WriteAllText(Path.Combine(directory, $"snipe-log-old-{i}.ndjson"), "orphan");
			}
			string sentinel = Path.Combine(directory, "sentinel.txt");
			File.WriteAllText(sentinel, "keep-me");
			string nested = Path.Combine(directory, "nested");
			Directory.CreateDirectory(nested);
			string nestedFile = Path.Combine(nested, "snipe-log-nested.ndjson");
			File.WriteAllText(nestedFile, "nested-data");
			var sender = new RecordingSender(true);
			using (var pipeline = new SnipeLogPipeline(null, directory, sender))
			{
				Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
				pipeline.Append(CreateRecord("new-record"));
				Assert.IsTrue(await pipeline.SendAsync());
				Assert.AreEqual(1, sender.Contents.Count);
				StringAssert.Contains("new-record", sender.Contents[0]);
				StringAssert.DoesNotContain("orphan", sender.Contents[0]);
			}
			Assert.AreEqual("keep-me", File.ReadAllText(sentinel));
			Assert.AreEqual("nested-data", File.ReadAllText(nestedFile));
		}

		[Test]
		public async Task SharedDirectory_PreservesLiveBuffersAndSendOrder()
		{
			string directory = CreateTemporaryDirectory();
			var firstSender = new RecordingSender(false, false, false, false);
			using (var first = new SnipeLogPipeline(null, directory, firstSender))
			{
				foreach (string message in new[] { "A", "B", "C", "D" })
				{
					first.Append(CreateRecord(message));
					Assert.IsFalse(await first.SendAsync());
				}
				Assert.AreEqual(4, CountNonEmptyLogFiles(directory));
				string[] ownedPaths = Directory.GetFiles(directory, "*.ndjson");
				var retainedFiles = new Dictionary<string, string>();
				foreach (string path in ownedPaths)
				{
					if (new FileInfo(path).Length > 0)
					{
						retainedFiles.Add(path, File.ReadAllText(path));
					}
				}
				using (var second = new SnipeLogPipeline(null, Path.Combine(directory, ".") + Path.DirectorySeparatorChar,
					new RecordingSender(true)))
				{
					second.Append(CreateRecord("second-owner"));
					Assert.IsTrue(await second.SendAsync());
				}
				foreach (string path in ownedPaths)
				{
					Assert.IsTrue(File.Exists(path), "A live owner must retain its current and sealed files.");
				}
				foreach (var file in retainedFiles)
				{
					Assert.AreEqual(file.Value, File.ReadAllText(file.Key));
				}
				Assert.IsTrue(await first.SendAsync());
				Assert.AreEqual(8, firstSender.Contents.Count);
				for (int i = 0; i < 4; i++)
				{
					StringAssert.Contains($"\"msg\":\"{(char)('A' + i)}\"", firstSender.Contents[4 + i]);
				}
			}
			Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task DisposeDuringSend_PreservesReaderAndOwnerUntilCompletion(bool success)
		{
			string directory = CreateTemporaryDirectory();
			var sender = new BlockingSender();
			using (var pipeline = new SnipeLogPipeline(null, directory, sender))
			{
				pipeline.Append(CreateRecord("in-flight"));
				Task<bool> sending = pipeline.SendAsync().AsTask();
				await sender.FirstSendStarted;
				Task<bool> queued = pipeline.SendAsync().AsTask();
				try
				{
					pipeline.Dispose();
					pipeline.Dispose();
					Assert.IsFalse(sending.IsCompleted);
					Assert.AreEqual(1, CountNonEmptyLogFiles(directory));
					using (var replacement = new SnipeLogPipeline(null, directory, new RecordingSender(false)))
					{
						Assert.AreEqual(1, CountNonEmptyLogFiles(directory));
						replacement.Append(CreateRecord("replacement"));
						Assert.IsFalse(await replacement.SendAsync());
						sender.ReleaseFirst(success);
						Assert.AreEqual(success, await sending);
						Assert.IsFalse(await queued);
						Assert.IsFalse(await pipeline.SendAsync());
						Assert.AreEqual(1, sender.SendCount);
						Assert.AreEqual(1, CountNonEmptyLogFiles(directory));
						string replacementFile = Array.Find(Directory.GetFiles(directory, "*.ndjson"),
							path => new FileInfo(path).Length > 0);
						StringAssert.Contains("replacement", File.ReadAllText(replacementFile));
						StringAssert.Contains("in-flight", sender.Contents[0]);
					}
				}
				finally
				{
					sender.ReleaseFirst(success);
					await sending;
					await queued;
				}
			}
			Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
		}

		[Test]
		public async Task DisposeDuringSend_CompletesAllSelectedFiles()
		{
			string directory = CreateTemporaryDirectory();
			var sender = new BlockingSender(2);
			using (var pipeline = new SnipeLogPipeline(null, directory, sender))
			{
				pipeline.Append(CreateRecord("A"));
				Assert.IsFalse(await pipeline.SendAsync());
				pipeline.Append(CreateRecord("B"));
				Task<bool> sending = pipeline.SendAsync().AsTask();
				await sender.FirstSendStarted;
				try
				{
					Assert.AreEqual(2, CountNonEmptyLogFiles(directory));
					pipeline.Append(CreateRecord("after-barrier"));
					pipeline.Dispose();
				}
				finally
				{
					sender.ReleaseFirst();
					await sending;
				}
				Assert.IsTrue(await sending);
				Assert.AreEqual(3, sender.Contents.Count);
				StringAssert.Contains("\"msg\":\"A\"", sender.Contents[1]);
				StringAssert.Contains("\"msg\":\"B\"", sender.Contents[2]);
				Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
			}
		}

		[Test]
		public async Task BufferDispose_DiscardsPendingCommandsAndCompletesSeals()
		{
			string directory = CreateTemporaryDirectory();
			var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var buffer = new SnipeLogFileBuffer(directory, startGate.Task);
			Task disposing = null;
			try
			{
				Assert.IsTrue(buffer.Append(Encoding.UTF8.GetBytes("discard-me\n")));
				Task<SnipeLogCacheDirectory.SendBatch> seal = buffer.SealCurrentFileAsync().AsTask();
				disposing = Task.Run(() => buffer.Dispose());
				Task completed = await Task.WhenAny(seal, Task.Delay(TimeSpan.FromSeconds(5)));
				Assert.AreSame(seal, completed, "A pending seal must complete without starting the writer.");
				Assert.IsNull(await seal);
				Assert.IsFalse(buffer.Append(Encoding.UTF8.GetBytes("rejected\n")));
				Assert.IsNull(await buffer.SealCurrentFileAsync());
				Assert.IsEmpty(Directory.GetFiles(directory));
			}
			finally
			{
				startGate.TrySetResult(true);
				if (disposing != null) await disposing;
				buffer.Dispose();
			}
			Assert.IsEmpty(Directory.GetFiles(directory));
		}

		[Test]
		[Platform("Win")]
		public async Task Dispose_FailedDeletionIsRetriedByNextBuffer()
		{
			string directory = CreateTemporaryDirectory();
			using (var pipeline = new SnipeLogPipeline(null, directory, new RecordingSender(false)))
			{
				pipeline.Append(CreateRecord("locked-file"));
				Assert.IsFalse(await pipeline.SendAsync());
				string file = Directory.GetFiles(directory, "*.ndjson")[0];
				using (var blocker = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					Assert.DoesNotThrow(() => pipeline.Dispose());
					Assert.IsTrue(File.Exists(file));
				}
			}
			using (var next = new SnipeLogPipeline(null, directory, new RecordingSender(true)))
			{
				Assert.IsEmpty(Directory.GetFiles(directory, "*.ndjson"));
			}
		}

		[Test]
		public void ConstructorCleanup_IsScopedToItsDirectory()
		{
			string first = CreateTemporaryDirectory();
			string second = CreateTemporaryDirectory();
			File.WriteAllText(Path.Combine(first, "snipe-log-old.ndjson"), "first");
			string untouched = Path.Combine(second, "snipe-log-old.ndjson");
			File.WriteAllText(untouched, "second");
			using (var buffer = new SnipeLogFileBuffer(first))
			{
				Assert.IsEmpty(Directory.GetFiles(first));
				Assert.AreEqual("second", File.ReadAllText(untouched));
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
			private readonly int _blockOnSend;
			private readonly UniTaskCompletionSource<bool> _firstSend = new UniTaskCompletionSource<bool>();
			private readonly TaskCompletionSource<bool> _firstSendStarted =
				new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			internal Task FirstSendStarted => _firstSendStarted.Task;
			internal int SendCount { get; private set; }
			internal List<string> Contents { get; } = new List<string>();

			internal BlockingSender(int blockOnSend = 1)
			{
				_blockOnSend = blockOnSend;
			}

			public async UniTask<bool> SendAsync(StreamReader file, Action<int> acknowledgeRecords)
			{
				SendCount++;
				bool success = SendCount >= _blockOnSend;
				if (SendCount == _blockOnSend)
				{
					_firstSendStarted.TrySetResult(true);
					success = await _firstSend.Task;
				}

				string content = file.ReadToEnd();
				Contents.Add(content);
				if (success)
				{
					acknowledgeRecords(CountRecords(content));
				}
				return success;
			}

			internal void ReleaseFirst(bool success = true)
			{
				_firstSend.TrySetResult(success);
			}
		}
	}
}
