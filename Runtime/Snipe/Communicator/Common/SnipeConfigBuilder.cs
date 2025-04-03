using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using fastJSON;

namespace MiniIT.Snipe.Configuration
{
	public class SnipeConfigBuilder
	{
		private readonly SnipeConfigData _data = new();

		public SnipeConfigBuilder SetProjectInfo(SnipeProjectInfo projectInfo)
		{
			_data.ProjectInfo = projectInfo;
			return this;
		}

		public SnipeConfigBuilder SetWebSocketImplementation(WebSocketImplementations webSocketImplementation)
		{
			_data.WebSocketImplementation = webSocketImplementation;
			return this;
		}

		public SnipeConfigBuilder SetAutoJoinRoom(bool autoJoinRoom)
		{
			_data.AutoJoinRoom = autoJoinRoom;
			return this;
		}

		public SnipeConfigBuilder SetServerWebSocketUrls(List<string> serverWebSocketUrls)
		{
			serverWebSocketUrls ??= new List<string>();
			_data.ServerWebSocketUrls = serverWebSocketUrls;
			return this;
		}

		public SnipeConfigBuilder SetServerUdpUrls(List<UdpAddress> serverUdpUrls)
		{
			serverUdpUrls ??= new List<UdpAddress>();
			_data.ServerUdpUrls = serverUdpUrls;
			return this;
		}

		public SnipeConfigBuilder SetHeartbeatInterval(TimeSpan heartbeatInterval)
		{
			_data.HttpHeartbeatInterval = heartbeatInterval;
			return this;
		}

		public SnipeConfigBuilder SetCompressionEnabled(bool compressionEnabled)
		{
			_data.CompressionEnabled = compressionEnabled;
			return this;
		}

		public SnipeConfigBuilder SetMinMessageBytesToCompress(int minMessageBytesToCompress)
		{
			_data.MinMessageBytesToCompress = minMessageBytesToCompress;
			return this;
		}

		public SnipeConfigBuilder SetLogReporterUrl(string logReporterUrl)
		{
			_data.LogReporterUrl = logReporterUrl;
			return this;
		}

		public SnipeConfigBuilder SetLoginParameters(Dictionary<string, object> loginParameters)
		{
			_data.LoginParameters = loginParameters;
			return this;
		}

		public SnipeConfig Build(int contextId)
		{
			return new SnipeConfig(contextId, _data);
		}

		//----------------------

		public void Initialize(SnipeProjectInfo project, IDictionary<string, object> data)
		{
			SetProjectInfo(project);

			ParseNew(data);
			ParseLogReporterSection(data);
			ParseCompressionSection(data);
		}

		/// <summary>
		/// Initialize with default values
		/// </summary>
		public void InitializeDefault(SnipeProjectInfo project)
		{
			SetProjectInfo(project);

			if (project.Mode == SnipeProjectMode.Dev)
			{
				InitializeDefaultConnectionDev();
				//InitializeDefaultTablesConfigDev();
			}
			else
			{
				InitializeDefaultConnectionLive();
				//InitializeDefaultTablesConfigLive();
			}

			//InitializeUrlIndices();

			//InitializeAppInfo();
		}

		#region Default config

		private void InitializeDefaultConnectionDev()
		{
			_data.ServerUdpUrls.Clear();
			_data.ServerUdpUrls.Add(new UdpAddress() { Host = "dev.snipe.dev", Port = 10666 });

			_data.ServerWebSocketUrls.Clear();
			_data.ServerWebSocketUrls.Add("wss://dev.snipe.dev/wss_11000/");
			_data.ServerWebSocketUrls.Add("wss://dev-proxy.snipe.dev/wss_11000/");
			_data.ServerWebSocketUrls.Add("wss://dev2.snipe.dev/wss_11000/");
			_data.ServerWebSocketUrls.Add("wss://dev-proxy2.snipe.dev/wss_11000/");

			_data.ServerHttpUrl = "https://dev.snipe.dev/";
			_data.HttpHeartbeatInterval = TimeSpan.FromMinutes(1);

			_data.LogReporterUrl = "https://logs-dev.snipe.dev/api/v1/log/batch";
		}

		private void InitializeDefaultConnectionLive()
		{
			_data.ServerUdpUrls.Clear();
			_data.ServerUdpUrls.Add(new UdpAddress() { Host = "live.snipe.dev", Port = 16666 });

			_data.ServerWebSocketUrls.Clear();
			_data.ServerWebSocketUrls.Add("wss://live.snipe.dev/wss_16000/");
			_data.ServerWebSocketUrls.Add("wss://live-proxy.snipe.dev/wss_16000/");
			_data.ServerWebSocketUrls.Add("wss://live2.snipe.dev/wss_16000/");
			_data.ServerWebSocketUrls.Add("wss://live-proxy2.snipe.dev/wss_16000/");

			_data.ServerHttpUrl = "https://live.snipe.dev/";
			_data.HttpHeartbeatInterval = TimeSpan.Zero;

			_data.LogReporterUrl = "https://logs.snipe.dev/api/v1/log/batch";
		}



		#endregion Default config

		private void ParseNew(IDictionary<string, object> data)
		{
			if (data.TryGetValue("snipeUdpHost", out string udpHost) && !string.IsNullOrWhiteSpace(udpHost) &&
			    data.TryGetValue("snipeUdpPort", out string udpPort) && ushort.TryParse(udpPort.Trim(), out ushort port))
			{
				_data.ServerUdpUrls.Clear();
				_data.ServerUdpUrls.Add(new UdpAddress() { Host = udpHost.Trim(), Port = port });
			}

			if (data.TryGetValue("snipeHttpUrl", out string httpUrl) && !string.IsNullOrWhiteSpace(httpUrl))
			{
				_data.ServerHttpUrl = httpUrl.Trim();
			}

			if (data.TryGetValue("snipeWssUrl", out object wssUrl))
			{
				List<string> outputList = _data.ServerWebSocketUrls;
				ParseWebSocketUrls(outputList, wssUrl);
			}

			if (data.TryGetValue("snipeDev", out bool dev))
			{
				_data.ProjectInfo.Mode = dev ? SnipeProjectMode.Dev : SnipeProjectMode.Live;
			}

			ParseLogReporterSection(data);
			ParseCompressionSection(data);
		}

		// [Testable]
		internal static void ParseWebSocketUrls(List<string> outputList, object wssUrl)
		{
			if (wssUrl is IList wssUrlList && wssUrlList.Count > 0)
			{
				SetWebSocketUrls(outputList, wssUrlList);
			}
			else if (wssUrl is string wssUrlString && !string.IsNullOrWhiteSpace(wssUrlString))
			{
				wssUrlString = wssUrlString.Trim();
				string lowerUrl = wssUrlString.ToLower();

				if (lowerUrl.StartsWith('['))
				{
					IList list;
					try
					{
						list = (IList)JSON.Parse(wssUrlString);
					}
					catch (Exception)
					{
						list = null;
					}

					if (list != null && list.Count > 0)
					{
						SetWebSocketUrls(outputList, list);
					}
				}
				else if (lowerUrl.StartsWith("wss://"))
				{
					outputList.Clear();
					outputList.Add(wssUrlString);
				}
			}
		}

		private static void SetWebSocketUrls(List<string> outputList, IList wssUrlList)
		{
			outputList.Clear();

			foreach (var listItem in wssUrlList)
			{
				if (listItem is string url && !string.IsNullOrWhiteSpace(url))
				{
					url = url.Trim();
					if (url.ToLower().StartsWith("wss://"))
					{
						outputList.Add(url);
					}
				}
			}
		}


		private void ParseLogReporterSection(IDictionary<string, object> data)
		{
			if (data.TryGetValue("log_reporter", out var logReporterField))
			{
				if (logReporterField is IDictionary<string, object> logReporterSection)
				{
					_data.LogReporterUrl = logReporterSection.SafeGetString("url").Trim();
				}
				else if (logReporterField is string logReporterString)
				{
					var regex = new Regex(@"^http.?://.*", RegexOptions.IgnoreCase); // == StartsWith("http(s)://")

					if (regex.IsMatch(logReporterString))
					{
						_data.LogReporterUrl = logReporterString;
					}
					else
					{
						var dict = (Dictionary<string, object>)JSON.Parse(logReporterString);
						if (dict != null)
						{
							_data.LogReporterUrl = dict.SafeGetString("url").Trim();
						}
					}
				}
			}
		}

		private void ParseCompressionSection(IDictionary<string, object> data)
		{
			if (!data.TryGetValue("compression", out var compressionField) ||
			    compressionField is not IDictionary<string, object> compression)
			{
				return;
			}
			
			_data.CompressionEnabled = compression.SafeGetValue<bool>("enabled");

			if (!_data.CompressionEnabled)
			{
				return;
			}

			if (compression.TryGetValue("min_size", out int minSize) ||
			    compression.TryGetValue("minSize", out minSize))
			{
				_data.MinMessageBytesToCompress = minSize;
			}
		}
	}
}
