using System;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe
{
	public interface ILogReporter : IDisposable
	{
		void Initialize(SnipeContext context, SnipeOptions options);
		UniTask<bool> SendAsync();
	}

	public interface ILogReporterFactory
	{
		ILogReporter Create();
	}
}
