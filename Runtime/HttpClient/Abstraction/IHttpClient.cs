using System;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public interface IHttpClient
	{
		void Reset();
		void SetAuthToken(string token);
		UniTask<IHttpClientResponse> GetAsync(Uri uri);
		UniTask<IHttpClientResponse> PostJsonAsync(Uri uri, string content);
		UniTask<IHttpClientResponse> PostAsync(Uri uri, string name, byte[] content);
	}

	public interface IHttpClientResponse : IDisposable
	{
		long ResponseCode { get; }
		bool IsSuccess { get; }
		string Error { get; }
		UniTask<string> GetContentAsync();
	}
}
