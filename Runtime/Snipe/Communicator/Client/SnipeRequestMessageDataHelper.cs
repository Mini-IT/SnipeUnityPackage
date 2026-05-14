using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MiniIT.Snipe
{
	internal static class SnipeRequestMessageDataHelper
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Ensure(ref IDictionary<string, object> data, IDictionary<string, object> message)
		{
			if (data != null)
			{
				return;
			}

			if (message.TryGetValue("data", out var dataObj))
			{
				data = dataObj as Dictionary<string, object> ?? new Dictionary<string, object>();
			}
			else
			{
				data = new Dictionary<string, object>();
			}
		}
	}
}
