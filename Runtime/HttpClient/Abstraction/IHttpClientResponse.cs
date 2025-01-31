using System;
using Cysharp.Threading.Tasks;

namespace MiniIT.Http
{
	public interface IHttpClientResponse : IDisposable
	{
		long ResponseCode { get; }
		bool IsSuccess { get; }
		string Error { get; }
		UniTask<string> GetStringContentAsync();
		UniTask<byte[]> GetBinaryContentAsync();
	}
}
