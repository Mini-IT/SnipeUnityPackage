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

	public class SnipeConfigData
	{
		public SnipeProjectInfo ProjectInfo;
		public string ProjectName;
		public WebSocketImplementations WebSocketImplementation = WebSocketImplementations.BestWebSocket;
		public bool AutoJoinRoom;
		public List<string> ServerWebSocketUrls;
		public List<UdpAddress> ServerUdpUrls;
		public string ServerHttpUrl;

		/// <summary>
		/// Http transport heartbeat interval.
		/// If the value is less than 1 second then heartbeat is turned off.
		/// </summary>
		public TimeSpan HttpHeartbeatInterval = TimeSpan.FromMinutes(1);

		public bool CompressionEnabled  = true;
		public int MinMessageBytesToCompress = 13 * 1024;

		public IDictionary<string, object> LoginParameters;// = new Dictionary<string, object>();

		public string LogReporterUrl;
	}
}
