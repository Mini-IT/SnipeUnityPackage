using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiniIT.Snipe
{
	public sealed class SnipeCommunicator : MonoBehaviour, IDisposable
	{
		private readonly int INSTANCE_ID = new System.Random().Next();
		
		private const float RETRY_INIT_CLIENT_DELAY = 0.75f; // seconds
		private const float RETRY_INIT_CLIENT_MAX_DELAY = 60.0f; // seconds
		private const float RETRY_INIT_CLIENT_RANDOM_DELAY = 0.5f; // seconds
		
		public delegate void MessageReceivedHandler(string message_type, string error_code, ExpandoObject data, int request_id);
		public delegate void ConnectionSucceededHandler();
		public delegate void ConnectionFailedHandler(bool will_restore = false);
		public delegate void LoginSucceededHandler();
		public delegate void PreDestroyHandler();

		public event ConnectionSucceededHandler ConnectionSucceeded;
		public event ConnectionFailedHandler ConnectionFailed;
		public event LoginSucceededHandler LoginSucceeded;
		public event MessageReceivedHandler MessageReceived;
		public event PreDestroyHandler PreDestroy;

		public string UserName { get; private set; }

		private SnipeServiceClient mClient;

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
				return mClient != null && mClient.Connected;
			}
		}

		public bool LoggedIn
		{
			get { return mClient != null && mClient.LoggedIn; }
		}
		
		private int mRoomId = 0;
		public bool RoomJoined
		{
			get { return mRoomId != 0 && mClient != null && mClient.LoggedIn; }
		}

		private bool mDisconnecting = false;
		
		private readonly List<Action> mMainThreadActions = new List<Action>();
		private bool mHasMainThreadActions = false; // To prevent the use of the lock keyword every frame
		
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
					DontDestroyOnLoad(game_object);
				}
				return mInstance;
			}
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
			if (CheckLoginParams())
			{
				InitClient();
			}
			else
			{
				Authorize();
			}
		}

		private bool CheckLoginParams()
		{
			if (SnipeAuthCommunicator.UserID != 0 && !string.IsNullOrEmpty(SnipeAuthCommunicator.LoginToken))
			{
				// TODO: check token expiry
				return true;
			}

			return false;
		}

		private void Authorize()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Authorize");

			SnipeAuthCommunicator.Authorize(OnAuthSucceeded, OnAuthFailed);
		}

		private void OnAuthSucceeded()
		{
			InitClient();
		}

		private void OnAuthFailed()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnAuthFailed");

			if (ConnectionFailed != null)
			{
				InvokeInMainThread(() =>
				{
					ConnectionFailed?.Invoke(false);
				});
			}
		}

		private void InitClient()
		{
			if (LoggedIn)
			{
				DebugLogger.LogWarning($"[SnipeCommunicator] ({INSTANCE_ID}) InitClient - already logged in");
				return;
			}

			if (mClient == null)
			{
				mClient = new SnipeServiceClient();
				mClient.ConnectionOpened += OnClientConnectionSucceeded;
				mClient.ConnectionClosed += OnClientConnectionFailed;
				mClient.MessageReceived += OnMessageReceived;
			}

			lock (mClient)
			{
				if (!mClient.Connected)
				{
					mDisconnecting = false;
					mClient.Connect();
				}
			}
		}

		public void Disconnect()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) {this.name} Disconnect");

			mRoomId = 0;
			mDisconnecting = true;
			UserName = "";

			if (mClient != null)
				mClient.Disconnect();
		}

		private void OnDestroy()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnDestroy");
			
			mRoomId = 0;
			
			ClearMainThreadActionsQueue();
			
			DisposeRequests();

			try
			{
				PreDestroy?.Invoke();
			}
			catch (Exception) { }

			if (mClient != null)
			{
				mClient.ConnectionOpened -= OnClientConnectionSucceeded;
				mClient.ConnectionClosed -= OnClientConnectionFailed;
				mClient.MessageReceived -= OnMessageReceived;
				mClient.Disconnect();
				mClient = null;
			}
		}
		
		private void OnClientConnectionSucceeded()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Connection succeeded");

			mRestoreConnectionAttempt = 0;
			mDisconnecting = false;

			InvokeInMainThread(() =>
			{
				AnalyticsTrackConnectionSucceeded();
				ConnectionSucceeded?.Invoke();
			});
		}
		
		private void OnClientConnectionFailed()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{mClient?.ConnectionId}] Game Connection failed");
			
			mRoomId = 0;

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

		private void OnMessageReceived(string message_type, string error_code, ExpandoObject data, int request_id)
		{
			// DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{mClient?.ConnectionId}] OnMessageReceived {request_id} {message_type} {error_code} " + (data != null ? data.ToJSONString() : "null"));

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
				if (error_code == SnipeErrorCodes.OK)
				{
					mRoomId = data?.SafeGetValue<int>("roomID") ?? 0;
				}
				else
				{
					mRoomId = 0;
					DisposeRoomRequests();
				}
			}
			else if (message_type == SnipeMessageTypes.ROOM_DEAD)
			{
				mRoomId = 0;
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
				Analytics.TrackErrorCodeNotOk(message_type, error_code, data);
			}
		}
		
		#region Main Thread
		
		private void InvokeInMainThread(Action action)
		{
			lock (mMainThreadActions)
			{
				mMainThreadActions.Add(action);
				mHasMainThreadActions = true;
			}
		}

		private void ClearMainThreadActionsQueue()
		{
			lock (mMainThreadActions)
			{
				mMainThreadActions.Clear();
			}
		}

		private void Update()
		{
			if (!mHasMainThreadActions)
				return;
				
			lock (mMainThreadActions)
			{
				for (int i = 0; i < mMainThreadActions.Count; i++)
				{
					var action = mMainThreadActions[i];
					if (action == null)
						continue;
					
					action.Invoke();

					// the handler could have called Dispose
					// if (!Connected)
					// {
						// mMainThreadCoroutine = null;
						// yield break;
					// }
				}
				
				mMainThreadActions.Clear();
				mHasMainThreadActions = false;
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
		
		public void Request(string message_type, ExpandoObject parameters = null)
		{
			CreateRequest(message_type, parameters).Request();
		}

		internal int Request(SnipeCommunicatorRequest request)
		{
			if (!LoggedIn || request == null)
				return 0;

			return mClient.SendRequest(request.MessageType, request.Data);
		}

		public SnipeCommunicatorRequest CreateRequest(string message_type = null, ExpandoObject parameters = null)
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
		
		public void RequestActionRun(string action_id, ExpandoObject parameters = null)
		{
			if (mClient == null || !mClient.LoggedIn)
				return;
			
			CreateActionRunRequest(action_id, parameters).Request();
		}
		
		public SnipeCommunicatorRequest CreateActionRunRequest(string action_id, ExpandoObject parameters = null)
		{
			if (parameters == null)
				parameters = new ExpandoObject() { ["actionID"] = action_id };
			else
				parameters["actionID"] = action_id;
			
			return CreateRequest(SnipeMessageTypes.ACTION_RUN, parameters);
		}
		
		#endregion // ActionRun Requests
		
		public void Dispose()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Dispose");
			
			StopAllCoroutines();
			mHasMainThreadActions = false;
			
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
		
		private void AnalyticsTrackConnectionSucceeded()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_CONNECTED, new ExpandoObject()
			{
				["connection_type"] = "websocket",
			});
		}
		
		private void AnalyticsTrackConnectionFailed()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_DISCONNECTED, new ExpandoObject()
			{
				//["communicator"] = this.name,
				["connection_id"] = mClient?.ConnectionId,
				//["disconnect_reason"] = mClient?.DisconnectReason,
				//["check_connection_message"] = mClient?.CheckConnectionMessageType,
			});
		}
		
		#endregion Analytics
	}
}