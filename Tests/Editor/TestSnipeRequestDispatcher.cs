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

			for (int i = 0; i < SnipeClient.DEFAULT_REQUESTS_PER_SECOND_LIMIT + 1; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			yield return fixture.WaitForDelayCall();

			Assert.AreEqual(SnipeClient.DEFAULT_REQUESTS_PER_SECOND_LIMIT, fixture.Sent.Count);
			Assert.AreEqual(1, fixture.DelayCalls.Count);

			fixture.Dispatcher.Clear();
		}

		[UnityTest]
		public IEnumerator QueuedSmallRequests_AreAutoBatchedAfterCooldown()
		{
			var fixture = new DispatcherFixture();

			for (int i = 0; i < SnipeClient.DEFAULT_REQUESTS_PER_SECOND_LIMIT; i++)
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
		public IEnumerator QueuedLargeRequest_IsNotAutoBatched()
		{
			var fixture = new DispatcherFixture();

			for (int i = 0; i < SnipeClient.DEFAULT_REQUESTS_PER_SECOND_LIMIT; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			fixture.Dispatcher.Send(fixture.CreateMessage(10, SnipeClient.DEFAULT_AUTO_BATCH_MAX_MESSAGE_BYTES + 1), true);
			fixture.Dispatcher.Send(fixture.CreateMessage(11), true);

			yield return fixture.WaitForDelayCall();
			fixture.CompleteNextDelay();
			yield return null;

			Assert.AreEqual(0, fixture.Batches.Count);
			Assert.AreEqual(SnipeClient.DEFAULT_REQUESTS_PER_SECOND_LIMIT + 2, fixture.Sent.Count);

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
		public IEnumerator Clear_CancelsQueuedRequests()
		{
			var fixture = new DispatcherFixture();

			for (int i = 0; i < SnipeClient.DEFAULT_REQUESTS_PER_SECOND_LIMIT + 1; i++)
			{
				fixture.Dispatcher.Send(fixture.CreateMessage(i), true);
			}

			yield return fixture.WaitForDelayCall();
			fixture.Dispatcher.Clear();
			yield return null;

			Assert.AreEqual(SnipeClient.DEFAULT_REQUESTS_PER_SECOND_LIMIT, fixture.Sent.Count);
		}

		private sealed class DispatcherFixture
		{
			public readonly List<IDictionary<string, object>> Sent = new List<IDictionary<string, object>>();
			public readonly List<List<IDictionary<string, object>>> Batches = new List<List<IDictionary<string, object>>>();
			public readonly List<int> DelayCalls = new List<int>();

			private readonly Queue<PendingDelay> _pendingDelays = new Queue<PendingDelay>();
			private long _timestamp = 1;

			public SnipeRequestDispatcher Dispatcher { get; }

			public DispatcherFixture()
			{
				Dispatcher = new SnipeRequestDispatcher(
					Send,
					SendBatch,
					() => true,
					() => _timestamp,
					1000,
					Delay);
			}

			public IDictionary<string, object> CreateMessage(int id, int payloadSize = 0)
			{
				var message = new Dictionary<string, object>()
				{
					["t"] = "test",
					["id"] = id,
				};

				if (payloadSize > 0)
				{
					message["data"] = new Dictionary<string, object>()
					{
						["payload"] = new string('x', payloadSize),
					};
				}

				return message;
			}

			public void CompleteNextDelay()
			{
				var delay = _pendingDelays.Dequeue();
				_timestamp += delay.DelayMs;
				delay.Complete();
			}

			public IEnumerator WaitForDelayCall()
			{
				for (int i = 0; i < 10; i++)
				{
					if (DelayCalls.Count > 0)
					{
						yield break;
					}

					yield return null;
				}

				Assert.Fail("Dispatcher did not start delay");
			}

			private bool Send(IDictionary<string, object> message)
			{
				Sent.Add(message);
				return true;
			}

			private bool SendBatch(List<IDictionary<string, object>> messages)
			{
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
