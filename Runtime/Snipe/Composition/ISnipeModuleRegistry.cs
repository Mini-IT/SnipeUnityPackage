using System;

namespace MiniIT.Snipe
{
	public interface ISnipeModuleRegistry
	{
		void Register<T>(T instance) where T : class;
		bool TryGet<T>(out T instance) where T : class;
		T Get<T>() where T : class;
	}
}
