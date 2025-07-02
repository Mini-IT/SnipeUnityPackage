using System;
using System.Threading;

namespace MiniIT.Snipe
{
	public static class SnipeManagerExtension
	{
		/// <inheritdoc cref="ISnipeContextReference.GetSnipeContextWhenReady"/>
		public static void GetContextWhenReady(this ISnipeContextProvider provider, Action<SnipeContext> callback,
			CancellationToken cancellationToken = default)
		{
			GetContextWhenReady(provider, 0, callback, cancellationToken);
		}

		/// <inheritdoc cref="ISnipeContextReference.GetSnipeContextWhenReady"/>
		/// <param name="id">context id</param>
		public static void GetContextWhenReady(this ISnipeContextProvider provider, int id, Action<SnipeContext> callback, CancellationToken cancellationToken = default)
		{
			provider.GetContextReference(id).GetSnipeContextWhenReady(callback, cancellationToken);
		}
	}
}
