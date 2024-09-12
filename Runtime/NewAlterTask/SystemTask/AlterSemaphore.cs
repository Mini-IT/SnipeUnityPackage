#if UNITY_WEBGL
#define FORCE_UNITASK
#endif

#if !FORCE_UNITASK

using System.Threading;

namespace MiniIT.Threading
{
	public class AlterSemaphore : SemaphoreSlim
	{
		public AlterSemaphore(int initialCount) : base(initialCount)
		{
		}

		public AlterSemaphore(int initialCount, int maxCount) : base(initialCount, maxCount)
		{
		}
	}
}

#endif
