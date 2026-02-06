using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using fastJSON;

namespace MiniIT.Snipe.Configuration
{
	public class SnipeOptionsBuilder
	{
		private readonly SnipeOptionsData _data = new();

		public SnipeOptionsBuilder SetProjectInfo(SnipeProjectInfo projectInfo)
		{
			_data.ProjectInfo = projectInfo;
			return this;
		}

		public SnipeOptionsBuilder SetWebSocketImplementation(WebSocketImplementations webSocketImplementation)
		{
			_data.WebSocketImplementation = webSocketImplementation;
			return this;
		}

		public SnipeOptionsBuilder SetAutoJoinRoom(bool autoJoinRoom)
		{
			_data.AutoJoinRoom = autoJoinRoom;
			return this;
		}

		public SnipeOptionsBuilder SetServerWebSocketUrls(List<string> urls)
		{
			_data.ServerWebSocketUrls = urls ?? new List<string>();
			return this;
		}

		public SnipeOptionsBuilder SetServerUdpUrls(List<UdpAddress> urls)
		{
			_data.ServerUdpUrls = urls ?? new List<UdpAddress>();
			return this;
		}

		public SnipeOptionsBuilder SetServerHttpUrls(List<string> urls)
		{
			_data.ServerHttpUrls = urls ?? new List<string>();
			return this;
		}

		public SnipeOptionsBuilder SetHeartbeatInterval(TimeSpan heartbeatInterval)
		{
			_data.HttpHeartbeatInterval = heartbeatInterval;
			return this;
		}

		public SnipeOptionsBuilder SetCompressionEnabled(bool compressionEnabled)
		{
			_data.CompressionEnabled = compressionEnabled;
			return this;
		}

		public SnipeOptionsBuilder SetMinMessageBytesToCompress(int minMessageBytesToCompress)
		{
			_data.MinMessageBytesToCompress = minMessageBytesToCompress;
			return this;
		}

		public SnipeOptionsBuilder SetLogReporterUrl(string logReporterUrl)
		{
			_data.LogReporterUrl = logReporterUrl;
			return this;
		}

		public SnipeOptionsBuilder SetLoginParameters(Dictionary<string, object> loginParameters)
		{
			_data.LoginParameters = loginParameters;
			return this;
		}

		public SnipeOptions Build(int contextId, ISnipeServices services)
		{
			return new SnipeOptions(contextId, _data, services);
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

			_data.ServerHttpUrls.Clear();
			_data.ServerHttpUrls.Add("https://dev.snipe.dev/");
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

			_data.ServerHttpUrls.Clear();
			_data.ServerHttpUrls.Add("https://live.snipe.dev/");
			_data.HttpHeartbeatInterval = TimeSpan.Zero;

			_data.LogReporterUrl = "https://logs.snipe.dev/api/v1/log/batch";
		}


		#endregion Default config

		private void ParseNew(IDictionary<string, object> data)
		{
			if (data.TryGetValue("snipeUdpUrls", out object udpUrls))
			{
				_data.ServerUdpUrls.Clear();
				ParseUdpUrls(_data.ServerUdpUrls, udpUrls);
			}
			else if (data.TryGetValue("snipeUdpHost", out string udpHost) && !string.IsNullOrWhiteSpace(udpHost) &&
			    data.TryGetValue("snipeUdpPort", out string udpPort) && ushort.TryParse(udpPort.Trim(), out ushort port))
			{
				_data.ServerUdpUrls.Clear();
				_data.ServerUdpUrls.Add(new UdpAddress() { Host = udpHost.Trim(), Port = port });
			}

			if (data.TryGetValue("snipeHttpUrls", out object httpUrls))
			{
				ParseHttpUrls(_data.ServerHttpUrls, httpUrls);
			}
			else if (data.TryGetValue("snipeHttpUrl", out string httpUrl) && !string.IsNullOrWhiteSpace(httpUrl))
			{
				_data.ServerHttpUrls.Clear();
				_data.ServerHttpUrls.Add(httpUrl.Trim());
			}

			if (data.TryGetValue("snipeWssUrl", out object wssUrl))
			{
				ParseWebSocketUrls(_data.ServerWebSocketUrls, wssUrl);
			}

			if (data.TryGetValue("snipeDev", out bool dev))
			{
				_data.ProjectInfo.Mode = dev ? SnipeProjectMode.Dev : SnipeProjectMode.Live;
			}

			ParseLogReporterSection(data);
			ParseCompressionSection(data);
		}

		// [Testable]
		internal static void ParseUdpUrls(List<UdpAddress> outputList, object input)
		{
			var urls = new List<string>();
			ParseUrls(urls, input, (url) =>
			{
				string[] parts = url.Split("://");
				url = parts[^1];
				parts = url.ToLower().Split(':');
				return parts.Length == 2 && !string.IsNullOrEmpty(parts[0]) && ushort.TryParse(parts[1], out ushort port);
			});

			foreach (var item in urls)
			{
				string[] parts = item.Split("://");
				string url = parts[^1];
				parts = url.ToLower().Split(':');

				if (parts.Length == 2 && ushort.TryParse(parts[1], out ushort port))
				{
					outputList.Add(new UdpAddress() { Host = parts[0], Port = port });
				}
			}
		}

		// [Testable]
		internal static void ParseWebSocketUrls(List<string> outputList, object input)
		{
			ParseUrls(outputList, input, (url) => url.ToLower().StartsWith("wss://"));
		}

		// [Testable]
		internal static void ParseHttpUrls(List<string> outputList, object input)
		{
			ParseUrls(outputList, input, (url) => url.ToLower().StartsWith("https://"));
		}

		private static void ParseUrls(List<string> outputList, object input, Func<string, bool> urlChecker)
		{
			if (input is IList urlList && urlList.Count > 0)
			{
				SetUrls(outputList, urlList, urlChecker);
			}
			else if (input is string urlString && !string.IsNullOrWhiteSpace(urlString))
			{
				urlString = urlString.Trim();
				string lowerUrl = urlString.ToLower();

				if (lowerUrl.StartsWith('['))
				{
					IList list;
					try
					{
						list = (IList)JSON.Parse(urlString);
					}
					catch (Exception)
					{
						list = null;
					}

					if (list != null && list.Count > 0)
					{
						SetUrls(outputList, list, urlChecker);
					}
				}
				else if (urlChecker.Invoke(lowerUrl))
				{
					outputList.Clear();
					outputList.Add(urlString);
				}
			}
		}

		private static void SetUrls(List<string> outputList, IList wssUrlList, Func<string, bool> urlChecker)
		{
			outputList.Clear();

			foreach (var listItem in wssUrlList)
			{
				if (listItem is string url && !string.IsNullOrWhiteSpace(url))
				{
					url = url.Trim();
					if (urlChecker.Invoke(url))
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
