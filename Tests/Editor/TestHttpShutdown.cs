using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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

			yield return WaitUntil(() => httpClient.GetWithTimeoutCalls > 0);

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

			yield return WaitUntil(() => httpClient.GetWithTimeoutCalls > 0);

			var token = httpClient.LastGetToken;
			Assert.IsFalse(token.IsCancellationRequested);

			transport.Dispose();

			Assert.IsTrue(token.IsCancellationRequested);
		}

		[UnityTest]
		public IEnumerator SnipeConfigLoadingService_Dispose_CancelsHttpLoad()
		{
			var httpClient = new RecordingHttpClient()
			{
				CompletePostJson = false,
			};
			var service = new SnipeConfigLoadingService("project", CreateServices(httpClient));

			service.Load().Forget();

			yield return WaitUntil(() => httpClient.PostJsonCalls > 0);

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

		private static IEnumerator WaitUntil(Func<bool> condition)
		{
			const int MAX_WAIT_FRAMES = 60;
			for (int i = 0; i < MAX_WAIT_FRAMES; i++)
			{
				if (condition())
				{
					yield break;
				}

				yield return null;
			}

			Assert.Fail("Condition was not reached");
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
			public bool CompleteGet = true;
			public bool CompletePostJson = true;

			public int GetWithoutTimeoutCalls;
			public int GetWithTimeoutCalls;
			public TimeSpan LastGetTimeout;
			public CancellationToken LastGetToken;

			public int PostJsonCalls;
			public CancellationToken LastPostJsonToken;

			public void Reset() { }
			public void SetAuthToken(string token) { }
			public void SetPersistentClientId(string token) { }

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

				return CompleteGet
					? UniTask.FromResult<IHttpClientResponse>(new RecordingHttpClientResponse(true))
					: WaitForCancellation(cancellationToken);
			}

			public UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout, CancellationToken cancellationToken = default)
			{
				PostJsonCalls++;
				LastPostJsonToken = cancellationToken;

				return CompletePostJson
					? UniTask.FromResult<IHttpClientResponse>(new RecordingHttpClientResponse(true))
					: WaitForCancellation(cancellationToken);
			}

			public UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout, CancellationToken cancellationToken = default)
			{
				return UniTask.FromResult<IHttpClientResponse>(new RecordingHttpClientResponse(true));
			}

			private static UniTask<IHttpClientResponse> WaitForCancellation(CancellationToken cancellationToken)
			{
				var completion = new UniTaskCompletionSource<IHttpClientResponse>();
				if (cancellationToken.IsCancellationRequested)
				{
					completion.TrySetCanceled(cancellationToken);
				}
				else if (cancellationToken.CanBeCanceled)
				{
					cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
				}

				return completion.Task;
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
