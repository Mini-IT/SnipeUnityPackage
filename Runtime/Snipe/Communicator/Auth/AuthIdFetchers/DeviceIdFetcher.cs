using System;
using System.Security.Cryptography;
using System.Text;
using MiniIT.Snipe.Logging;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class DeviceIdFetcher : AuthIdFetcher
	{
		private readonly Microsoft.Extensions.Logging.ILogger _logger;

		public DeviceIdFetcher()
		{
			_logger = LogManager.GetLogger(nameof(DeviceIdFetcher));
		}

		public override void Fetch(bool wait_initialization, Action<string> callback = null)
		{
			if (string.IsNullOrEmpty(Value))
			{
				if (SystemInfo.unsupportedIdentifier != SystemInfo.deviceUniqueIdentifier)
				{
					Value = SystemInfo.deviceUniqueIdentifier;

					if (string.IsNullOrEmpty(Value))
					{
						Value = SystemInfo.deviceUniqueIdentifier;
						_logger.Log($"[DeviceIdFetcher] Value = {Value}");
						if (Value.Length > 64)
						{
							Value = GetHashString(Value);
							_logger.Log($"[DeviceIdFetcher] Value Hash = {Value}");
						}
					}
				}
				//else
				//{
					// TODO: generate device id using custom algorithm
				//}
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
				sb.Append(b.ToString("X2").ToLower());
			}

			return sb.ToString();
		}
	}
}
