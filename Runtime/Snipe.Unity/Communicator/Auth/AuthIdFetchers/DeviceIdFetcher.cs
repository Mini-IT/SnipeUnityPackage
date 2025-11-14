using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
	public class DeviceIdFetcher : AuthIdFetcher
	{
		private readonly Microsoft.Extensions.Logging.ILogger _logger;

		public DeviceIdFetcher()
		{
			_logger = SnipeServices.LogService.GetLogger(nameof(DeviceIdFetcher));
		}

		public override void Fetch(bool _, Action<string> callback = null)
		{
#if UNITY_WEBGL
            if (string.IsNullOrEmpty(Value))
            {
                string value = PlayerPrefs.GetString(ID_PREFS_KEY);
				
                if (string.IsNullOrEmpty(value))
                {
                    value = Guid.NewGuid().ToString();
                    PlayerPrefs.GetString(ID_PREFS_KEY);
                }

                Value = value;
				_logger.LogTrace($"[DeviceIdFetcher] Value = {Value}");
            }
#else
			if (string.IsNullOrEmpty(Value))
			{
				if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
				{
					Value = SystemInfo.deviceUniqueIdentifier;

					_logger.LogTrace($"[DeviceIdFetcher] Value = {Value}");

					if (Value.Length > 64)
					{
						Value = GetHashString(Value);
						_logger.LogTrace($"[DeviceIdFetcher] Value Hash = {Value}");
					}
				}
				else
				{
					_logger.LogTrace("Not Supported");
					// TODO: generate device id using custom algorithm
				}
			}
#endif

			callback?.Invoke(Value);
		}

		private static byte[] GetHash(string inputString)
		{
			using (HashAlgorithm algorithm = SHA256.Create())
			{
				return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
			}
		}

		private static string GetHashString(string inputString)
		{
			StringBuilder sb = new StringBuilder();
			foreach (byte b in GetHash(inputString))
			{
				sb.Append(b.ToString("X2").ToLowerInvariant());
			}

			return sb.ToString();
		}
	}
}
