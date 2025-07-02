using System;
using System.Threading;

namespace MiniIT.Snipe
{
	public interface ISnipeContextReference
	{
		/// <summary>
		/// Try to get the instance of <see cref="SnipeContext"/>.
		/// If the internal reference is not set yet,
		/// then <b>no instance will be created</b>
		/// </summary>
		/// <param name="context">The instance of <see cref="SnipeContext"/></param>
		/// <returns><c>true</c> if a valid intance is found</returns>
		bool TryGetSnipeContext(out SnipeContext context);

		/// <summary>
		/// Ivokes the specified callback when the referenced context gets created
		/// </summary>
		/// <param name="callback">Method to call when the context is ready</param>
		/// <param name="cancellationToken"></param>
		void GetSnipeContextWhenReady(Action<SnipeContext> callback, CancellationToken cancellationToken = default);
	}
}
