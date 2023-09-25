using System;

namespace MiniIT.Snipe
{
	public interface IMainThreadRunner
	{
		void RunInMainThread(Action action);
	}
}
