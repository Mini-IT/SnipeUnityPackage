using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	internal static class SnipeProfileManagerRequestGuard
	{
		public static void TrackForbiddenRequest(string messageType, IAnalyticsContext analytics, ILogger logger)
		{
			if (!IsForbiddenRequest(messageType))
			{
				return;
			}

			logger.LogError("SNIPE_PROFILEMENAGER: request type '{0}' is forbidden while Profile Manager is used. Request will still be sent.", messageType);

			var properties = new Dictionary<string, object>(1)
			{
				["message_type"] = messageType,
			};

			analytics?.TrackError("SNIPE_PROFILEMENAGER forbidden request", null, properties);
		}

		private static bool IsForbiddenRequest(string messageType)
		{
			switch (messageType)
			{
				case "attr.set":
				case "attr.setMulti":
				case "attr.inc":
				case "attr.dec":
					return true;

				default:
					return false;
			}
		}
	}
}
