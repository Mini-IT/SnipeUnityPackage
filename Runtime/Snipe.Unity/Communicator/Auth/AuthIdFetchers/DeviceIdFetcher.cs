using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using UnityEngine;

namespace MiniIT.Snipe.Unity
{
#if UNITY_WEBGL

    public class DeviceIdFetcher : AuthIdFetcher
    {
        private const string ID_PREFS_KEY = "com.miniit.app.webgl.id";

        public override void Fetch(bool _, Action<string> callback = null)
        {
	        var sharedPrefs = SnipeServices.Instance.SharedPrefs;

            if (string.IsNullOrEmpty(Value))
            {
                string value = sharedPrefs.GetString(ID_PREFS_KEY);
                if (string.IsNullOrEmpty(value))
                {
                    value = Guid.NewGuid().ToString();
                    sharedPrefs.SetString(ID_PREFS_KEY, value);
                }

                Value = value;
            }

            callback?.Invoke(Value);
        }
    }

#else

	public class DeviceIdFetcher : AuthIdFetcher
	{
		private readonly Microsoft.Extensions.Logging.ILogger _logger;

		public DeviceIdFetcher()
		{
			_logger = SnipeServices.Instance.LogService.GetLogger(nameof(DeviceIdFetcher));
		}

		public override void Fetch(bool _, Action<string> callback = null)
		{
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

#endif
}
