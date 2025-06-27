using System.Runtime.CompilerServices;
using System.Threading;

namespace MiniIT.Utils
{
	public static class CancellationTokenHelper
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Dispose(ref CancellationTokenSource cts, bool cancel)
		{
			var cancellation = cts; // local copy for thread safety
			cts = null;

			if (cancellation == null)
			{
				return;
			}

			if (cancel)
			{
				cancellation.Cancel();
			}
			cancellation.Dispose();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void CancelAndDispose(ref CancellationTokenSource cts)
		{
			Dispose(ref cts, true);
		}
	}
}
