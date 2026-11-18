using System;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe
{
	public interface ISnipeLogPipeline : IDisposable
	{
		void Initialize(SnipeContext context, SnipeOptions options);
		void Append(SnipeLogRecord record);
		UniTask<bool> SendAsync();
	}
}
