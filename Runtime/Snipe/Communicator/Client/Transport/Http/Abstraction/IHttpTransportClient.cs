
using System;
using MiniIT.Threading.Tasks;

namespace MiniIT.Snipe.Internal
{
	public interface IHttpTransportClient
	{
		void Reset();
		void SetAuthToken(string token);
		AlterTask<IHttpTransportClientResponse> GetAsync(Uri uri);
		AlterTask<IHttpTransportClientResponse> PostJsonAsync(Uri uri, string content);
	}

	public interface IHttpTransportClientResponse : IDisposable
	{
		long ResponseCode { get; }
		bool IsSuccess { get; }
		string Error { get; }
		AlterTask<string> GetContentAsync();
	}
}
