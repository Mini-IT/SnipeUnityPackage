using Cysharp.Threading.Tasks;
using MiniIT.Threading.Tasks;
using UnityEngine.Networking;

namespace MiniIT.Snipe.Internal
{
	public struct UnityHttpTransportClientResponse : IHttpTransportClientResponse
	{
		public long ResponseCode { get; }

		public bool IsSuccess { get; }
		public string Error { get; }

		private readonly UnityWebRequest _unityWebRequest;

		public UnityHttpTransportClientResponse(UnityWebRequest unityWebRequest) : this()
		{
			_unityWebRequest = unityWebRequest;
			ResponseCode = _unityWebRequest.responseCode;
			IsSuccess = string.IsNullOrEmpty(unityWebRequest.error);
			Error = unityWebRequest.error;
		}

		public AlterTask<string> GetContentAsync()
		{
			string content = _unityWebRequest?.downloadHandler?.text;
			return AlterTask<string>.FromResult(content);
		}

		public void Dispose()
		{
			_unityWebRequest?.Dispose();
		}
	}
}
