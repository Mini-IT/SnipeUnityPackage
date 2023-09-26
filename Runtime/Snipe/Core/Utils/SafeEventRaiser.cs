using System;

namespace MiniIT.Utils
{
	// Safe events raising
	// https://www.codeproject.com/Articles/36760/C-events-fundamentals-and-exception-handling-in-mu#exceptions

	public class SafeEventRaiser
	{
		private Action<Delegate, Exception> _invocationExceptionHandler;

		public SafeEventRaiser(Action<Delegate, Exception> onException)
		{
			_invocationExceptionHandler = onException;
		}

		public void RaiseEvent(Delegate eventDelegate, params object[] args)
		{
			if (eventDelegate == null)
			{
				return;
			}

			var invocationList = eventDelegate.GetInvocationList();
			foreach (Delegate handler in invocationList)
			{
				try
				{
					handler?.DynamicInvoke(args);
				}
				catch (Exception e)
				{
					_invocationExceptionHandler?.Invoke(handler, e);
				}
			}
		}
	}
}
