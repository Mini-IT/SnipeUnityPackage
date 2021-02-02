using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
		
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; // ms

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
		private ConcurrentQueue<MPackMap> mSendMessages;

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
			
			StopSendTask();
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
			
			if (mSendMessages == null)
			{
				StartSendTask();
			}
			mSendMessages.Enqueue(message);
			
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
		
		private void DoSendRequest(MPackMap message)
		{
			if (!Connected || message == null)
				return;
			
			DebugLogger.Log($"[SnipeServiceClient] DoSendRequest - {mRequestId} - {message.ToString()}");

			var bytes = message.EncodeToBytes();
			lock (mWebSocket)
			{
				mWebSocket.SendRequest(bytes);
			}

			if (mHeartbeatEnabled)
			{
				ResetHeartbeatTimer();
			}
		}
		
		protected void RequestLogin()
		{
			if (mLoggedIn || !Connected)
				return;

			DoSendRequest(new MPackMap()
			{
				["t"] = SnipeMessageTypes.USER_LOGIN,
				["data"] = new MPackMap()
				{
					["ckey"] = SnipeConfig.Instance.ClientKey,
					["id"] = SnipeAuthCommunicator.UserID,
					["token"] = SnipeAuthCommunicator.LoginToken,
					["loginGame"] = true, // Snipe V5
					["appInfo"] = SnipeConfig.Instance.AppInfo,
				}
			});
		}

		protected void ProcessMessage(byte[] raw_data_buffer)
		{
			var message = MPack.ParseFromBytes(raw_data_buffer) as MPackMap;

			if (message != null)
			{
				string message_type = Convert.ToString(message["t"]);
				string error_code = (message.TryGetValue("errorCode", out var message_value_error_code)) ? Convert.ToString(message_value_error_code) : "";
				int request_id = (message.TryGetValue("id", out var message_value_id)) ? Convert.ToInt32(message_value_id) : 0;
				
				MPackMap response_mpack_data = null;
				ExpandoObject response_data = null;
				try
				{
					response_mpack_data = message["data"] as MPackMap;
					if (response_mpack_data != null)
						response_data = ConvertToExpandoObject(response_mpack_data);
				}
				catch(Exception)
				{
					response_mpack_data = null;
					response_data = null;
				}
				
				DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - {request_id} - {message_type} {error_code} {response_data?.ToJSONString()}");

				if (!mLoggedIn)
				{
					if (message_type == SnipeMessageTypes.USER_LOGIN)
					{	
						if (error_code == SnipeErrorCodes.OK)
						{
							DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - Login Succeeded");
							
							mLoggedIn = true;

							if (response_mpack_data != null)
							{
								try
								{
									this.ConnectionId = Convert.ToString(response_mpack_data["connectionID"]);
								}
								catch (Exception)
								{
									this.ConnectionId = "";
								}
							}

							try
							{
								LoginSucceeded?.Invoke();
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - LoginSucceeded invokation error: " + e.Message);
							}

							if (mHeartbeatEnabled)
							{
								StartHeartbeat();
							}
						}
						else
						{
							DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - Login Failed");
							
							try
							{
								LoginFailed?.Invoke(error_code);
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - LoginFailed invokation error: " + e.Message);
							}
						}
					}
				}

				if (MessageReceived != null)
				{
					try
					{
						MessageReceived.Invoke(message_type, error_code, response_data, request_id);
					}
					catch (Exception e)
					{
						DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - MessageReceived invokation error: " + e.Message + "\n" + e.StackTrace);
					}
				}
				else
				{
					DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] ProcessMessage - no MessageReceived listeners");
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
			ResetHeartbeatTimer();

			await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);

			while (!cancellation.IsCancellationRequested && Connected)
			{
				if (DateTime.UtcNow.Ticks >= mHeartbeatTriggerTicks)
				{
					lock (mWebSocket)
					{
						mWebSocket.Ping();
					}
					ResetHeartbeatTimer();

					DebugLogger.Log($"[SnipeServiceClient] [{ConnectionId}] Heartbeat ping");
				}

				await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);
			}
		}

		private void ResetHeartbeatTimer()
		{
			mHeartbeatTriggerTicks = DateTime.UtcNow.AddSeconds(HEARTBEAT_INTERVAL).Ticks;
		}

		#endregion
		
		#region Send task

		private CancellationTokenSource mSendTaskCancellation;

		private void StartSendTask()
		{
			mSendTaskCancellation?.Cancel();
			
			mSendMessages = new ConcurrentQueue<MPackMap>();

			mSendTaskCancellation = new CancellationTokenSource();
			_ = SendTask(mHeartbeatCancellation.Token);
		}

		private void StopSendTask()
		{
			if (mSendTaskCancellation != null)
			{
				mSendTaskCancellation.Cancel();
				mSendTaskCancellation = null;
			}
			
			mSendMessages = null;
		}

		private async Task SendTask(CancellationToken cancellation)
		{
			while (!cancellation.IsCancellationRequested && Connected)
			{
				if (mSendMessages != null && !mSendMessages.IsEmpty && mSendMessages.TryDequeue(out var message))
				{
					DoSendRequest(message);
				}

				await Task.Yield();
			}
			
			mSendMessages = null;
		}

		#endregion
	}
}