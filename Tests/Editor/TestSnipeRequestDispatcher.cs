using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestSnipeRequestDispatcher
	{
		[UnityTest]
		public IEnumerator Send_LimitsImmediateRequests()
		{
			var fixture = new DispatcherFixture();

			for (int i = 0; i < fixture.RequestsPerSecondLimit + 1; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			yield return fixture.WaitForDelayCall();

			Assert.AreEqual(fixture.RequestsPerSecondLimit, fixture.Sent.Count);
			Assert.AreEqual(1, fixture.DelayCalls.Count);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator Send_UsesConfiguredRequestsPerSecondLimit()
		{
			var fixture = new DispatcherFixture(3);

			for (int i = 0; i < fixture.RequestsPerSecondLimit + 1; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			yield return fixture.WaitForDelayCall();

			Assert.AreEqual(fixture.RequestsPerSecondLimit, fixture.Sent.Count);
			Assert.AreEqual(1, fixture.DelayCalls.Count);

			fixture.Dispatcher.Clear();
		}

		[Test]
		public void Send_UsesCurrentRequestsPerSecondLimit()
		{
			int requestsPerSecondLimit = 3;
			var fixture = new DispatcherFixture(() => requestsPerSecondLimit);

			for (int i = 0; i < requestsPerSecondLimit; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			requestsPerSecondLimit = 5;
			fixture.Dispatcher.Send(fixture.CreateMessage(10), true);

			Assert.AreEqual(4, fixture.Sent.Count);
			Assert.AreEqual(0, fixture.DelayCalls.Count);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator QueuedSmallRequests_AreAutoBatchedAfterCooldown()
		{
			var fixture = new DispatcherFixture();

			for (int i = 0; i < fixture.RequestsPerSecondLimit; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			fixture.Dispatcher.Send(fixture.CreateMessage(10), true);
			fixture.Dispatcher.Send(fixture.CreateMessage(11), true);

			yield return fixture.WaitForDelayCall();
			fixture.CompleteNextDelay();
			yield return null;

			Assert.AreEqual(1, fixture.Batches.Count);
			Assert.AreEqual(2, fixture.Batches[0].Count);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator QueuedSmallRequests_BatchConsumesRequestSlots()
		{
			var fixture = new DispatcherFixture(SnipeClient.MAX_BATCH_SIZE);

			for (int i = 0; i < fixture.RequestsPerSecondLimit; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			for (int i = 0; i < fixture.RequestsPerSecondLimit + 1; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(10 + i), true);
			}

			yield return fixture.WaitForDelayCall();
			fixture.CompleteNextDelay();
			yield return fixture.WaitForDelayCalls(2);

			Assert.AreEqual(1, fixture.Batches.Count);
			Assert.AreEqual(fixture.RequestsPerSecondLimit, fixture.Batches[0].Count);
			Assert.AreEqual(fixture.RequestsPerSecondLimit, fixture.Sent.Count);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator SendBatch_WaitsForEnoughRequestSlots()
		{
			var fixture = new DispatcherFixture();

			for (int i = 0; i < fixture.RequestsPerSecondLimit - 1; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			fixture.Dispatcher.SendBatch(fixture.CreateBatch(10, 2));

			yield return fixture.WaitForDelayCall();

			Assert.AreEqual(0, fixture.Batches.Count);

			fixture.CompleteNextDelay();
			yield return null;

			Assert.AreEqual(1, fixture.Batches.Count);
			Assert.AreEqual(2, fixture.Batches[0].Count);

			fixture.Dispatcher.Clear();
		}

		[Test]
		public void Send_ReleasesRequestSlotWhenSendFails()
		{
			var fixture = new DispatcherFixture(1);

			fixture.FailSend = true;
			fixture.Dispatcher.Send(fixture.CreateMessage(1), true);

			fixture.FailSend = false;
			fixture.Dispatcher.Send(fixture.CreateMessage(2), true);

			Assert.AreEqual(1, fixture.Sent.Count);
			Assert.AreEqual(2, fixture.Sent[0]["id"]);
			Assert.AreEqual(0, fixture.DelayCalls.Count);

			fixture.Dispatcher.Clear();
		}

		[Test]
		public void SendBatch_ReleasesRequestSlotsWhenSendFails()
		{
			var fixture = new DispatcherFixture(2);

			fixture.FailBatch = true;
			fixture.Dispatcher.SendBatch(fixture.CreateBatch(1, 2));

			fixture.FailBatch = false;
			fixture.Dispatcher.Send(fixture.CreateMessage(3), true);

			Assert.AreEqual(1, fixture.Sent.Count);
			Assert.AreEqual(3, fixture.Sent[0]["id"]);
			Assert.AreEqual(0, fixture.DelayCalls.Count);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator QueuedLargeRequest_IsNotAutoBatched()
		{
			var fixture = new DispatcherFixture();

			for (int i = 0; i < fixture.RequestsPerSecondLimit; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			fixture.Dispatcher.Send(fixture.CreateLargeMessage(10), true);
			fixture.Dispatcher.Send(fixture.CreateMessage(11), true);

			yield return fixture.WaitForDelayCall();
			fixture.CompleteNextDelay();
			yield return null;

			Assert.AreEqual(0, fixture.Batches.Count);
			Assert.AreEqual(fixture.RequestsPerSecondLimit + 2, fixture.Sent.Count);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator RateLimit_RetriesSameRequestAfterDelay()
		{
			var fixture = new DispatcherFixture();
			var message = fixture.CreateMessage(1);

			fixture.Dispatcher.Send(message, true);
			Assert.IsTrue(fixture.Dispatcher.TryHandleRateLimit(1));

			yield return fixture.WaitForDelayCall();
			fixture.CompleteNextDelay();
			yield return null;

			Assert.AreEqual(2, fixture.Sent.Count);
			Assert.AreSame(message, fixture.Sent[1]);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator RateLimit_RetriesAfterLongClientPause()
		{
			var fixture = new DispatcherFixture();

			fixture.Dispatcher.Send(fixture.CreateMessage(1), true);
			fixture.AdvanceTime(31000);

			Assert.IsTrue(fixture.Dispatcher.TryHandleRateLimit(1));
			yield return fixture.WaitForDelayCall();

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator RateLimit_BackoffIsClientWide()
		{
			var fixture = new DispatcherFixture();

			fixture.Dispatcher.Send(fixture.CreateMessage(1), true);
			fixture.Dispatcher.Send(fixture.CreateMessage(2), true);

			Assert.IsTrue(fixture.Dispatcher.TryHandleRateLimit(1));
			yield return fixture.WaitForDelayCalls(1);

			Assert.IsTrue(fixture.Dispatcher.TryHandleRateLimit(2));
			yield return fixture.WaitForDelayCalls(2);

			Assert.AreEqual(SnipeClient.RATE_LIMIT_RETRY_DELAY_MS, fixture.DelayCalls[0]);
			Assert.AreEqual(SnipeClient.RATE_LIMIT_RETRY_DELAY_MS, fixture.DelayCalls[1]);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator RateLimit_BackoffAdvancesAfterCooldown()
		{
			var fixture = new DispatcherFixture();
			var message = fixture.CreateMessage(1);

			fixture.Dispatcher.Send(message, true);

			Assert.IsTrue(fixture.Dispatcher.TryHandleRateLimit(1));
			yield return fixture.WaitForDelayCalls(1);
			fixture.CompleteNextDelay();
			yield return null;

			Assert.IsTrue(fixture.Dispatcher.TryHandleRateLimit(1));
			yield return fixture.WaitForDelayCalls(2);

			Assert.AreEqual(SnipeClient.RATE_LIMIT_RETRY_DELAY_MS, fixture.DelayCalls[0]);
			Assert.AreEqual(SnipeClient.RATE_LIMIT_RETRY_DELAY_MS * 2, fixture.DelayCalls[1]);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator Clear_CancelsQueuedRequests()
		{
			var fixture = new DispatcherFixture();

			for (int i = 0; i < fixture.RequestsPerSecondLimit + 1; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			yield return fixture.WaitForDelayCall();
			fixture.Dispatcher.Clear();
			yield return null;

			Assert.AreEqual(fixture.RequestsPerSecondLimit, fixture.Sent.Count);
		}

		private sealed class DispatcherFixture
		{
			public readonly List<IDictionary<string, object>> Sent = new List<IDictionary<string, object>>();
			public readonly List<List<IDictionary<string, object>>> Batches = new List<List<IDictionary<string, object>>>();
			public readonly List<int> DelayCalls = new List<int>();

			private readonly Queue<PendingDelay> _pendingDelays = new Queue<PendingDelay>();
			private long _timestamp = 1;

			public SnipeRequestDispatcher Dispatcher { get; }
			public int RequestsPerSecondLimit { get; }
			public bool FailSend { get; set; }
			public bool FailBatch { get; set; }

			public DispatcherFixture(int requestsPerSecondLimit = SnipeOptions.DEFAULT_REQUESTS_PER_SECOND_LIMIT)
				: this(() => requestsPerSecondLimit, requestsPerSecondLimit)
			{
			}

			public DispatcherFixture(Func<int> getRequestsPerSecondLimit)
				: this(getRequestsPerSecondLimit, getRequestsPerSecondLimit())
			{
			}

			private DispatcherFixture(Func<int> getRequestsPerSecondLimit, int requestsPerSecondLimit)
			{
				RequestsPerSecondLimit = requestsPerSecondLimit;
				Dispatcher = new SnipeRequestDispatcher(
					Send,
					SendBatch,
					() => true,
					NullAnalyticsContext.Instance,
					() => _timestamp,
					1000,
					Delay,
					getRequestsPerSecondLimit);
			}

			public IDictionary<string, object> CreateMessage(int id)
			{
				return new Dictionary<string, object>()
				{
					["t"] = "test",
					["id"] = id,
				};
			}

			public IDictionary<string, object> CreateLargeMessage(int id)
			{
				var message = CreateMessage(id);

				for (int i = 0; i < 31; i++)
				{
					message["extra" + i] = i;
				}

				return message;
			}

			public List<IDictionary<string, object>> CreateBatch(int startId, int count)
			{
				var batch = new List<IDictionary<string, object>>(count);

				for (int i = 0; i < count; i++)
				{
					batch.Add(CreateMessage(startId + i));
				}

				return batch;
			}

			public void CompleteNextDelay()
			{
				var delay = _pendingDelays.Dequeue();
				_timestamp += delay.DelayMs;
				delay.Complete();
			}

			public void AdvanceTime(int delayMs)
			{
				_timestamp += delayMs;
			}

			public IEnumerator WaitForDelayCall()
			{
				return WaitForDelayCalls(1);
			}

			public IEnumerator WaitForDelayCalls(int count)
			{
				for (int i = 0; i < 10; i++)
				{
					if (DelayCalls.Count >= count)
					{
						yield break;
					}

					yield return null;
				}

				Assert.Fail("Dispatcher did not start delay");
			}

			private bool Send(IDictionary<string, object> message)
			{
				if (FailSend)
				{
					return false;
				}

				Sent.Add(message);
				return true;
			}

			private bool SendBatch(List<IDictionary<string, object>> messages)
			{
				if (FailBatch)
				{
					return false;
				}

				Batches.Add(messages);
				return true;
			}

			private async UniTask Delay(int delayMs, CancellationToken cancellation)
			{
				DelayCalls.Add(delayMs);

				var delay = new PendingDelay(delayMs, cancellation);
				_pendingDelays.Enqueue(delay);
				await delay.Task;
			}
		}

		private sealed class PendingDelay
		{
			private readonly CancellationTokenRegistration _registration;
			private readonly UniTaskCompletionSource<bool> _completionSource = new UniTaskCompletionSource<bool>();

			public int DelayMs { get; }
			public UniTask<bool> Task => _completionSource.Task;

			public PendingDelay(int delayMs, CancellationToken cancellation)
			{
				DelayMs = delayMs;
				_registration = cancellation.Register(Cancel);
			}

			public void Complete()
			{
				_registration.Dispose();
				_completionSource.TrySetResult(true);
			}

			private void Cancel()
			{
				_completionSource.TrySetCanceled();
			}
		}
	}
}
