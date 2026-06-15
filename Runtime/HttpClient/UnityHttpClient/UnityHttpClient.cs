
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MiniIT.Http
{
	public class UnityHttpClient : IHttpClient
	{
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

		public async UniTask<IHttpClientResponse> Get(Uri uri)
		{
			await UniTask.SwitchToMainThread();

			var request = UnityWebRequest.Get(uri);
			return await SendRequestAsync(request);
		}

		public async UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout)
		{
			await UniTask.SwitchToMainThread();

			var request = UnityWebRequest.Get(uri);
			return await SendRequestAsync(request, timeout);
		}

		public async UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout)
		{
			await UniTask.SwitchToMainThread();

			var request = UnityWebRequest.Post(uri, content, "application/json");
			FillHeaders(request);

			return await SendRequestAsync(request, timeout);
		}

		public async UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout)
		{
			await UniTask.SwitchToMainThread();

			var form = new WWWForm();
			form.AddBinaryData(name, content);

			var request = UnityWebRequest.Post(uri, form);
			FillHeaders(request);

			return await SendRequestAsync(request, timeout);
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

		private async UniTask<IHttpClientResponse> SendRequestAsync(UnityWebRequest request)
		{
			request.downloadHandler = new DownloadHandlerBuffer();
			try
			{
				await request.SendWebRequest().ToUniTask();
				return new UnityHttpClientResponse(request);
			}
			finally
			{
				request.Dispose();
			}
		}

		private async UniTask<IHttpClientResponse> SendRequestAsync(UnityWebRequest request, TimeSpan timeout)
		{
			if (timeout == TimeSpan.Zero)
			{
				return await SendRequestAsync(request);
			}

			request.downloadHandler = new DownloadHandlerBuffer();
			using var cts = new CancellationTokenSource(timeout);
			try
			{
				await request.SendWebRequest().ToUniTask(cancellationToken: cts.Token);
				return new UnityHttpClientResponse(request);
			}
			catch (OperationCanceledException)
			{
				request.Abort();
				return UnityHttpClientResponse.CreateTimeout();
			}
			catch (UnityWebRequestException)
			{
				var response = new UnityHttpClientResponse(request);

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
