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

		void GetSnipeContextWhenReady(Action<SnipeContext> callback, CancellationToken cancellationToken = default);
	}
}
