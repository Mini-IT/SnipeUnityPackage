
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MiniIT.Http
{
	public class UnityHttpClient : IHttpClient
	{
		private const string REQUEST_TIMEOUT_ERROR = "Request timeout";

		private string _authToken;
		private string _persistentClientId;

		public void Reset()
		{
			_authToken = null;
		}

		public void SetAuthToken(string token)
		{
			_authToken = token;
		}

		public void SetPersistentClientId(string id)
		{
			_persistentClientId = id;
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri, CancellationToken cancellationToken = default)
		{
			if (!await TrySwitchToMainThread(cancellationToken))
			{
				return UnityHttpClientResponse.CreateTimeout();
			}

			var request = UnityWebRequest.Get(uri);
			return await SendRequestAsync(request, cancellationToken);
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
			if (!await TrySwitchToMainThread(cancellationToken))
			{
				return UnityHttpClientResponse.CreateTimeout();
			}

			var request = UnityWebRequest.Get(uri);
			return await SendRequestAsync(request, timeout, cancellationToken);
		}

		public async UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
			if (!await TrySwitchToMainThread(cancellationToken))
			{
				return UnityHttpClientResponse.CreateTimeout();
			}

			var request = UnityWebRequest.Post(uri, content, "application/json");
			FillHeaders(request);

			return await SendRequestAsync(request, timeout, cancellationToken);
		}

		public async UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
			if (!await TrySwitchToMainThread(cancellationToken))
			{
				return UnityHttpClientResponse.CreateTimeout();
			}

			var form = new WWWForm();
			form.AddBinaryData(name, content);

			var request = UnityWebRequest.Post(uri, form);
			FillHeaders(request);

			return await SendRequestAsync(request, timeout, cancellationToken);
		}

		private void FillHeaders(UnityWebRequest request)
		{
			if (!string.IsNullOrEmpty(_authToken))
			{
				request.SetRequestHeader("Authorization", "Bearer " + _authToken);
			}

			if (!string.IsNullOrEmpty(_persistentClientId))
			{
				request.SetRequestHeader("DeviceID", _persistentClientId);
			}
		}

		private static async UniTask<bool> TrySwitchToMainThread(CancellationToken cancellationToken)
		{
			try
			{
				await UniTask.SwitchToMainThread(cancellationToken);
				return true;
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		private async UniTask<IHttpClientResponse> SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
		{
			request.downloadHandler = new DownloadHandlerBuffer();
			try
			{
				await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken, cancelImmediately: true);
				return new UnityHttpClientResponse(request);
			}
			catch (OperationCanceledException)
			{
				request.Abort();
				return UnityHttpClientResponse.CreateTimeout();
			}
			finally
			{
				request.Dispose();
			}
		}

		private async UniTask<IHttpClientResponse> SendRequestAsync(UnityWebRequest request, TimeSpan timeout, CancellationToken cancellationToken)
		{
			if (timeout == TimeSpan.Zero)
			{
				return await SendRequestAsync(request, cancellationToken);
			}

			request.downloadHandler = new DownloadHandlerBuffer();
			using var timeoutCts = new CancellationTokenSource(timeout);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
			try
			{
				await request.SendWebRequest().ToUniTask(cancellationToken: linkedCts.Token, cancelImmediately: true);
				return new UnityHttpClientResponse(request);
			}
			catch (OperationCanceledException)
			{
				request.Abort();
				return UnityHttpClientResponse.CreateTimeout();
			}
			catch (UnityWebRequestException e)
			{
				if (string.Equals(request.error, REQUEST_TIMEOUT_ERROR, StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(e.Message, REQUEST_TIMEOUT_ERROR, StringComparison.OrdinalIgnoreCase))
				{
					request.Abort();
					return UnityHttpClientResponse.CreateTimeout();
				}

				var response = new UnityHttpClientResponse(request);

				// If a response has a valid content, consider it as ok
				if (request.isDone && request.downloadHandler?.data != null)
				{
					string content = await response.GetStringContentAsync();
					if (!string.IsNullOrEmpty(content) && content.Contains("errorCode"))
					{
						response.IsSuccess = true;
						return response;
					}
				}

				return response;
			}
			finally
			{
				request.Dispose();
			}
		}
	}
}
