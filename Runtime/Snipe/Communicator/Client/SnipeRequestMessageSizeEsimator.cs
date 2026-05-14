using System.Collections;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	internal static class SnipeRequestMessageSizeEsimator
	{
		private const int MAX_ITEMS_COUNT = 50;
		private const int MAX_STR_LENGTH = 2048;

		/// <summary>
		/// Empirically estimates if the message is not too large
		/// </summary>
		/// <param name="message">The message to estimate</param>
		/// <returns><c>true</c> if the message is small enough</returns>
		internal static bool EstimateSizeSmall(IDictionary<string, object> message)
		{
			if (message == null || message.Count > 30)
			{
				return false;
			}

			int count = 0;
			int stringLength = 0;
			return EstimateMessageSizeSmall(message, ref count, ref stringLength);
		}

		private static bool EstimateMessageSizeSmall(IDictionary<string, object> message, ref int count, ref int stringLength)
		{
			foreach (var kvp in message)
			{
				count++;
				stringLength += kvp.Key.Length;

				if (count > MAX_ITEMS_COUNT || stringLength > MAX_STR_LENGTH)
				{
					return false;
				}

				if (!EstimateMessageItemSizeSmall(kvp.Value, ref count, ref stringLength))
				{
					return false;
				}
			}

			return true;
		}

		private static bool EstimateMessageItemSizeSmall(object value, ref int count, ref int stringLength)
		{
			if (count > MAX_ITEMS_COUNT || stringLength > MAX_STR_LENGTH)
			{
				return false;
			}

			if (value is IList list)
			{
				count += list.Count;
				foreach (var item in list)
				{
					if (!EstimateMessageItemSizeSmall(item, ref count, ref stringLength))
					{
						return false;
					}
				}
			}
			else if (value is IDictionary<string, object> dict)
			{
				if (!EstimateMessageSizeSmall(dict, ref count, ref stringLength))
				{
					return false;
				}
			}
			else if (value is string str)
			{
				stringLength += str.Length;
			}
			else
			{
				stringLength += 3;
			}

			if (count > MAX_ITEMS_COUNT || stringLength > MAX_STR_LENGTH)
			{
				return false;
			}

			return true;
		}
	}
}
