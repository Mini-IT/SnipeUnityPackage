using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using MiniIT;
using UnityEngine;
using CS;

namespace MiniIT.Snipe
{
	public class SnipeServiceClient
	{
		public delegate void MessageReceivedHandler(string message_type, string error_code, ExpandoObject data, int request_id);
		public event MessageReceivedHandler MessageReceived;
		public event Action ConnectionOpened;
		public event Action ConnectionClosed;
		public event Action LoginSucceeded;
		public event Action<string> LoginFailed;

		internal const string MESSAGE_TYPE_USER_LOGIN = "user.login";
		private const string MESSAGE_TYPE_PING = "user.ping";
		
		internal const string ERROR_CODE_OK = "ok";

		private const double HEARTBEAT_INTERVAL = 30; // seconds

		protected bool mLoggedIn = false;

		public bool Connected { get { return (mWebSocket != null && mWebSocket.Connected); } }
		public bool LoggedIn { get { return mLoggedIn && Connected; } }

		public string ConnectionId { get; private set; }

		protected bool mHeartbeatEnabled = true;
		public bool HeartbeatEnabled
		{
			get { return mHeartbeatEnabled; }
			set
			{
				if (mHeartbeatEnabled != value)
				{
					mHeartbeatEnabled = value;
					if (!mHeartbeatEnabled)
						StopHeartbeat();
					else if (LoggedIn)
						StartHeartbeat();
				}
			}
		}

		private int mRequestId = 0;

		protected void RequestLogin()
		{
			if (mLoggedIn || !Connected)
				return;

			SendRequest(new MPackMap()
			{
				["t"] = MESSAGE_TYPE_USER_LOGIN,
				["data"] = new MPackMap()
				{
					["ckey"] = SnipeConfig.Instance.ClientKey,
					["id"] = SnipeAuthCommunicator.UserID,
					["token"] = SnipeAuthCommunicator.LoginToken,
					["loginGame"] = true, // Snipe V5
				}
			});
		}

		protected MPackMap ConvertToMPackMap(Dictionary<string, object> dictionary)
		{
			var map = new MPackMap();
			foreach (var pair in dictionary)
			{
				if (pair.Value is MPack mpack_value)
				{
					map.Add(MPack.From(pair.Key), mpack_value);
				}
				else if (pair.Value is Dictionary<string, object> value_dictionary)
				{
					map.Add(MPack.From(pair.Key), ConvertToMPackMap(value_dictionary));
				}
				else if (pair.Value is IList value_list)
				{
					var mpack_list = new MPackArray();
					foreach (var value_item in value_list)
					{
						if (value_item is Dictionary<string, object> value_item_dictionary)
						{
							mpack_list.Add(ConvertToMPackMap(value_item_dictionary));
						}
						else if (value_item is IExpandoObjectConvertable value_obj)
						{
							mpack_list.Add(ConvertToMPackMap(value_obj.ConvertToExpandoObject()));
						}
						else
						{
							try
							{
								mpack_list.Add(MPack.From(value_item));
							}
							catch (NotSupportedException)
							{ }
						}
						
					}
					map.Add(MPack.From(pair.Key), mpack_list);
				}
				else
				{
					if (pair.Value != null)
						map.Add(MPack.From(pair.Key), MPack.From(pair.Value));
					else
						DebugLogger.LogError($"[SnipeServiceClient] Value is null. Key = '{pair.Key}'. Null values are not supported. The parameter won't be added to the message.");
				}
			}

			return map;
		}

		protected ExpandoObject ConvertToExpandoObject(MPackMap map)
		{
			var obj = new ExpandoObject();
			foreach (string key in map.Keys)
			{
				var member = map[key];
				if (member is MPackMap member_map)
				{
					obj[key] = ConvertToExpandoObject(member_map);
				}
				else if (member is MPackArray member_array)
				{
					var list = new List<object>();
					foreach (var v in member_array)
					{
						if (v is MPackMap value_map)
							list.Add(ConvertToExpandoObject(value_map));
						else
							list.Add(v.Value);
					}
					obj[key] = list;
				}
				else
				{
					obj[key] = member.Value;
				}
			}
			return obj;
		}

		#region Web Socket

		private WebSocketWrapper mWebSocket = null;

		public void Connect()
		{
			if (mWebSocket != null)  // already connected or trying to connect
				return;

			Disconnect(); // clean up

			string url = SnipeConfig.Instance.ServiceWebsocketURL;

			DebugLogger.Log("[SnipeServiceClient] WebSocket Connect to " + url);
			
			mWebSocket = new WebSocketWrapper();
			mWebSocket.OnConnectionOpened += OnWebSocketConnected;
			mWebSocket.OnConnectionClosed += OnWebSocketClosed;
			mWebSocket.ProcessMessage += ProcessMessage;
			mWebSocket.Connect(url);
		}

		private void OnWebSocketConnected()
		{
			DebugLogger.Log("[SnipeServiceClient] OnWebSocketConnected");
			
			try
			{
				ConnectionOpened?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log("[SnipeServiceClient] OnWebSocketConnected - ConnectionOpened invokation error: " + e.Message);
			}

			RequestLogin();
		}

		protected void OnWebSocketClosed()
		{
			DebugLogger.Log("[SnipeServiceClient] OnWebSocketClosed");

			Disconnect();

			try
			{
				ConnectionClosed?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeServiceClient] OnWebSocketClosed - ConnectionClosed invokation error: {e.Message}\n{e.StackTrace}");
			}
		}

		public void Disconnect()
		{
			mLoggedIn = false;
			ConnectionId = "";

			StopHeartbeat();

			if (mWebSocket != null)
			{
				mWebSocket.OnConnectionOpened -= OnWebSocketConnected;
				mWebSocket.OnConnectionClosed -= OnWebSocketClosed;
				mWebSocket.ProcessMessage -= ProcessMessage;
				mWebSocket.Disconnect();
				mWebSocket = null;
			}
		}

		public int SendRequest(MPackMap message)
		{
			if (!Connected || message == null)
				return 0;

			message["id"] = ++mRequestId;
			
			DebugLogger.Log($"[SnipeServiceClient] SendRequest {mRequestId} - " + message["t"]);

			var bytes = message.EncodeToBytes();
			lock (mWebSocket)
			{
				mWebSocket.SendRequest(bytes);
			}

			if (mHeartbeatEnabled)
			{
				ResetHeartbeatTimer();
			}

			return mRequestId;
		}

		public int SendRequest(Dictionary<string, object> message)
		{
			return SendRequest(ConvertToMPackMap(message));
		}

		public int SendRequest(string message_type, Dictionary<string, object> data)
		{
			if (data == null)
			{
				return SendRequest(new MPackMap()
				{
					["t"] = message_type,
				});
			}
			else
			{
				return SendRequest(new MPackMap()
				{
					["t"] = message_type,
					["data"] = ConvertToMPackMap(data)
				});
			}
		}

		protected void ProcessMessage(byte[] raw_data_buffer)
		{
			var message = MPack.ParseFromBytes(raw_data_buffer) as MPackMap;

			if (message != null)
			{
				string message_type = Convert.ToString(message["t"]);
				string error_code = (message.TryGetValue("errorCode", out var message_value_error_code)) ? Convert.ToString(message_value_error_code) : "";
				int request_id = (message.TryGetValue("id", out var message_value_id)) ? Convert.ToInt32(message_value_id) : 0;
				
				DebugLogger.Log($"[SnipeServiceClient] ProcessMessage {request_id} {message_type} {error_code}");

				MPackMap response_data = null;

				if (!mLoggedIn)
				{
					if (message_type == MESSAGE_TYPE_USER_LOGIN)
					{	
						if (error_code == ERROR_CODE_OK)
						{
							DebugLogger.Log("[SnipeServiceClient] ProcessMessage - Login Succeeded");
							
							mLoggedIn = true;

							try
							{
								response_data = message["data"] as MPackMap;
								if (response_data != null)
								{
									this.ConnectionId = Convert.ToString(response_data["connectionID"]);
								}
							}
							catch (Exception)
							{
								this.ConnectionId = "";
							}

							try
							{
								LoginSucceeded?.Invoke();
							}
							catch (Exception e)
							{
								DebugLogger.Log("[SnipeServiceClient] ProcessMessage - LoginSucceeded invokation error: " + e.Message);
							}

							if (mHeartbeatEnabled)
							{
								StartHeartbeat();
							}
						}
						else
						{
							DebugLogger.Log("[SnipeServiceClient] ProcessMessage - Login Failed");
							
							try
							{
								LoginFailed?.Invoke(error_code);
							}
							catch (Exception e)
							{
								DebugLogger.Log("[SnipeServiceClient] ProcessMessage - LoginFailed invokation error: " + e.Message);
							}
						}
					}
				}

				if (MessageReceived != null)
				{
					try
					{
						if (response_data == null)
						{
							try
							{
								response_data = message["data"] as MPackMap;
							}
							catch(Exception)
							{
								response_data = null;
							}
						}
						DebugLogger.Log("[SnipeServiceClient] ProcessMessage - Invoke MessageReceived " + message_type);
						MessageReceived.Invoke(message_type, error_code, (response_data != null) ? ConvertToExpandoObject(response_data) : null, request_id);
					}
					catch (Exception e)
					{
						DebugLogger.Log("[SnipeServiceClient] ProcessMessage - MessageReceived invokation error: " + e.Message + "\n" + e.StackTrace);
					}
				}
				else
				{
					DebugLogger.Log("[SnipeServiceClient] ProcessMessage - no MessageReceived listeners");
				}

				if (mHeartbeatEnabled)
				{
					ResetHeartbeatTimer();
				}
			}
		}

		#endregion // Web Socket

		#region Heartbeat

		private long mHeartbeatTriggerTicks = 0;

		private CancellationTokenSource mHeartbeatCancellation;

		private void StartHeartbeat()
		{
			mHeartbeatCancellation?.Cancel();

			mHeartbeatCancellation = new CancellationTokenSource();
			_ = HeartbeatTask(mHeartbeatCancellation.Token);
		}

		private void StopHeartbeat()
		{
			if (mHeartbeatCancellation != null)
			{
				mHeartbeatCancellation.Cancel();
				mHeartbeatCancellation = null;
			}
		}

		private async Task HeartbeatTask(CancellationToken cancellation)
		{
			var message = new MPackMap() { ["t"] = MESSAGE_TYPE_PING };
			var bytes = message.EncodeToBytes();

			ResetHeartbeatTimer();

			await Task.Delay(5000, cancellation);

			while (!cancellation.IsCancellationRequested && Connected)
			{
				if (DateTime.Now.Ticks >= mHeartbeatTriggerTicks)
				{
					lock (mWebSocket)
					{
						mWebSocket.Ping();
					}
					ResetHeartbeatTimer();

					DebugLogger.Log("[SnipeServiceClient] Heartbeat ping");
				}

				await Task.Delay(5000, cancellation);
			}
		}

		private void ResetHeartbeatTimer()
		{
			mHeartbeatTriggerTicks = DateTime.Now.AddSeconds(HEARTBEAT_INTERVAL).Ticks;
		}

		#endregion

	}
}