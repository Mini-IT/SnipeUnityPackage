﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	public sealed class SnipeCommunicator : IDisposable
	{
		private readonly int INSTANCE_ID = new System.Random().Next();
		
		private const int RETRY_INIT_CLIENT_DELAY = 500; // ms
		private const int RETRY_INIT_CLIENT_MIN_DELAY = 500; // ms
		private const int RETRY_INIT_CLIENT_MAX_DELAY = 10000; // ms
		private const int RETRY_INIT_CLIENT_RANDOM_DELAY = 500; // ms
		
		public delegate void MessageReceivedHandler(string message_type, string error_code, SnipeObject data, int request_id);
		public delegate void ConnectionSucceededHandler();
		public delegate void ConnectionFailedHandler(bool will_restore = false);
		public delegate void LoginSucceededHandler();
		public delegate void PreDestroyHandler();

		public event ConnectionSucceededHandler ConnectionSucceeded;
		public event ConnectionFailedHandler ConnectionFailed;
		public event LoginSucceededHandler LoginSucceeded;
		public event MessageReceivedHandler MessageReceived;
		public event PreDestroyHandler PreDestroy;
		
		public SnipeAuthCommunicator Auth { get; private set; }

		public string UserName { get; private set; }
		public string ConnectionId { get { return Client?.ConnectionId; } }
		public TimeSpan ServerReaction { get { return Client?.ServerReaction ?? new TimeSpan(0); } }
		public TimeSpan CurrentRequestElapsed { get { return Client?.CurrentRequestElapsed ?? new TimeSpan(0); } }

		internal SnipeClient Client { get; private set; }

		public int RestoreConnectionAttempts = 10;
		private int _restoreConnectionAttempt;
		
		public bool AllowRequestsToWaitForLogin = true;
		
		private bool _autoLogin = true;

		private List<SnipeCommunicatorRequest> _requests;
		public List<SnipeCommunicatorRequest> Requests
		{
			get
			{
				_requests ??= new List<SnipeCommunicatorRequest>();
				return _requests;
			}
		}

		public readonly List<SnipeRequestDescriptor> MergeableRequestTypes = new List<SnipeRequestDescriptor>();

		public bool Connected
		{
			get
			{
				return Client != null && Client.Connected;
			}
		}

		public bool LoggedIn
		{
			get { return Client != null && Client.LoggedIn; }
		}
		
		private bool? _roomJoined = null;
		public bool? RoomJoined
		{
			get { return (Client != null && Client.LoggedIn) ? _roomJoined : null; }
		}

		public bool BatchMode
		{
			get => Client?.BatchMode ?? false;
			set
			{
				if (Client != null)
					Client.BatchMode = value;
			}
		}

		private bool _disconnecting = false;
		
		private TaskScheduler _mainThreadScheduler;
		private CancellationTokenSource _delayedInitCancellation;
		
		private static SnipeCommunicator s_instance;
		public static SnipeCommunicator Instance
		{
			get
			{
				if (s_instance == null)
				{
					s_instance = new SnipeCommunicator();
				}
				return s_instance;
			}
		}
		
		public static bool InstanceInitialized
		{
			get => s_instance != null;
		}
		
		public static void DestroyInstance()
		{
			if (s_instance != null)
			{
				var temp = s_instance;
				s_instance = null;
				temp.Dispose();
			}
		}
		
		private SnipeCommunicator()
		{
			if (s_instance != null && s_instance != this)
			{
				DebugLogger.LogError("[SnipeCommunicator] There is another instance");
				return;
			}

			s_instance = this;
			this.Auth = new SnipeAuthCommunicator();

			DebugLogger.Log($"[SnipeCommunicator] PACKAGE VERSION: {PackageInfo.VERSION}");

			UnityTerminator.Run();
		}
		
		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public void StartCommunicator(bool autologin = true)
		{
			_mainThreadScheduler = (SynchronizationContext.Current != null) ?
				TaskScheduler.FromCurrentSynchronizationContext() :
				TaskScheduler.Current;
			
			_autoLogin = autologin;
			InitClient();
		}
		
		private void InitClient()
		{
			if (_delayedInitCancellation != null)
			{
				_delayedInitCancellation.Cancel();
				_delayedInitCancellation = null;
			}

			if (LoggedIn)
			{
				DebugLogger.LogWarning($"[SnipeCommunicator] ({INSTANCE_ID}) InitClient - already logged in");
				return;
			}

			if (Client == null)
			{
				Client = new SnipeClient();
				Client.ConnectionOpened += OnClientConnectionOpened;
				Client.ConnectionClosed += OnClientConnectionClosed;
				Client.UdpConnectionFailed += OnClientUdpConnectionFailed;
				Client.MessageReceived += OnMessageReceived;
			}

			lock (Client)
			{
				if (!Client.Connected)
				{
					_disconnecting = false;
					Client.Connect();
					
					RunInMainThread(() =>
					{
						AnalyticsTrackStartConnection();
					});
				}
			}
		}

		public void Authorize()
		{
			if (!Connected || LoggedIn)
				return;
			
			RunInMainThread(() =>
			{
				DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Authorize");
				Auth.Authorize(OnAuthResult);
			});
		}

		private void OnAuthResult(string error_code, int user_id)
		{
			if (user_id != 0)  // authorization succeeded
				return;

			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnAuthResult - authorization failed");

			if (ConnectionFailed != null)
			{
				RunInMainThread(() =>
				{
					RaiseEvent(ConnectionFailed, false);
				});
			}
		}
		
		public void Disconnect()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Disconnect");

			_roomJoined = null;
			_disconnecting = true;
			UserName = "";

			if (_delayedInitCancellation != null)
			{
				_delayedInitCancellation.Cancel();
				_delayedInitCancellation = null;
			}

			if (Client != null && Client.Connected)
			{
				Client.Disconnect();
			}
		}

		private void OnClientConnectionOpened()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Client connection opened");

			_restoreConnectionAttempt = 0;
			_disconnecting = false;
			
			if (_autoLogin)
			{
				Authorize();
			}

			RunInMainThread(() =>
			{
				AnalyticsTrackConnectionSucceeded();
				RaiseEvent(ConnectionSucceeded);
			});
		}
		
		private void OnClientConnectionClosed()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{Client?.ConnectionId}] Client connection closed");
			
			_roomJoined = null;

			RunInMainThread(() =>
			{
				AnalyticsTrackConnectionFailed();
				OnConnectionFailed();
			});
		}
		
		private void OnClientUdpConnectionFailed()
		{
			RunInMainThread(() =>
			{
				AnalyticsTrackUdpConnectionFailed();
			});
		}
		
		// Main thread
		private void OnConnectionFailed()
		{	
			//ClearMainThreadActionsQueue();

			if (_restoreConnectionAttempt < RestoreConnectionAttempts && !_disconnecting)
			{
				RaiseEvent(ConnectionFailed, true);
				
				_restoreConnectionAttempt++;
				DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Attempt to restore connection {_restoreConnectionAttempt}");

				if (_delayedInitCancellation == null)
				{
					_delayedInitCancellation = new CancellationTokenSource();
					DelayedInitClient(_delayedInitCancellation.Token);
				}
			}
			else if (ConnectionFailed != null)
			{
				RaiseEvent(ConnectionFailed, false);
				DisposeRequests();
			}
		}

		private void OnMessageReceived(string message_type, string error_code, SnipeObject data, int request_id)
		{
			// DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{Client?.ConnectionId}] OnMessageReceived {request_id} {message_type} {error_code} " + (data != null ? data.ToJSONString() : "null"));

			if (message_type == SnipeMessageTypes.USER_LOGIN)
			{
				switch (error_code)
				{
					case SnipeErrorCodes.OK:
					case SnipeErrorCodes.ALREADY_LOGGED_IN:
						UserName = data.SafeGetString("name");
						_autoLogin = true;

						if (LoginSucceeded != null)
						{
							RunInMainThread(() =>
							{
								RaiseEvent(LoginSucceeded);
							});
						}
						break;

					case SnipeErrorCodes.WRONG_TOKEN:
					case SnipeErrorCodes.USER_NOT_FOUND:
						Authorize();
						break;

					case SnipeErrorCodes.USER_ONLINE:
					case SnipeErrorCodes.LOGOUT_IN_PROGRESS:
						// Do nothing!
						// AuthProvider will retry to login automatically
						break;

					case SnipeErrorCodes.GAME_SERVERS_OFFLINE:
						OnConnectionFailed();
						break;

					default: // unexpected error code
						DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) {message_type} - Unexpected error code: {error_code}");
						OnConnectionFailed();
						break;
				}
				
			}
			else if (message_type == SnipeMessageTypes.ROOM_JOIN)
			{
				if (error_code == SnipeErrorCodes.OK || error_code == SnipeErrorCodes.ALREADY_IN_ROOM)
				{
					_roomJoined = true;
				}
				else
				{
					_roomJoined = false;
					DisposeRoomRequests();
				}
			}
			else if (message_type == SnipeMessageTypes.ROOM_DEAD)
			{
				_roomJoined = false;
				DisposeRoomRequests();
			}
			
			if (MessageReceived != null)
			{
				RunInMainThread(() =>
				{
					RaiseEvent(MessageReceived, message_type, error_code, data, request_id);
				});
			}
			
			if (error_code != SnipeErrorCodes.OK)
			{
				RunInMainThread(() =>
				{
					Analytics.TrackErrorCodeNotOk(message_type, error_code, data);
				});
			}
		}
		
		#region Main Thread
		
		private void RunInMainThread(Action action)
		{
			new Task(action).RunSynchronously(_mainThreadScheduler);
		}

		#endregion // Main Thread
		
		#region Safe events raising
		
		// https://www.codeproject.com/Articles/36760/C-events-fundamentals-and-exception-handling-in-mu#exceptions
		
		private void RaiseEvent(Delegate event_delegate, params object[] args)
		{
			if (event_delegate != null)
			{
				foreach (Delegate handler in event_delegate.GetInvocationList())
				{
					if (handler == null)
						continue;
					
					try
					{
						handler.DynamicInvoke(args);
					}
					catch (Exception e)
					{
						string message = (e is System.Reflection.TargetInvocationException tie) ?
							$"{tie.InnerException?.Message}\n{tie.InnerException?.StackTrace}" :
							$"{e.Message}\n{e.StackTrace}";
						DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) RaiseEvent - Error in the handler {handler?.Method?.Name}: {message}");
					}
				}
			}
		}
		
		#endregion

		private async void DelayedInitClient(CancellationToken cancellation)
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) WaitAndInitClient - start delay");
			Random random = new Random();
			int delay = RETRY_INIT_CLIENT_DELAY * _restoreConnectionAttempt + random.Next(RETRY_INIT_CLIENT_RANDOM_DELAY);
			if (delay < RETRY_INIT_CLIENT_MIN_DELAY)
				delay = RETRY_INIT_CLIENT_MIN_DELAY;
			else if (delay > RETRY_INIT_CLIENT_MAX_DELAY)
				delay = RETRY_INIT_CLIENT_MAX_DELAY;

			try
			{
				await Task.Delay(delay, cancellation);
			}
			catch (Exception)
			{
				return;
			}
			if (cancellation.IsCancellationRequested)
			{
				return;
			}

			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) WaitAndInitClient - delay finished");
			InitClient();
		}
		
		public void Request(string message_type, SnipeObject parameters = null)
		{
			CreateRequest(message_type, parameters).Request();
		}
		
		public SnipeCommunicatorRequest CreateRequest(string message_type = null, SnipeObject parameters = null)
		{
			var request = new SnipeCommunicatorRequest(this, message_type);
			request.Data = parameters;
			return request;
		}
		
		public void DisposeRoomRequests()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeRoomRequests");
			
			List<SnipeCommunicatorRequest> room_requests = null;
			foreach (var request in Requests)
			{
				if (request != null && request.MessageType.StartsWith(SnipeMessageTypes.PREFIX_ROOM))
				{
					if (room_requests == null)
						room_requests = new List<SnipeCommunicatorRequest>();
					
					room_requests.Add(request);
				}
			}
			if (room_requests != null)
			{
				foreach (var request in room_requests)
				{
					request?.Dispose();
				}
			}
		}

		#region ActionRun Requests
		
		public void RequestActionRun(string action_id, SnipeObject parameters = null)
		{
			if (Client == null || !Client.LoggedIn)
				return;
			
			CreateActionRunRequest(action_id, parameters).Request();
		}
		
		public SnipeCommunicatorRequest CreateActionRunRequest(string action_id, SnipeObject parameters = null)
		{
			if (parameters == null)
				parameters = new SnipeObject() { ["actionID"] = action_id };
			else
				parameters["actionID"] = action_id;
			
			return CreateRequest(SnipeMessageTypes.ACTION_RUN, parameters);
		}
		
		#endregion // ActionRun Requests
		
		public void Dispose()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Dispose");

			if (Client != null)
			{
				Client.ConnectionOpened -= OnClientConnectionOpened;
				Client.ConnectionClosed -= OnClientConnectionClosed;
				Client.UdpConnectionFailed -= OnClientUdpConnectionFailed;
				Client.MessageReceived -= OnMessageReceived;
			}

			Disconnect();
			DisposeRequests();

			try
			{
				RaiseEvent(PreDestroy);
			}
			catch (Exception) { }

			Client = null;
		}
		
		public void DisposeRequests()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeRequests");
			
			if (_requests != null)
			{
				var temp_requests = _requests;
				_requests = null;
				foreach (var request in temp_requests)
				{
					request?.Dispose();
				}
			}
		}

		#region Analytics

		private void AnalyticsTrackStartConnection()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_START_CONNECTION);
		}
		
		private void AnalyticsTrackConnectionSucceeded()
		{
			var data = Client.UdpClientConnected ? new SnipeObject()
			{
				["connection_type"] = "udp",
				["connection_time"] = Analytics.UdpConnectionTime.TotalMilliseconds,
				["connection_url"] = Analytics.ConnectionUrl,
				
				["udp dns resolve"] = Analytics.UdpDnsResolveTime.TotalMilliseconds,
				["udp socket connect"] = Analytics.UdpSocketConnectTime.TotalMilliseconds,
				["udp handshake request"] = Analytics.UdpSendHandshakeTime.TotalMilliseconds,
				["udp misc"] = Analytics.UdpConnectionTime.TotalMilliseconds <= 0 ? 0:
					Analytics.UdpConnectionTime.TotalMilliseconds -
					Analytics.UdpDnsResolveTime.TotalMilliseconds -
					Analytics.UdpSocketConnectTime.TotalMilliseconds -
					Analytics.UdpSendHandshakeTime.TotalMilliseconds,
			} :
			new SnipeObject()
			{
				["connection_type"] = "websocket",
				["connection_time"] = Analytics.ConnectionEstablishmentTime.TotalMilliseconds,
				["connection_url"] = Analytics.ConnectionUrl,
				
				["ws tcp client connection"] = Analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds,
				["ws ssl auth"] = Analytics.WebSocketSslAuthenticateTime.TotalMilliseconds,
				["ws upgrade request"] = Analytics.WebSocketHandshakeTime.TotalMilliseconds,
				["ws misc"] = Analytics.ConnectionEstablishmentTime.TotalMilliseconds <= 0 ? 0:
					Analytics.ConnectionEstablishmentTime.TotalMilliseconds - 
					Analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds -
					Analytics.WebSocketSslAuthenticateTime.TotalMilliseconds -
					Analytics.WebSocketHandshakeTime.TotalMilliseconds,
			};
			
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_CONNECTED, data);
		}
		
		private void AnalyticsTrackConnectionFailed()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_DISCONNECTED, new SnipeObject()
			{
				//["communicator"] = this.name,
				["connection_id"] = Client?.ConnectionId,
				//["disconnect_reason"] = Client?.DisconnectReason,
				//["check_connection_message"] = Client?.CheckConnectionMessageType,
				["connection_url"] = Analytics.ConnectionUrl,
				
				["ws tcp client connection"] = Analytics.WebSocketTcpClientConnectionTime.TotalMilliseconds,
				["ws ssl auth"] = Analytics.WebSocketSslAuthenticateTime.TotalMilliseconds,
				["ws upgrade request"] = Analytics.WebSocketHandshakeTime.TotalMilliseconds,
				["ws disconnect reason"] = Analytics.WebSocketDisconnectReason,
				
				["udp connection_time"] = Analytics.UdpConnectionTime.TotalMilliseconds,
				["udp dns resolve"] = Analytics.UdpDnsResolveTime.TotalMilliseconds,
				["udp socket connect"] = Analytics.UdpSocketConnectTime.TotalMilliseconds,
				["udp handshake request"] = Analytics.UdpSendHandshakeTime.TotalMilliseconds,
				["udp exception"] = Analytics.UdpException?.ToString(),
				["udp dns resolved"] = Analytics.UdpDnsResolved,
			});
		}
		
		private void AnalyticsTrackUdpConnectionFailed()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_DISCONNECTED + " UDP", new SnipeObject()
			{
				["connection_type"] = "udp",
				["connection_time"] = Analytics.UdpConnectionTime.TotalMilliseconds,
				["connection_url"] = Analytics.ConnectionUrl,
				
				["udp dns resolve"] = Analytics.UdpDnsResolveTime.TotalMilliseconds,
				["udp socket connect"] = Analytics.UdpSocketConnectTime.TotalMilliseconds,
				["udp handshake request"] = Analytics.UdpSendHandshakeTime.TotalMilliseconds,
				["udp misc"] = Analytics.UdpConnectionTime.TotalMilliseconds <= 0 ? 0 :
					Analytics.UdpConnectionTime.TotalMilliseconds -
					Analytics.UdpDnsResolveTime.TotalMilliseconds -
					Analytics.UdpSocketConnectTime.TotalMilliseconds -
					Analytics.UdpSendHandshakeTime.TotalMilliseconds,
				["udp exception"] = Analytics.UdpException?.ToString(),
				["udp dns resolved"] = Analytics.UdpDnsResolved,
			});
		}
		
		#endregion Analytics
	}
}
