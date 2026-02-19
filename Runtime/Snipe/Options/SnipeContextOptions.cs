using System;
using System.Collections.Generic;
using MiniIT.Snipe.Configuration;

namespace MiniIT.Snipe
{
	public sealed class SnipeContextOptions
	{
		public WebSocketImplementations WebSocketImplementation = WebSocketImplementations.BestWebSocket;
		public bool AutoJoinRoom = true;
		public List<string> ServerWebSocketUrls = new List<string>();
		public List<UdpAddress> ServerUdpUrls = new List<UdpAddress>();
		public List<string> ServerHttpUrls = new List<string>();
		public TimeSpan HttpHeartbeatInterval = TimeSpan.FromMinutes(1);
		public bool CompressionEnabled = true;
		public int MinMessageBytesToCompress = 13 * 1024;
		public Dictionary<string, object> LoginParameters;
		public string LogReporterUrl;
	}
}
