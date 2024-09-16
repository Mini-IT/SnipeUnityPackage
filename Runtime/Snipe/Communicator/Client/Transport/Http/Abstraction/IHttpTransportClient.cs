
using System;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Internal
{
	public interface IHttpTransportClient
	{
		void Reset();
		void SetAuthToken(string token);
		UniTask<IHttpTransportClientResponse> GetAsync(Uri uri);
		UniTask<IHttpTransportClientResponse> PostJsonAsync(Uri uri, string content);
	}

	public interface IHttpTransportClientResponse : IDisposable
	{
		long ResponseCode { get; }
		bool IsSuccess { get; }
		string Error { get; }
		UniTask<string> GetContentAsync();
	}
}
