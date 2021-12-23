using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using MiniIT.MessagePack;

using kcp2k;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeClient
	{
		public const int SNIPE_VERSION = 6;
		
		public delegate void MessageReceivedHandler(string message_type, string error_code, SnipeObject data, int request_id);
		public event MessageReceivedHandler MessageReceived;
		public event Action ConnectionOpened;
		public event Action ConnectionClosed;
		public event Action LoginSucceeded;
		public event Action<string> LoginFailed;
		public event Action UdpConnectionFailed;
		
		private const double HEARTBEAT_INTERVAL = 30; // seconds
		private const int HEARTBEAT_TASK_DELAY = 5000; //milliseconds
		private const int CHECK_CONNECTION_TIMEOUT = 5000; // milliseconds

		private bool mConnected = false;
		protected bool mLoggedIn = false;

		public bool Connected => UdpClientConnected || WebSocketConnected;
		public bool LoggedIn { get { return mLoggedIn && Connected; } }

		public string ConnectionId { get; private set; }
		public bool BadConnection { get; private set; } = false;

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
		
		private Stopwatch mConnectionStopwatch;
		private Stopwatch mPingStopwatch;
		
		private Stopwatch mServerReactionStopwatch;
		public TimeSpan CurrentRequestElapsed { get { return mServerReactionStopwatch?.Elapsed ?? new TimeSpan(0); } }
		public TimeSpan ServerReaction { get; private set; }

		private int mRequestId = 0;
		private ConcurrentQueue<SnipeObject> mSendMessages;
		
		public void Connect(bool udp = true)
		{
			if (udp && !string.IsNullOrEmpty(SnipeConfig.ServerUdpAddress) && SnipeConfig.ServerUdpPort > 0)
				ConnectUdpClient();
			else
				ConnectWebSocket();
		}
		
		#region UdpClient
		
		private KcpClient mUdpClient;
		private bool mUdpClientConnected;
		
		private const byte OPCODE_AUTHENTICATION_REQUEST = 1;
		private const byte OPCODE_AUTHENTICATION_RESPONSE = 2;
		private const byte OPCODE_AUTHENTICATED = 3;
		private const byte OPCODE_SNIPE_REQUEST = 4;
		private const byte OPCODE_SNIPE_RESPONSE = 5;
		
		public bool UdpClientConnected => mUdpClient != null && mUdpClient.connected;
		
		private void ConnectUdpClient()
		{	
			mUdpClient = new KcpClient(
				OnUdpClientConnected,
				(message, channel) => OnUdpClientDataReceived(message),
				OnUdpClientDisconnected);
			
			mUdpClient.Connect(
				SnipeConfig.ServerUdpAddress,
				SnipeConfig.ServerUdpPort,
				true,  // NoDelay is recommended to reduce latency
				10,    // KCP internal update interval. 100ms is KCP default, but a lower interval is recommended to minimize latency and to scale to more networked entities
				2,     // KCP fastresend parameter. Faster resend for the cost of higher bandwidth. 0 in normal mode, 2 in turbo mode
				false, // KCP congestion window. Enabled in normal mode, disabled in turbo mode. Disable this for high scale games if connections get choked regularly
				4096,  // SendWindowSize    - KCP window size can be modified to support higher loads
				4096,  // ReceiveWindowSize - KCP window size can be modified to support higher loads. This also increases max message size
				10000, // KCP timeout in milliseconds. Note that KCP sends a ping automatically
				Kcp.DEADLINK * 2); // KCP will try to retransmit lost messages up to MaxRetransmit (aka dead_link) before disconnecting. default prematurely disconnects a lot of people (#3022). use 2x
			
			StartUdpNetworkLoop();
		}
		
		private void OnUdpClientConnected() 
		{
			// tunnel authentication
			DebugLogger.Log("[SnipeClient] OnUdpClientConnected - Sending tunnel authentication response");
			
			mUdpClientConnected = true;
			
			var code = "N4EPtDpPdLfpGwLp";
			int pos = 0;
			byte[] data = new byte[code.Length * 2 + 5];
			data.WriteByte(ref pos, OPCODE_AUTHENTICATION_RESPONSE);
			data.WriteString(ref pos, code);
			mUdpClient.Send(new ArraySegment<byte>(data, 0, pos), KcpChannel.Reliable);
		}
		
		private void OnUdpClientDisconnected()
		{
			DebugLogger.Log("[SnipeClient] OnUdpClientDisconnected");
			
			UdpConnectionFailed?.Invoke();
			
			if (mUdpClientConnected)
			{
				Disconnect(true);
			}
			else // not connected yet, try websocket
			{
				ConnectWebSocket();
			}
		}
		
		// private void OnUdpClientError(Exception err)
		// {
			// DebugLogger.Log($"[SnipeClient] OnUdpClientError: {err.Message}");
		// }
		
		private void OnUdpClientDataReceived(ArraySegment<byte> b) //, int channel)
		{
			DebugLogger.Log("[SnipeClient] OnUdpClientDataReceived");
			
			var data = b.Array;
			int pos = b.Offset;
				
			// get opcode
			
			byte opcode = (byte)data.ReadByte(ref pos);
			// idk what that is...
			if (opcode == 200)
				return;
			// DebugLogger.Log($"[SnipeClient] : Received opcode {opcode}");

			// auth request -> auth response -> authenticated
			// handled in OnClientConnected
			if (opcode == OPCODE_AUTHENTICATION_REQUEST)
			{
				return;
			}
			else if (opcode == OPCODE_AUTHENTICATED)
			{
				OnConnected();
			}
			else if (opcode == OPCODE_SNIPE_RESPONSE)
			{
				//var len = data.ReadInt(ref posin);
				// DebugLogger.Log($"[SnipeClient] recv snipe response"); // ({len} bytes) "); // {BitConverter.ToString(b.Array, posin, len)}
				byte[] msg = data.ReadBytes(ref pos);
				ProcessMessage(msg);
			}
		}
		
		private void DoSendRequestUdpClient(byte[] msg)
		{
			int pos = 0;
			// opcode + length (4 bytes) + msg
			byte[] data = new byte[msg.Length + 5];
			data.WriteByte(ref pos, OPCODE_SNIPE_REQUEST);
			data.WriteBytes(ref pos, msg);
			mUdpClient.Send(new ArraySegment<byte>(data, 0, pos), KcpChannel.Reliable);
		}
		
		private CancellationTokenSource mUdpNetworkLoopCancellation;

		private void StartUdpNetworkLoop()
		{
			DebugLogger.Log("[SnipeClient] StartUdpNetworkLoop");
			
			mUdpNetworkLoopCancellation?.Cancel();

			mUdpNetworkLoopCancellation = new CancellationTokenSource();
			#pragma warning disable CS4014
			UdpNetworkLoop(mUdpNetworkLoopCancellation.Token);
			#pragma warning restore CS4014
		}

		private void StopUdpNetworkLoop()
		{
			DebugLogger.Log("[SnipeClient] StopUdpNetworkLoop");
			
			if (mUdpNetworkLoopCancellation != null)
			{
				mUdpNetworkLoopCancellation.Cancel();
				mUdpNetworkLoopCancellation = null;
			}
		}

		private async void UdpNetworkLoop(CancellationToken cancellation)
		{
			DebugLogger.Log("[SnipeClient] UdpNetworkLoop - start");
			
			while (cancellation != null && !cancellation.IsCancellationRequested)
			{
				mUdpClient?.TickIncoming();
				mUdpClient?.TickOutgoing();
				await Task.Yield();
			}
			
			DebugLogger.Log("[SnipeClient] UdpNetworkLoop - finish");
		}
		
		#endregion UdpClient
		
		#region Web Socket

		private WebSocketWrapper mWebSocket = null;
		private object mWebSocketLock = new object();
		
		public bool WebSocketConnected => mWebSocket != null && mWebSocket.Connected;
		
		private void ConnectWebSocket()
		{
			if (mWebSocket != null)  // already connected or trying to connect
				return;

			Disconnect(false); // clean up

			string url = SnipeConfig.GetServerUrl();

			DebugLogger.Log("[SnipeClient] WebSocket Connect to " + url);
			
			mWebSocket = new WebSocketWrapper();
			mWebSocket.OnConnectionOpened += OnWebSocketConnected;
			mWebSocket.OnConnectionClosed += OnWebSocketClosed;
			mWebSocket.ProcessMessage += ProcessMessage;
			
			mConnectionStopwatch = Stopwatch.StartNew();
			
			mWebSocket.Connect(url);
		}

		private void OnWebSocketConnected()
		{
			DebugLogger.Log($"[SnipeClient] OnWebSocketConnected");
			
			mConnectionStopwatch?.Stop();
			Analytics.ConnectionEstablishmentTime = mConnectionStopwatch?.ElapsedMilliseconds ?? 0;
			
			OnConnected();
		}
		
		protected void OnWebSocketClosed()
		{
			DebugLogger.Log("[SnipeClient] OnWebSocketClosed");
			
			if (!mConnected) // failed to establish connection
			{
				SnipeConfig.NextServerUrl();
			}

			Disconnect(true);
		}
		
		private void DoSendRequestWebSocket(byte[] bytes)
		{
			lock (mWebSocketLock)
			{
				mWebSocket.SendRequest(bytes);
			}
			
			if (mServerReactionStopwatch != null)
			{
				mServerReactionStopwatch.Reset();
				mServerReactionStopwatch.Start();
			}
			else
			{
				mServerReactionStopwatch = Stopwatch.StartNew();
			}

			// if (mHeartbeatEnabled)
			// {
				// ResetHeartbeatTimer();
			// }
		}
		
		#endregion // Web Socket
		
		private void OnConnected()
		{
			mConnected = true;
			
			try
			{
				ConnectionOpened?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log("[SnipeClient] ConnectionOpened invokation error: " + e.Message);
			}
		}

		private void RaiseConnectionClosedEvent()
		{
			try
			{
				ConnectionClosed?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log($"[SnipeClient] ConnectionClosed invokation error: {e.Message}\n{e.StackTrace}");
			}
		}

		public void Disconnect()
		{
			Disconnect(true);
		}

		private void Disconnect(bool raise_event)
		{
			mConnected = false;
			mLoggedIn = false;
			ConnectionId = "";
			
			mConnectionStopwatch?.Stop();
			Analytics.PingTime = 0;
			
			StopSendTask();
			StopHeartbeat();
			StopCheckConnection();
			
			StopUdpNetworkLoop();
			
			mUdpClientConnected = false;
			if (mUdpClient != null)
			{
				mUdpClient.Disconnect();
				mUdpClient = null;
			}

			if (mWebSocket != null)
			{
				mWebSocket.OnConnectionOpened -= OnWebSocketConnected;
				mWebSocket.OnConnectionClosed -= OnWebSocketClosed;
				mWebSocket.ProcessMessage -= ProcessMessage;
				mWebSocket.Disconnect();
				mWebSocket = null;
			}

			if (raise_event)
			{
				RaiseConnectionClosedEvent();
			}
		}

		public int SendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return 0;
				
			message["id"] = ++mRequestId;
			
			if (!mLoggedIn)
			{
				var data = message["data"] as SnipeObject ?? new SnipeObject();
				data["ckey"] = SnipeConfig.ClientKey;
				message["data"] = data;
			}
			
			if (mSendMessages == null)
			{
				StartSendTask();
			}
			mSendMessages.Enqueue(message);
			
			return mRequestId;
		}

		public int SendRequest(string message_type, SnipeObject data)
		{
			if (data == null)
			{
				return SendRequest(new SnipeObject()
				{
					["t"] = message_type,
				});
			}
			else
			{
				return SendRequest(new SnipeObject()
				{
					["t"] = message_type,
					["data"] = data
				});
			}
		}
		
		private void DoSendRequest(SnipeObject message)
		{
			if (!Connected || message == null)
				return;
			
			DebugLogger.Log($"[SnipeClient] DoSendRequest - {message.ToJSONString()}");

			byte[] bytes = MessagePackSerializer.Serialize(message);
			
			if (UdpClientConnected)
				DoSendRequestUdpClient(bytes);
			else if (WebSocketConnected)
				DoSendRequestWebSocket(bytes);
		}
		
		protected void ProcessMessage(byte[] raw_data_buffer)
		{
			if (mServerReactionStopwatch != null)
			{
				mServerReactionStopwatch.Stop();
				ServerReaction = mServerReactionStopwatch.Elapsed;
			}
			
			StopCheckConnection();
			
			var message = MessagePackDeserializer.Parse(raw_data_buffer) as SnipeObject;

			if (message != null)
			{
				string message_type = message.SafeGetString("t");
				string error_code =  message.SafeGetString("errorCode");
				int request_id = message.SafeGetValue<int>("id");
				SnipeObject response_data = message.SafeGetValue<SnipeObject>("data");
				
				DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - {request_id} - {message_type} {error_code} {response_data?.ToJSONString()}");

				if (!mLoggedIn)
				{
					if (message_type == SnipeMessageTypes.USER_LOGIN)
					{	
						if (error_code == SnipeErrorCodes.OK || error_code == "alreadyLoggedIn")
						{
							DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - Login Succeeded");
							
							mLoggedIn = true;

							if (response_data != null)
							{
								this.ConnectionId = response_data.SafeGetString("connectionID");
							}
							else
							{
								this.ConnectionId = "";
							}

							try
							{
								LoginSucceeded?.Invoke();
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - LoginSucceeded invokation error: " + e.Message);
							}

							if (mHeartbeatEnabled)
							{
								StartHeartbeat();
							}
						}
						else
						{
							DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - Login Failed");
							
							try
							{
								LoginFailed?.Invoke(error_code);
							}
							catch (Exception e)
							{
								DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - LoginFailed invokation error: " + e.Message);
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
						DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - MessageReceived invokation error: " + e.Message + "\n" + e.StackTrace);
					}
				}
				else
				{
					DebugLogger.Log($"[SnipeClient] [{ConnectionId}] ProcessMessage - no MessageReceived listeners");
				}

				if (mHeartbeatEnabled)
				{
					ResetHeartbeatTimer();
				}
			}
		}

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
			//ResetHeartbeatTimer();

			// await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);
			mHeartbeatTriggerTicks = 0;

			while (!cancellation.IsCancellationRequested && Connected)
			{
				if (DateTime.UtcNow.Ticks >= mHeartbeatTriggerTicks)
				{
					bool pinging = false;
					if (pinging)
					{
						await Task.Yield();
					}
					else
					{
						lock (mWebSocketLock)
						{
							pinging = true;
							
							if (mPingStopwatch == null)
							{
								mPingStopwatch = Stopwatch.StartNew();
							}
							else
							{
								mPingStopwatch.Restart();
							}
							
							mWebSocket.Ping(pong =>
							{
								pinging = false;
								mPingStopwatch?.Stop();
								Analytics.PingTime = pong && mPingStopwatch != null ? mPingStopwatch.ElapsedMilliseconds : 0;
								
								if (pong)
									DebugLogger.Log($"[SnipeClient] [{ConnectionId}] Heartbeat pong {Analytics.PingTime} ms");
								else
									DebugLogger.Log($"[SnipeClient] [{ConnectionId}] Heartbeat pong NOT RECEIVED");
							});
						}
					}
					
					ResetHeartbeatTimer();

					DebugLogger.Log($"[SnipeClient] [{ConnectionId}] Heartbeat ping");
				}
				
				if (cancellation.IsCancellationRequested)
				{
					return;
				}

				await Task.Delay(HEARTBEAT_TASK_DELAY, cancellation);
			}
		}

		private void ResetHeartbeatTimer()
		{
			mHeartbeatTriggerTicks = DateTime.UtcNow.AddSeconds(HEARTBEAT_INTERVAL).Ticks;
		}

		#endregion
		
		#region CheckConnection

		private CancellationTokenSource mCheckConnectionCancellation;
		
		private void StartCheckConnection()
		{
			if (!mLoggedIn)
				return;
			
			// DebugLogger.Log($"[SnipeClient] [{ConnectionId}] StartCheckConnection");

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

				// DebugLogger.Log($"[SnipeClient] [{ConnectionId}] StopCheckConnection");
			}
			
			BadConnection = false;
		}

		private async Task CheckConnectionTask(CancellationToken cancellation)
		{
			BadConnection = false;
			
			try
			{
				await Task.Delay(CHECK_CONNECTION_TIMEOUT, cancellation);
			}
			catch (TaskCanceledException)
			{
				// This is OK. Just terminating the task
				return;
			}

			// if the connection is ok then this task should already be cancelled
			if (cancellation.IsCancellationRequested)
				return;
			
			BadConnection = true;
			DebugLogger.Log($"[SnipeClient] [{ConnectionId}] CheckConnectionTask - Bad connection detected");
			
			bool pinging = false;
			while (Connected)
			{
				if (pinging)
				{
					await Task.Yield();
				}
				else
				{
					lock (mWebSocketLock)
					{
						pinging = true;
						mWebSocket.Ping(pong =>
						{
							pinging = false;
							
							if (pong)
							{
								DebugLogger.Log($"[SnipeClient] [{ConnectionId}] CheckConnectionTask - pong received");
							}
							else
							{
								DebugLogger.Log($"[SnipeClient] [{ConnectionId}] CheckConnectionTask - pong NOT received");
								OnDisconnectDetected();
							}
						});
					}
				}
				
				// if the connection is ok then this task should already be cancelled
				if (cancellation.IsCancellationRequested)
				{
					BadConnection = false;
					return;
				}
			}
			
			// try
			// {
				// await Task.Delay(CHECK_CONNECTION_TIMEOUT * 2, cancellation);
			// }
			// catch (TaskCanceledException)
			// {
				// // This is OK. Just terminating the task
				// BadConnection = false;
				// return;
			// }
			
			// // if the connection is ok then this task should already be cancelled
			// if (cancellation.IsCancellationRequested)
			// {
				// BadConnection = false;
				// return;
			// }
			
			// OnDisconnectDetected();
		}
		
		private void OnDisconnectDetected()
		{
			if (Connected)
			{
				// Disconnect detected
				DebugLogger.Log($"[SnipeClient] [{ConnectionId}] CheckConnectionTask - Disconnect detected");

				OnWebSocketClosed();
			}
		}

		#endregion
		
		#region Send task

		private CancellationTokenSource mSendTaskCancellation;

		private void StartSendTask()
		{
			mSendTaskCancellation?.Cancel();
			
			mSendMessages = new ConcurrentQueue<SnipeObject>();

			mSendTaskCancellation = new CancellationTokenSource();
			_ = SendTask(mHeartbeatCancellation?.Token);
		}

		private void StopSendTask()
		{
			StopCheckConnection();
			
			if (mSendTaskCancellation != null)
			{
				mSendTaskCancellation.Cancel();
				mSendTaskCancellation = null;
			}
			
			mSendMessages = null;
		}

		private async Task SendTask(CancellationToken? cancellation)
		{
			while (cancellation?.IsCancellationRequested != true && Connected)
			{
				if (mSendMessages != null && !mSendMessages.IsEmpty && mSendMessages.TryDequeue(out var message))
				{
					DoSendRequest(message);
					
					if (mSendMessages.IsEmpty && !message.SafeGetString("t").StartsWith("payment/"))
					{
						StartCheckConnection();
					}
				}

				await Task.Yield();
			}
			
			mSendMessages = null;
		}

		#endregion
	}
}