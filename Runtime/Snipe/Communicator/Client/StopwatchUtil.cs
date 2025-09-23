using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MiniIT.Snipe
{
	public static class StopwatchUtil
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static TimeSpan GetElapsedTime(long startTS)
		{
			long nowTicks = Stopwatch.GetTimestamp();
			double elapsedTicks = nowTicks - startTS;
			return (startTS > 0) ? TimeSpan.FromSeconds(elapsedTicks / Stopwatch.Frequency) : TimeSpan.Zero;
		}
	}
}
