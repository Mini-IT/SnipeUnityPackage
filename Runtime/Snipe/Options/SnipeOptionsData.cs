using System;
using System.Collections.Generic;

namespace MiniIT.Snipe.Configuration
{
	public enum SnipeProjectMode
	{
		Live = 0,
		Dev = 1,
	}

	public struct SnipeProjectInfo
	{
		public string ProjectID;
		public string ClientKey;
		public SnipeProjectMode Mode;
	}

	public enum WebSocketImplementations
	{
		WebSocketSharp,
		ClientWebSocket,
		BestWebSocket,
	}

	public class UdpAddress
	{
		public string Host;
		public ushort Port;
	}

	public class SnipeOptionsData
	{
		public SnipeProjectInfo ProjectInfo;
		public WebSocketImplementations WebSocketImplementation = WebSocketImplementations.BestWebSocket;
		public bool AutoJoinRoom = true;
		public List<string> ServerWebSocketUrls = new List<string>();
		public List<UdpAddress> ServerUdpUrls = new List<UdpAddress>();
		public List<string> ServerHttpUrls = new List<string>();

		/// <summary>
		/// Http transport heartbeat interval.
		/// If the value is less than 1 second then heartbeat is turned off.
		/// </summary>
		public TimeSpan HttpHeartbeatInterval = TimeSpan.FromMinutes(1);

		public bool CompressionEnabled  = true;
		public int MinMessageBytesToCompress = 13 * 1024;

		public IDictionary<string, object> LoginParameters;// = new Dictionary<string, object>();

		public string LogReporterUrl;

		public SnipeOptionsData Clone()
		{
			var copy = new SnipeOptionsData()
			{
				ProjectInfo = ProjectInfo,
				WebSocketImplementation = WebSocketImplementation,
				AutoJoinRoom = AutoJoinRoom,
				ServerWebSocketUrls = new List<string>(ServerWebSocketUrls),
				ServerUdpUrls = CloneUdpAddresses(ServerUdpUrls),
				ServerHttpUrls = new List<string>(ServerHttpUrls),
				HttpHeartbeatInterval = HttpHeartbeatInterval,
				CompressionEnabled = CompressionEnabled,
				MinMessageBytesToCompress = MinMessageBytesToCompress,
				LoginParameters = LoginParameters != null ? new Dictionary<string, object>(LoginParameters) : null,
				LogReporterUrl = LogReporterUrl,
			};

			return copy;
		}

		private static List<UdpAddress> CloneUdpAddresses(List<UdpAddress> source)
		{
			if (source == null || source.Count == 0)
			{
				return new List<UdpAddress>();
			}

			var result = new List<UdpAddress>(source.Count);
			for (int i = 0; i < source.Count; i++)
			{
				UdpAddress item = source[i];
				if (item == null)
				{
					continue;
				}

				result.Add(new UdpAddress()
				{
					Host = item.Host,
					Port = item.Port,
				});
			}

			return result;
		}
	}
}
