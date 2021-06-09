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
		private int mRestoreConnectionAttempt;
		
		public bool AllowRequestsToWaitForLogin = true;
		public bool KeepOfflineRequests = false; // works only if AllowRequestsToWaitForLogin == false
		
		private List<SnipeCommunicatorRequest> mRequests;
		public List<SnipeCommunicatorRequest> Requests
		{
			get
			{
				if (mRequests == null)
					mRequests = new List<SnipeCommunicatorRequest>();
				return mRequests;
			}
		}

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
		
		private bool? mRoomJoined = null;
		public bool? RoomJoined
		{
			get { return (Client != null && Client.LoggedIn) ? mRoomJoined : null; }
		}

		private bool mDisconnecting = false;
		
		private /*readonly*/ ConcurrentQueue<Action> mMainThreadActions = new ConcurrentQueue<Action>();
		private Coroutine MainThreadLoopCoroutine;
		
		private static SnipeCommunicator mInstance;
		public static SnipeCommunicator Instance
		{
			get
			{
				if (mInstance == null)
				{
					var game_object = new GameObject("[SnipeCommunicator]");
					//game_object.hideFlags = HideFlags.HideAndDontSave;
					mInstance = game_object.AddComponent<SnipeCommunicator>();
					mInstance.Auth = new SnipeAuthCommunicator();
					DontDestroyOnLoad(game_object);
					
					DebugLogger.InitInstance();
				}
				return mInstance;
			}
		}
		
		public static bool InstanceInitialized
		{
			get => mInstance != null;
		}
		
		public static void DestroyInstance()
		{
			if (mInstance != null)
			{
				if (mInstance.gameObject != null)
					GameObject.DestroyImmediate(mInstance.gameObject);
				mInstance = null;
			}
		}
		
		private void Awake()
		{
			if (mInstance != null && mInstance != this)
			{
				GameObject.DestroyImmediate(this.gameObject);
				return;
			}
			DontDestroyOnLoad(this.gameObject);
		}
		
		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public void StartCommunicator()
		{
			if (MainThreadLoopCoroutine == null)
			{
				MainThreadLoopCoroutine = StartCoroutine(MainThreadLoop());
			}
			
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
				Client.MessageReceived += OnMessageReceived;
			}

			lock (Client)
			{
				if (!Client.Connected)
				{
					mDisconnecting = false;
					Client.Connect();
				}
			}
			
			InvokeInMainThread(() =>
			{
				AnalyticsTrackStartConnection();
			});
		}

		private void Authorize()
		{
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
					ConnectionFailed?.Invoke(false);
				});
			}
		}
		
		public void Disconnect()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) {this.name} Disconnect");

			mRoomJoined = null;
			mDisconnecting = true;
			UserName = "";

			if (Client != null)
				Client.Disconnect();
		}

		private void OnDestroy()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnDestroy");
			
			mRoomJoined = null;
			
			if (MainThreadLoopCoroutine != null)
			{
				StopCoroutine(MainThreadLoopCoroutine);
				MainThreadLoopCoroutine = null;
			}
			ClearMainThreadActionsQueue();
			
			DisposeRequests();

			try
			{
				PreDestroy?.Invoke();
			}
			catch (Exception) { }

			if (Client != null)
			{
				Client.ConnectionOpened -= OnClientConnectionOpened;
				Client.ConnectionClosed -= OnClientConnectionClosed;
				Client.MessageReceived -= OnMessageReceived;
				Client.Disconnect();
				Client = null;
			}
		}
		
		private void OnClientConnectionOpened()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Client connection opened");

			mRestoreConnectionAttempt = 0;
			mDisconnecting = false;
			
			Authorize();

			InvokeInMainThread(() =>
			{
				AnalyticsTrackConnectionSucceeded();
				ConnectionSucceeded?.Invoke();
			});
		}
		
		private void OnClientConnectionClosed()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{Client?.ConnectionId}] Client connection closed");
			
			mRoomJoined = null;

			InvokeInMainThread(() =>
			{
				AnalyticsTrackConnectionFailed();
				OnConnectionFailed();
			});
		}
		
		// Main thread
		private void OnConnectionFailed()
		{	
			//ClearMainThreadActionsQueue();

			if (mRestoreConnectionAttempt < RestoreConnectionAttempts && !mDisconnecting)
			{
				ConnectionFailed?.Invoke(true);
				
				mRestoreConnectionAttempt++;
				DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Attempt to restore connection {mRestoreConnectionAttempt}");
				
				StartCoroutine(WaitAndInitClient());
			}
			else if (ConnectionFailed != null)
			{
				ConnectionFailed?.Invoke(false);
				DisposeRequests();
			}
		}

		private void OnMessageReceived(string message_type, string error_code, SnipeObject data, int request_id)
		{
			// DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{Client?.ConnectionId}] OnMessageReceived {request_id} {message_type} {error_code} " + (data != null ? data.ToJSONString() : "null"));

			if (message_type == SnipeMessageTypes.USER_LOGIN)
			{
				if (error_code == SnipeErrorCodes.OK)
				{
					UserName = data.SafeGetString("name");

					if (LoginSucceeded != null)
					{
						InvokeInMainThread(() =>
						{
							LoginSucceeded?.Invoke();
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
					mRoomJoined = true;
				}
				else
				{
					mRoomJoined = false;
					DisposeRoomRequests();
				}
			}
			else if (message_type == SnipeMessageTypes.ROOM_DEAD)
			{
				mRoomJoined = false;
				DisposeRoomRequests();
			}
			
			if (MessageReceived != null)
			{
				InvokeInMainThread(() =>
				{
					MessageReceived?.Invoke(message_type, error_code, data, request_id);
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
			mMainThreadActions.Enqueue(action);
		}

		private void ClearMainThreadActionsQueue()
		{
			// mMainThreadActions.Clear(); // Requires .NET 5.0
			mMainThreadActions = new ConcurrentQueue<Action>();
		}

		private IEnumerator MainThreadLoop()
		{
			while (true)
			{
				if (mMainThreadActions != null && !mMainThreadActions.IsEmpty)
				{
					// mMainThreadActions.Dequeue()?.Invoke(); // // Requires .NET 5.0
					if (mMainThreadActions.TryDequeue(out var action))
					{
						action?.Invoke();
					}
				}
				
				yield return null;
			}
		}

		#endregion // Main Thread

		private IEnumerator WaitAndInitClient()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) WaitAndInitClient - start delay");
			float delay = RETRY_INIT_CLIENT_DELAY * mRestoreConnectionAttempt + UnityEngine.Random.value * RETRY_INIT_CLIENT_RANDOM_DELAY;
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
		
		public void ResendOfflineRequests()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) ResendOfflineRequests - begin");
			
			foreach (var request in Requests)
			{
				if (request != null && !request.Active)
				{
					request.ResendInactive();
				}
			}
			
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) ResendOfflineRequests - done");
		}
		
		public void DisposeOfflineRequests()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeOfflineRequests");
			
			List<SnipeCommunicatorRequest> inactive_requests = null;
			foreach (var request in Requests)
			{
				if (request != null && !request.Active)
				{
					if (inactive_requests == null)
						inactive_requests = new List<SnipeCommunicatorRequest>();
					
					inactive_requests.Add(request);
				}
			}
			if (inactive_requests != null)
			{
				foreach (var request in inactive_requests)
				{
					request?.Dispose();
				}
			}
		}
		
		public void DisposeRoomRequests()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeRoomRequests");
			
			List<SnipeCommunicatorRequest> room_requests = null;
			foreach (var request in Requests)
			{
				if (request != null && request.WaitingForRoomJoined)
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
		
		private void DisposeRequests()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeRequests");
			
			if (mRequests != null)
			{
				var temp_requests = mRequests;
				mRequests = null;
				foreach (var request in temp_requests)
				{
					request?.Dispose();
				}
				//temp_requests.Clear();
			}
		}
		
		#region Analytics
		
		private void AnalyticsTrackStartConnection()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_START_CONNECTION);
		}
		
		private void AnalyticsTrackConnectionSucceeded()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_CONNECTED, new SnipeObject()
			{
				["connection_type"] = "websocket",
			});
		}
		
		private void AnalyticsTrackConnectionFailed()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_DISCONNECTED, new SnipeObject()
			{
				//["communicator"] = this.name,
				["connection_id"] = Client?.ConnectionId,
				//["disconnect_reason"] = Client?.DisconnectReason,
				//["check_connection_message"] = Client?.CheckConnectionMessageType,
			});
		}
		
		#endregion Analytics
	}
}