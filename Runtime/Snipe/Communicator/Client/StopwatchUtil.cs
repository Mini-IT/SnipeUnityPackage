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
			return (startTS > 0) ? TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - startTS) * Stopwatch.Frequency) : TimeSpan.Zero;
		}
	}
}
