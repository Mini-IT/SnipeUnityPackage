using System;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public interface IHttpClient
	{
		void Reset();
		void SetAuthToken(string token);
		void SetPersistentClientId(string token);
		UniTask<IHttpClientResponse> Get(Uri uri);
		UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout);
		UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout);
		UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout);
	}
}
