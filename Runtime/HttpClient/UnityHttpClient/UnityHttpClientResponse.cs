using Cysharp.Threading.Tasks;
using MiniIT.Threading;
using UnityEngine.Networking;

namespace MiniIT.Http
{
	public struct UnityHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode { get; }

		public bool IsSuccess { get; }
		public string Error { get; }

		private readonly UnityWebRequest _unityWebRequest;

		public UnityHttpClientResponse(UnityWebRequest unityWebRequest) : this()
		{
			_unityWebRequest = unityWebRequest;
			ResponseCode = _unityWebRequest.responseCode;
			IsSuccess = string.IsNullOrEmpty(unityWebRequest.error);
			Error = unityWebRequest.error;
		}

		public UniTask<string> GetContentAsync()
		{
			string content = _unityWebRequest?.downloadHandler?.text;
			return UniTask.FromResult(content);
		}

		public void Dispose()
		{
			_unityWebRequest?.Dispose();
		}
	}
}
