using System;
using System.Collections.Generic;
using UnityEngine;
using MiniIT;

using System.Threading;
using System.Threading.Tasks;

//
// http://snipeserver.com
// https://github.com/Mini-IT/SnipeWiki/wiki


// Docs on how to use TCP Client:
// http://sunildube.blogspot.ru/2011/12/asynchronous-tcp-client-easy-example.html

namespace MiniIT.Snipe
{
	public class SnipeClient : MonoBehaviour, IDisposable
	{
		public const string KEY_MESSAGE_TYPE = "messageType";
		public const string KEY_REQUEST_ID = "_requestID";
		public const string KEY_CONNECTION_ID = "_connectionID";
		
		private const string MESSAGE_TYPE_USER_LOGIN = "user.login";
		private const string MESSAGE_TYPE_AUTH_LOGIN = "auth/user.login";
		private const string MESSAGE_TYPE_PING = "kit/user.ping";
		
		private const double HEARTBEAT_INTERVAL = 30;      // seconds
		private const int CHECK_CONNECTION_TIMEOUT = 2000; // milliseconds
		
		private static readonly ExpandoObject PING_MESSAGE_DATA = new ExpandoObject() { [KEY_MESSAGE_TYPE] = MESSAGE_TYPE_PING };

		private string mClientKey;
		public string ClientKey
		{
			get { return mClientKey; }
			set
			{
				if (mClientKey != value)
				{
					mClientKey = value;
					mClientKeySent = false;
				}
			}
		}
		private bool mClientKeySent;

		private string mAppInfo;
		public string AppInfo
		{
			get { return mAppInfo; }
			set
			{
				if (mAppInfo != value)
				{
					mAppInfo = value;
					mClientKeySent = false;
				}
			}
		}

		public string ConnectionId { get; private set; }

		protected bool mConnected = false;
		protected bool mHeartbeatEnabled = true;
		protected bool mLoggedIn = false;

		private string mConnectionHost;
		private int mConnectionPort;
		private string mConnectionWebSocketURL;

		private int mRequestId = 0;
		
		private bool mProcessingReceivedMessage = false;
		private Queue<ExpandoObject> mRequestsQueue;

		public static SnipeClient CreateInstance(string client_key, string name = "SnipeClient", bool heartbeat_enabled = true)
		{
			SnipeClient instance = new GameObject(name).AddComponent<SnipeClient>();
			instance.ClientKey = client_key;
			instance.mHeartbeatEnabled = heartbeat_enabled;
			DontDestroyOnLoad(instance.gameObject);
			return instance;
		}

		public static SnipeClient CreateInstance(string client_key, GameObject game_object = null, bool heartbeat_enabled = true)
		{
			SnipeClient instance;

			if (game_object == null)
			{
				instance = CreateInstance(client_key, "SnipeClient", heartbeat_enabled);
			}
			else
			{
				instance = game_object.AddComponent<SnipeClient>();
				instance.ClientKey = client_key;
				instance.mHeartbeatEnabled = heartbeat_enabled;
			}

			return instance;
		}

		internal class QueuedEvent
		{
			internal Action<ExpandoObject> handler;
			internal ExpandoObject data;

			internal QueuedEvent(Action<ExpandoObject> handler, ExpandoObject data)
			{
				this.handler = handler;
				this.data = data;
			}
		}
		private Queue<QueuedEvent> mDispatchEventQueue = new Queue<QueuedEvent>();

		#pragma warning disable 0067

		public event Action<ExpandoObject> ConnectionSucceeded;
		public event Action<ExpandoObject> ConnectionFailed;
		public event Action<ExpandoObject> ConnectionLost;
		//public event Action<ExpandoObject> ErrorHappened;
		public event Action<ExpandoObject> MessageReceived;

		#pragma warning restore 0067

		private SnipeTCPClient mTCPClient = null;
		private SnipeWebSocketClient mWebSocketClient = null;

		// DEBUG
		public string DisconnectReason { get; private set; }

		public SnipeClient()
		{
		}

		public void Init(string tcp_host, int tcp_port, string web_socket_url = "")
		{
			mConnectionHost = tcp_host;
			mConnectionPort = tcp_port;
			mConnectionWebSocketURL = web_socket_url;
		}

		private void DispatchEvent(Action<ExpandoObject> handler, ExpandoObject data = null)
		{
			mDispatchEventQueue.Enqueue(new QueuedEvent(handler, data));
		}

		private void DoDispatchEvent(Action<ExpandoObject> handler, ExpandoObject data)
		{
			Action<ExpandoObject> event_handler = handler;  // local variable for thread safety
			if (event_handler != null)
			{
				try
				{
					event_handler.Invoke(data);
				}
				catch (Exception e)
				{
					DebugLogger.Log("[SnipeClient] DispatchEvent error: " + e.ToString() + e.Message);
					DebugLogger.Log("[SnipeClient] ErrorData: " + (data != null ? data.ToJSONString() : "null"));
				}
			}
		}

		void Update()
		{
			while (mDispatchEventQueue != null && mDispatchEventQueue.Count > 0)
			{
				QueuedEvent item = mDispatchEventQueue.Dequeue();
				if (item == null)
					continue;

				DoDispatchEvent(item.handler, item.data);

				// item.handler could have called Dispose
				if (!ClientIsValid)
					return;
			}
		}

		public void Connect()
		{
			DebugLogger.Log($"[SnipeClient] Connect - {mConnectionHost} : {mConnectionPort} ws: {mConnectionWebSocketURL}");

			if (!string.IsNullOrEmpty(mConnectionHost) && mConnectionPort > 0)
			{
				ConnectTCP();
			}
			else if (!string.IsNullOrEmpty(mConnectionWebSocketURL))
			{
				ConnectWebSocket();
			}
		}
		
		private void ConnectTCP()
		{
			if (mTCPClient == null)
			{
				mTCPClient = new SnipeTCPClient();
				mTCPClient.OnConnectionSucceeded = OnTCPConnectionSucceeded;
				mTCPClient.OnConnectionFailed = OnTCPConnectionFailed;
				mTCPClient.OnConnectionLost = OnConnectionLost;
				mTCPClient.OnMessageReceived = OnMessageReceived;
			}
			mTCPClient.Connect(mConnectionHost, mConnectionPort);
		}
		
		public void ConnectWebSocket()
		{
			ConnectionId = "";

			if (mWebSocketClient == null)
			{
				mWebSocketClient = new SnipeWebSocketClient();
				mWebSocketClient.OnConnectionSucceeded = OnWebSocketConnectionSucceeded;
				mWebSocketClient.OnConnectionFailed = OnWebSocketConnectionFailed;
				mWebSocketClient.OnConnectionLost = OnConnectionLost;
				mWebSocketClient.OnMessageReceived = OnMessageReceived;
			}
			mWebSocketClient.Connect(mConnectionWebSocketURL);
		}
		
		private void OnTCPConnectionSucceeded()
		{
			if (mWebSocketClient != null)
			{
				mWebSocketClient.Dispose();
				mWebSocketClient = null;
			}
			
			mConnected = true;
			mClientKeySent = false;
			mLoggedIn = false;

			StopCheckConnection();

			DispatchEvent(ConnectionSucceeded);
		}
		
		private void OnTCPConnectionFailed()
		{
			mConnected = false;
			mLoggedIn = false;

			if (!string.IsNullOrEmpty(mConnectionWebSocketURL))
			{
				ConnectWebSocket();
			}
			else
			{
				DisconnectReason = "OnTCPConnectionFailed";
				DispatchEvent(ConnectionFailed);

				ConnectionId = "";
			}
		}
		
		private void OnConnectionLost()
		{
			mConnected = false;
			mLoggedIn = false;
			DisconnectReason = "OnConnectionLost";
			DispatchEvent(ConnectionLost);

			ConnectionId = "";
		}
		
		private void OnMessageReceived(ExpandoObject data)
		{
			StopCheckConnection();
			
			if (data != null && data.ContainsKey(KEY_CONNECTION_ID))
				ConnectionId = data.SafeGetString(KEY_CONNECTION_ID);

			if (!mLoggedIn && data != null)
			{
				string message_type = data.SafeGetString("type");
				if (message_type == MESSAGE_TYPE_USER_LOGIN)
				{
					string error_code = data.SafeGetString("errorCode");
					if (error_code == "ok")
					{
						mLoggedIn = true;
						StartHeartbeat();
					}
					
					Analytics.TrackEvent(Analytics.EVENT_LOGIN_RESPONSE_RECEIVED, new ExpandoObject()
					{
						["connection_id"] = ConnectionId,
						["request_id"] = data.SafeGetString(KEY_REQUEST_ID),
						["error_code"] = error_code,
					});
				}
				else if (message_type == MESSAGE_TYPE_AUTH_LOGIN)
				{
					string error_code = data.SafeGetString("errorCode");
					
					Analytics.TrackEvent(Analytics.EVENT_AUTH_LOGIN_RESPONSE_RECEIVED, new ExpandoObject()
					{
						["connection_id"] = ConnectionId,
						["request_id"] = data.SafeGetString(KEY_REQUEST_ID),
						["error_code"] = error_code,
					});
				}
			}

			mProcessingReceivedMessage = true;
			DispatchEvent(MessageReceived, data);
			mProcessingReceivedMessage = false;
			
			if (mRequestsQueue != null)
			{
				while (mRequestsQueue.Count > 0)
				{
					SendRequest(mRequestsQueue.Dequeue());
				}
			}
		}
		
		private void OnWebSocketConnectionSucceeded()
		{
			if (mTCPClient != null)
			{
				mTCPClient.Dispose();
				mTCPClient = null;
			}
			
			mConnected = true;
			mClientKeySent = false;
			mLoggedIn = false;

			StopCheckConnection();
			ResetHeartbeatTimer();

			DispatchEvent(ConnectionSucceeded);
		}
		
		private void OnWebSocketConnectionFailed()
		{
			mConnected = false;
			mLoggedIn = false;
			DisconnectReason = "OnWebSocketConnectionFailed";
			DispatchEvent(ConnectionFailed);
		}
		
		public void Reconnect()
		{
			if (Connected)
				return;

			Connect();
		}
		
	
		public void Disconnect()
		{
			DisconnectReason = "Disconnect called explicitly";
			DisconnectAndDispatch(null);
		}
		
		public void DisconnectAndDispatch(Action<ExpandoObject> event_to_dispatch)
		{
			DebugLogger.LogWarning("[SnipeClient] DisconnectAndDispatch. " + DisconnectReason);
			
			if (mTCPClient != null)
			{
				mTCPClient.Dispose();
				mTCPClient = null;
			}
			
			if (mWebSocketClient != null)
			{
				mWebSocketClient.Dispose();
				mWebSocketClient = null;
			}
			
			mConnected = false;
			mLoggedIn = false;
			mProcessingReceivedMessage = false;
			
			StopHeartbeat();
			StopCheckConnection();

			if (event_to_dispatch != null)
				DispatchEvent(event_to_dispatch);

			ConnectionId = "";
		}

		public int SendRequest(string message_type, ExpandoObject parameters = null)
		{
			if (parameters == null)
				parameters = new ExpandoObject();
			
			parameters[KEY_MESSAGE_TYPE] = message_type;

			return SendRequest(parameters);
		}

		public int SendRequest(ExpandoObject parameters)
		{
			if (parameters == null)
				return 0;
			
			if (!parameters.ContainsKey(KEY_REQUEST_ID))
			{
				parameters[KEY_REQUEST_ID] = ++mRequestId;
			}
			
			if (mProcessingReceivedMessage)
			{
				if (mRequestsQueue == null)
					mRequestsQueue = new Queue<ExpandoObject>();
				mRequestsQueue.Enqueue(parameters);
				return mRequestId;
			}

			DebugLogger.Log($"[SnipeClient] [{ConnectionId}] SendRequest " + parameters.ToJSONString());

			// mTcpClient.Connected property gets the connection state of the Socket as of the LAST I/O operation (not current state!)
			// (http://msdn.microsoft.com/en-us/library/system.net.sockets.socket.connected.aspx)
			// So we need to check the connection availability manually, and here is where we can do it

			if (this.Connected)
			{
				if (!mClientKeySent && !string.IsNullOrEmpty(ClientKey))
				{
					parameters["clientKey"] = ClientKey;
					mClientKeySent = true;

					if (!string.IsNullOrEmpty(mAppInfo))
						parameters["appInfo"] = mAppInfo;
				}

				ResetHeartbeatTimer();

				if (mTCPClient != null)
				{
					string message = HaxeSerializer.Run(parameters);
					
					lock (mTCPClient)
					{
						mTCPClient.SendRequest(message);
					}
				}
				else if (mWebSocketClient != null)
				{
					string message = HaxeSerializer.Run(parameters);
					
					lock (mWebSocketClient)
					{
						mWebSocketClient.SendRequest(message);
					}
				}
				
				string message_type = parameters.SafeGetString(KEY_MESSAGE_TYPE);
				
				if (!mLoggedIn)
				{
					if (message_type == MESSAGE_TYPE_USER_LOGIN)
					{
						Analytics.TrackEvent(Analytics.EVENT_LOGIN_REQUEST_SENT, new ExpandoObject()
						{
							["connection_id"] = ConnectionId,
							["request_id"] = parameters[KEY_REQUEST_ID],
						});
					}
					else if (message_type == MESSAGE_TYPE_AUTH_LOGIN)
					{
						Analytics.TrackEvent(Analytics.EVENT_AUTH_LOGIN_REQUEST_SENT, new ExpandoObject()
						{
							["connection_id"] = ConnectionId,
							["request_id"] = parameters[KEY_REQUEST_ID],
						});
					}
				}
				
				StartCheckConnection(message_type);
			}
			else
			{
				CheckConnectionLost();
			}

			return mRequestId;
		}

		protected void SendPingRequest()
		{
			if (mLoggedIn)
			{
				SendRequest(PING_MESSAGE_DATA);
			}
		}

		protected bool CheckConnectionLost()
		{
			if (mConnected && !this.Connected)
			{
				// Disconnect detected
				mConnected = false;
				DisconnectReason = "CheckConnectionLost";
				DisconnectAndDispatch(ConnectionLost);
				return true;
			}
			return false;
		}

		private bool ClientIsValid
		{
			get
			{
				return mTCPClient != null || mWebSocketClient != null;
			}
		}

		public bool Connected
		{
			get
			{
				return mConnected && ((mTCPClient != null && mTCPClient.Connected) || (mWebSocketClient != null && mWebSocketClient.Connected));
			}
		}

		public bool LoggedIn
		{
			get
			{
				return Connected && mLoggedIn;
			}
		}

		public bool ConnectedViaWebSocket
		{
			get
			{
				return mWebSocketClient != null && mWebSocketClient.Connected;
			}
		}

		#region IDisposable implementation
		
		public void Dispose ()
		{
			Disconnect();

			if (this.gameObject != null)
			{
				GameObject.DestroyImmediate(this.gameObject);
			}
		}

		#endregion

		private void OnApplicationFocus(bool focus)
		{
			DebugLogger.Log($"[SnipeClient] OnApplicationFocus focus = {focus}");

			if (focus)
			{
				SendPingRequest(); // check connection
			}
			else
			{
				StopCheckConnection();
			}
		}

		#region Heartbeat and CheckConnection

		private long mHeartbeatTriggerTicks = 0;

		private CancellationTokenSource mHeartbeatCancellation;
		private CancellationTokenSource mCheckConnectionCancellation;
		
		// DEBUG
		private string mCheckConnectionMessageType;

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

			await Task.Delay(5000, cancellation);

			while (!cancellation.IsCancellationRequested && Connected)
			{
				if (DateTime.Now.Ticks >= mHeartbeatTriggerTicks)
				{
					SendPingRequest();
					ResetHeartbeatTimer();

					DebugLogger.Log("[SnipeClient] Heartbeat ping");
				}

				await Task.Delay(5000, cancellation);
			}
		}

		private void ResetHeartbeatTimer()
		{
			mHeartbeatTriggerTicks = DateTime.Now.AddSeconds(HEARTBEAT_INTERVAL).Ticks;
		}

		private void StartCheckConnection(string message_type)
		{
			if (!mLoggedIn)
			{
				// DebugLogger.Log("[SnipeClient] StartCheckConnection - not logged in yet.");
				return;
			}
			
			mCheckConnectionMessageType = message_type;
			
			DebugLogger.Log("[SnipeClient] StartCheckConnection");

			mCheckConnectionCancellation?.Cancel();

			mCheckConnectionCancellation = new CancellationTokenSource();
			_ = CheckConnectionTask(mCheckConnectionCancellation.Token);
		}

		private void StopCheckConnection()
		{
			if (mCheckConnectionCancellation != null)
			{
				mCheckConnectionCancellation.Cancel();
				mCheckConnectionCancellation = null;

				DebugLogger.Log("[SnipeClient] StopCheckConnection");
			}
		}

		private async Task CheckConnectionTask(CancellationToken cancellation)
		{
			await Task.Delay(CHECK_CONNECTION_TIMEOUT, cancellation);

			// if the connection is ok then this task should already be cancelled
			if (cancellation.IsCancellationRequested)
				return;

			// Disconnect detected
			DebugLogger.Log("[SnipeClient] CheckConnectionTask - Disconnect detected");

			mConnected = false;
			DisconnectReason = "CheckConnectionTask - Disconnect detected - " + mCheckConnectionMessageType;
			DisconnectAndDispatch(ConnectionLost);
		}

		#endregion
	}

}