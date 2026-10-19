using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MiniIT.Http;
using MiniIT.Snipe.Configuration;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestHttpShutdown
	{
		[UnityTest]
		public IEnumerator HttpTransport_Connect_UsesHandshakeTimeoutAndCancellationToken()
		{
			var httpClient = new RecordingHttpClient();
			var transport = CreateHttpTransport(httpClient);

			transport.Connect("https://example.com/");

			yield return httpClient.GetWithTimeoutCalled.AsUniTask().ToCoroutine();

			Assert.AreEqual(0, httpClient.GetWithoutTimeoutCalls);
			Assert.AreEqual(TimeSpan.FromSeconds(3), httpClient.LastGetTimeout);
			Assert.IsTrue(httpClient.LastGetToken.CanBeCanceled);

			transport.Dispose();
		}

		[UnityTest]
		public IEnumerator HttpTransport_Dispose_CancelsPendingHandshake()
		{
			var httpClient = new RecordingHttpClient()
			{
				CompleteGet = false,
			};
			var transport = CreateHttpTransport(httpClient);

			transport.Connect("https://example.com/");

			yield return httpClient.GetWithTimeoutCalled.AsUniTask().ToCoroutine();

			var token = httpClient.LastGetToken;
			Assert.IsFalse(token.IsCancellationRequested);

			transport.Dispose();

			Assert.IsTrue(token.IsCancellationRequested);
		}

		[UnityTest]
		public IEnumerator HttpTransport_CancelledPostReturnsTimeout_ReconnectStartsHandshake()
		{
			return CheckReconnectAfterPostCancellation(true);
		}

		[UnityTest]
		public IEnumerator HttpTransport_CancelledPostThrows_ReconnectStartsHandshake()
		{
			return CheckReconnectAfterPostCancellation(false);
		}

		private static IEnumerator CheckReconnectAfterPostCancellation(bool returnTimeoutResponse)
		{
			var httpClient = new RecordingHttpClient()
			{
				CompletePostJson = false,
				ReturnTimeoutOnPostCancellation = returnTimeoutResponse,
			};

			var transport = CreateHttpTransport(httpClient);

			try
			{
				transport.Connect("https://example.com/");
				yield return httpClient.ConnectionOpened.Task.AsUniTask().ToCoroutine();
				transport.SendMessage(new Dictionary<string, object>() { ["t"] = "server.ping", ["id"] = -1 });
				yield return httpClient.PostJsonCalled.AsUniTask().ToCoroutine();

				transport.Disconnect();
				Assert.IsTrue(httpClient.LastPostJsonToken.IsCancellationRequested);
				httpClient.ConnectionOpened = new TaskCompletionSource<Transport>(TaskCreationOptions.RunContinuationsAsynchronously);
				transport.Connect("https://example.com/");
				yield return httpClient.ConnectionOpened.Task.AsUniTask().ToCoroutine();
				Assert.AreEqual(2, httpClient.GetWithTimeoutCalls);
				Assert.IsTrue(transport.Connected);
			}
			finally
			{
				transport.Dispose();
			}
		}

		[UnityTest]
		public IEnumerator SnipeConfigLoadingService_Dispose_CancelsHttpLoad()
		{
			var httpClient = new RecordingHttpClient()
			{
				CompletePostJson = false,
			};
			var service = new SnipeConfigLoadingService("project", CreateServices(httpClient));

			service.Load(TimeSpan.FromSeconds(3)).Forget();

			yield return httpClient.PostJsonCalled.AsUniTask().ToCoroutine();

			var token = httpClient.LastPostJsonToken;
			Assert.IsFalse(token.IsCancellationRequested);

			service.Dispose();

			Assert.IsTrue(token.IsCancellationRequested);
		}

		private static HttpTransport CreateHttpTransport(RecordingHttpClient httpClient)
		{
			var services = CreateServices(httpClient);
			var options = new SnipeOptions(0, new SnipeOptionsData(), services);
			return new HttpTransport(new TransportOptions()
			{
				SnipeOptions = options,
				SnipeServices = services,
				ConnectionOpenedHandler = httpClient.OnConnectionOpened,
			});
		}

		private static ISnipeServices CreateServices(RecordingHttpClient httpClient)
		{
			var defaults = new NullSnipeServices();
			return new NullSnipeServices(
				defaults.SharedPrefs,
				defaults.LoggerFactory,
				defaults.Analytics,
				defaults.MainThreadRunner,
				defaults.ApplicationInfo,
				defaults.FuzzyStopwatchFactory,
				new RecordingHttpClientFactory(httpClient),
				defaults.InternetReachability,
				defaults.Ticker);
		}

		private sealed class RecordingHttpClientFactory : IHttpClientFactory
		{
			private readonly IHttpClient _httpClient;

			public RecordingHttpClientFactory(IHttpClient httpClient)
			{
				_httpClient = httpClient;
			}

			public IHttpClient CreateHttpClient() => _httpClient;
		}

		private sealed class RecordingHttpClient : IHttpClient
		{
			private readonly TaskCompletionSource<bool> _getWithTimeoutCalled =
				new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			private readonly TaskCompletionSource<bool> _postJsonCalled =
				new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			public TaskCompletionSource<Transport> ConnectionOpened =
				new TaskCompletionSource<Transport>(TaskCreationOptions.RunContinuationsAsynchronously);

			public bool CompleteGet = true;
			public bool CompletePostJson = true;
			public bool ReturnTimeoutOnPostCancellation;
			public Task GetWithTimeoutCalled => _getWithTimeoutCalled.Task;
			public Task PostJsonCalled => _postJsonCalled.Task;

			public int GetWithoutTimeoutCalls;
			public int GetWithTimeoutCalls;
			public TimeSpan LastGetTimeout;
			public CancellationToken LastGetToken;

			public int PostJsonCalls;
			public CancellationToken LastPostJsonToken;

			public void Reset() { }
			public void SetAuthToken(string token) { }
			public void SetPersistentClientId(string token) { }

			public void OnConnectionOpened(Transport transport)
			{
				ConnectionOpened.TrySetResult(transport);
			}

			public UniTask<IHttpClientResponse> Get(Uri uri, CancellationToken cancellationToken = default)
			{
				GetWithoutTimeoutCalls++;
				return UniTask.FromResult<IHttpClientResponse>(new RecordingHttpClientResponse(false));
			}

			public UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout, CancellationToken cancellationToken = default)
			{
				GetWithTimeoutCalls++;
				LastGetTimeout = timeout;
				LastGetToken = cancellationToken;
				_getWithTimeoutCalled.TrySetResult(true);

				return CompleteGet
					? UniTask.FromResult<IHttpClientResponse>(new RecordingHttpClientResponse(true))
					: WaitForCancellation(cancellationToken);
			}

			public UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout, CancellationToken cancellationToken = default)
			{
				PostJsonCalls++;
				LastPostJsonToken = cancellationToken;
				_postJsonCalled.TrySetResult(true);

				return CompletePostJson
					? UniTask.FromResult<IHttpClientResponse>(new RecordingHttpClientResponse(true))
					: WaitForCancellation(cancellationToken, ReturnTimeoutOnPostCancellation);
			}

			public UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout, CancellationToken cancellationToken = default)
			{
				return UniTask.FromResult<IHttpClientResponse>(new RecordingHttpClientResponse(true));
			}

			private static UniTask<IHttpClientResponse> WaitForCancellation(CancellationToken cancellationToken, bool returnTimeoutResponse = false)
			{
				var completion = new UniTaskCompletionSource<IHttpClientResponse>();
				var state = (completion, cancellationToken, returnTimeoutResponse);
				if (cancellationToken.IsCancellationRequested)
				{
					CompleteCancellation(state);
				}
				else if (cancellationToken.CanBeCanceled)
				{
					cancellationToken.Register(CompleteCancellation, state);
				}

				return completion.Task;
			}

			private static void CompleteCancellation(object state)
			{
				var cancellation = ((UniTaskCompletionSource<IHttpClientResponse> Completion, CancellationToken Token, bool ReturnTimeoutResponse))state;
				if (cancellation.ReturnTimeoutResponse)
				{
					cancellation.Completion.TrySetResult(new RecordingHttpClientResponse(false));
				}
				else
				{
					cancellation.Completion.TrySetCanceled(cancellation.Token);
				}
			}
		}

		private readonly struct RecordingHttpClientResponse : IHttpClientResponse
		{
			private readonly bool _success;

			public long ResponseCode => _success ? 200 : 408;
			public bool IsSuccess => _success;
			public string Error => _success ? null : "RequestTimeout";

			public RecordingHttpClientResponse(bool success)
			{
				_success = success;
			}

			public UniTask<string> GetStringContentAsync() => UniTask.FromResult("{}");
			public UniTask<byte[]> GetBinaryContentAsync() => UniTask.FromResult(Array.Empty<byte>());
			public void Dispose() { }
		}
	}
}
