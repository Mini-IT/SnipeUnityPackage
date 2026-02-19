using System;
using Cysharp.Threading.Tasks;
using MiniIT.Http;

namespace MiniIT.Snipe
{
	public sealed class NullHttpClient : IHttpClient
	{
		public void Reset() { }
		public void SetAuthToken(string token) { }
		public void SetPersistentClientId(string token) { }
		public UniTask<IHttpClientResponse> Get(Uri uri) => UniTask.FromResult<IHttpClientResponse>(new NullHttpClientResponse());
		public UniTask<IHttpClientResponse> Get(Uri uri, TimeSpan timeout) => UniTask.FromResult<IHttpClientResponse>(new NullHttpClientResponse());
		public UniTask<IHttpClientResponse> PostJson(Uri uri, string content, TimeSpan timeout) => UniTask.FromResult<IHttpClientResponse>(new NullHttpClientResponse());
		public UniTask<IHttpClientResponse> Post(Uri uri, string name, byte[] content, TimeSpan timeout) => UniTask.FromResult<IHttpClientResponse>(new NullHttpClientResponse());
	}

	public sealed class NullHttpClientFactory : IHttpClientFactory
	{
		public IHttpClient CreateHttpClient() => new NullHttpClient();
	}

	public sealed class NullHttpClientResponse : IHttpClientResponse
	{
		public long ResponseCode => 0;
		public bool IsSuccess => false;
		public string Error => "NullHttpClient";

		public UniTask<string> GetStringContentAsync() => UniTask.FromResult(string.Empty);
		public UniTask<byte[]> GetBinaryContentAsync() => UniTask.FromResult(Array.Empty<byte>());
		public void Dispose() { }
	}
}
