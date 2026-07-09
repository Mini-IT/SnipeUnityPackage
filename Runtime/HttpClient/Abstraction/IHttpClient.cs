using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public interface IHttpClient
	{
		void Reset();
		void SetAuthToken(string token);
		void SetPersistentClientId(string token);
		UniTask<IHttpClientResponse> Get(Uri uri, CancellationToken cancellationToken = default);
		UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout, CancellationToken cancellationToken = default);
		UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout, CancellationToken cancellationToken = default);
		UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout, CancellationToken cancellationToken = default);
	}
}
