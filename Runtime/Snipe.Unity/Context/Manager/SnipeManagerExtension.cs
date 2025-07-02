using System;
using System.Threading;

namespace MiniIT.Snipe
{
	public static class SnipeManagerExtension
	{
		/// <inheritdoc cref="ISnipeContextReference.GetSnipeContextWhenReady"/>
		public static void GetContextWhenReady(this ISnipeManager snipeManager, Action<SnipeContext> callback,
			CancellationToken cancellationToken = default)
		{
			GetContextWhenReady(snipeManager, 0, callback, cancellationToken);
		}

		/// <inheritdoc cref="ISnipeContextReference.GetSnipeContextWhenReady"/>
		/// <param name="id">context id</param>
		public static void GetContextWhenReady(this ISnipeManager snipeManager, int id, Action<SnipeContext> callback, CancellationToken cancellationToken = default)
		{
			snipeManager.GetContextReference(id).GetSnipeContextWhenReady(callback, cancellationToken);
		}
	}
}
