using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;

namespace MiniIT.Snipe
{
	public sealed class SnipeCommunicator : MonoBehaviour, IDisposable
	{
		private readonly int INSTANCE_ID = new System.Random().Next();
		
		private const float RETRY_INIT_CLIENT_DELAY = 0.75f; // seconds
		private const float RETRY_INIT_CLIENT_MIN_DELAY = 1.0f; // seconds
		private const float RETRY_INIT_CLIENT_MAX_DELAY = 60.0f; // seconds
		private const float RETRY_INIT_CLIENT_RANDOM_DELAY = 0.5f; // seconds
		
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
		
		private /*readonly*/ ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
		private Coroutine MainThreadLoopCoroutine;
		
		private static SnipeCommunicator _instance;
		public static SnipeCommunicator Instance
		{
			get
			{
				if (_instance == null)
				{
					var game_object = new GameObject("[SnipeCommunicator]");
					game_object.hideFlags = HideFlags.DontSave; // HideFlags.HideAndDontSave;
					_instance = game_object.AddComponent<SnipeCommunicator>();
					_instance.Auth = new SnipeAuthCommunicator();
					DontDestroyOnLoad(game_object);
				}
				return _instance;
			}
		}
		
		public static bool InstanceInitialized
		{
			get => _instance != null;
		}
		
		public static void DestroyInstance()
		{
			if (_instance != null)
			{
				_instance.Dispose();
				_instance = null;
			}
		}
		
		private void Awake()
		{
			if (_instance != null && _instance != this)
			{
				GameObject.DestroyImmediate(this.gameObject);
				return;
			}
			DontDestroyOnLoad(this.gameObject);
		}
		
		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public void StartCommunicator(bool autologin = true)
		{
			if (MainThreadLoopCoroutine == null)
			{
				MainThreadLoopCoroutine = StartCoroutine(MainThreadLoop());
			}
			
			// If both connection types failed last session (value == 2), then try both again
			if (PlayerPrefs.GetInt(SnipePrefs.SKIP_UDP, 0) > 1)
			{
				PlayerPrefs.DeleteKey(SnipePrefs.SKIP_UDP);
			}
			
			_autoLogin = autologin;
			InitClient();
		}
		
		private void InitClient()
		{
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
					Client.Connect(PlayerPrefs.GetInt(SnipePrefs.SKIP_UDP, 0) != 1);
					
					InvokeInMainThread(() =>
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
			
			InvokeInMainThread(() =>
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
				InvokeInMainThread(() =>
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

			if (Client != null)
				Client.Disconnect();
		}

		private void OnDestroy()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnDestroy");
			
			_roomJoined = null;
			
			if (MainThreadLoopCoroutine != null)
			{
				StopCoroutine(MainThreadLoopCoroutine);
				MainThreadLoopCoroutine = null;
			}
			ClearMainThreadActionsQueue();
			
			DisposeRequests();

			try
			{
				RaiseEvent(PreDestroy);
			}
			catch (Exception) { }

			if (Client != null)
			{
				Client.ConnectionOpened -= OnClientConnectionOpened;
				Client.ConnectionClosed -= OnClientConnectionClosed;
				Client.UdpConnectionFailed -= OnClientUdpConnectionFailed;
				Client.MessageReceived -= OnMessageReceived;
				Client.Disconnect();
				Client = null;
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

			InvokeInMainThread(() =>
			{
				AnalyticsTrackConnectionSucceeded();
				RaiseEvent(ConnectionSucceeded);
				
				if (Client.WebSocketConnected)
				{
					// if the value == 2 then both UDP and websocket connections failed.
					// We'll save the flag only if the first attempt to connect to UDP failed and websocket succeeded.
					// Otherwise we should try both connection types next time
					if (PlayerPrefs.GetInt(SnipePrefs.SKIP_UDP, 0) == 0)
					{
						PlayerPrefs.SetInt(SnipePrefs.SKIP_UDP, 1);
					}
				}
				// else // successfully connected via UDP
				// {
				//		// if SKIP_UDP is not set (0) then no action needed.
				//		// if SKIP_UDP is set to 2, then connection troubles were observed.
				//		// Keep this value until next session to force try both connection types.
				//		// In this case SKIP_UDP will be deleted when StartCommunicator is invoked.
				//		// So no action needed in any case.
				// }
			});
		}
		
		private void OnClientConnectionClosed()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{Client?.ConnectionId}] Client connection closed");
			
			_roomJoined = null;

			InvokeInMainThread(() =>
			{
				AnalyticsTrackConnectionFailed();
				OnConnectionFailed();
			});
		}
		
		private void OnClientUdpConnectionFailed()
		{
			InvokeInMainThread(() =>
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
				
				StartCoroutine(WaitAndInitClient());
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
				if (error_code == SnipeErrorCodes.OK || error_code == SnipeErrorCodes.ALREADY_LOGGED_IN)
				{
					UserName = data.SafeGetString("name");
					_autoLogin = true;

					if (LoginSucceeded != null)
					{
						InvokeInMainThread(() =>
						{
							RaiseEvent(LoginSucceeded);
						});
					}
				}
				else if (error_code == SnipeErrorCodes.WRONG_TOKEN || error_code == SnipeErrorCodes.USER_NOT_FOUND)
				{
					Authorize();
				}
				else if (error_code == SnipeErrorCodes.GAME_SERVERS_OFFLINE)
				{
					OnConnectionFailed();
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
				InvokeInMainThread(() =>
				{
					RaiseEvent(MessageReceived, message_type, error_code, data, request_id);
				});
			}
			
			if (error_code != SnipeErrorCodes.OK)
			{
				InvokeInMainThread(() =>
				{
					Analytics.TrackErrorCodeNotOk(message_type, error_code, data);
				});
			}
		}
		
		#region Main Thread
		
		private void InvokeInMainThread(Action action)
		{
			_mainThreadActions.Enqueue(action);
		}

		private void ClearMainThreadActionsQueue()
		{
			// mMainThreadActions.Clear(); // Requires .NET 5.0
			_mainThreadActions = new ConcurrentQueue<Action>();
		}

		private IEnumerator MainThreadLoop()
		{
			while (true)
			{
				if (_mainThreadActions != null && !_mainThreadActions.IsEmpty)
				{
					// mMainThreadActions.Dequeue()?.Invoke(); // // Requires .NET 5.0
					if (_mainThreadActions.TryDequeue(out var action))
					{
						action?.Invoke();
					}
				}
				
				yield return null;
			}
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

		private IEnumerator WaitAndInitClient()
		{
			// Both connection types failed.
			// Don't force websocket - try both again next time
			PlayerPrefs.SetInt(SnipePrefs.SKIP_UDP, 2);
			
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) WaitAndInitClient - start delay");
			float delay = RETRY_INIT_CLIENT_DELAY * _restoreConnectionAttempt + UnityEngine.Random.value * RETRY_INIT_CLIENT_RANDOM_DELAY;
			if (delay < RETRY_INIT_CLIENT_MIN_DELAY)
				delay = RETRY_INIT_CLIENT_MIN_DELAY;
			yield return new WaitForSecondsRealtime(Mathf.Min(delay, RETRY_INIT_CLIENT_MAX_DELAY));
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
			
			StopAllCoroutines();
			MainThreadLoopCoroutine = null;
			
			Disconnect();
			DisposeRequests();

			if (this.gameObject != null)
			{
				GameObject.DestroyImmediate(this.gameObject);
			}
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

		private void OnApplicationQuit()
		{
			DestroyInstance();
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
				["connection_time"] = Client?.UdpConnectionTime,
				
				["udp dns resolve"] = Client?.UdpDnsResolveTime,
				["udp socket connect"] = Client?.UdpSocketConnectTime,
				["udp handshake request"] = Client?.UdpSendHandshakeTime,
				["udp misc"] = Client.UdpConnectionTime - 
					Client.UdpDnsResolveTime -
					Client.UdpSocketConnectTime -
					Client.UdpSendHandshakeTime,
			} :
			new SnipeObject()
			{
				["connection_type"] = "websocket",
				["connection_time"] = Analytics.ConnectionEstablishmentTime,
				
				["ws tcp client connection"] = Analytics.WebSocketTcpClientConnectionTime,
				["ws ssl auth"] = Analytics.WebSocketSslAuthenticateTime,
				["ws upgrade request"] = Analytics.WebSocketHandshakeTime,
				["ws misc"] = Analytics.ConnectionEstablishmentTime - 
					Analytics.WebSocketTcpClientConnectionTime -
					Analytics.WebSocketSslAuthenticateTime -
					Analytics.WebSocketHandshakeTime,
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
				
				["ws tcp client connection"] = Analytics.WebSocketTcpClientConnectionTime,
				["ws ssl auth"] = Analytics.WebSocketSslAuthenticateTime,
				["ws upgrade request"] = Analytics.WebSocketHandshakeTime,
				
				["udp connection_time"] = Client?.UdpConnectionTime,
				["udp dns resolve"] = Client?.UdpDnsResolveTime,
				["udp socket connect"] = Client?.UdpSocketConnectTime,
				["udp handshake request"] = Client?.UdpSendHandshakeTime,
			});
		}
		
		private void AnalyticsTrackUdpConnectionFailed()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_DISCONNECTED + " UDP", new SnipeObject()
			{
				["connection_type"] = "udp",
				["connection_time"] = Client?.UdpConnectionTime,
				
				["udp dns resolve"] = Client?.UdpDnsResolveTime,
				["udp socket connect"] = Client?.UdpSocketConnectTime,
				["udp handshake request"] = Client?.UdpSendHandshakeTime,
				["udp misc"] = Client != null ? Client.UdpConnectionTime - 
					Client.UdpDnsResolveTime -
					Client.UdpSocketConnectTime -
					Client.UdpSendHandshakeTime : 0,
			});
		}
		
		#endregion Analytics
	}
}