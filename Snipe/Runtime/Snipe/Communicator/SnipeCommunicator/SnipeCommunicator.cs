using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeCommunicator : MonoBehaviour, IDisposable
	{
		protected readonly int INSTANCE_ID = new System.Random().Next();
		
		private const float RETRY_INIT_CLIENT_DELAY = 0.5f; // seconds
		
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

		private SnipeServiceClient mClient;// { get; private set; }

		public int RestoreConnectionAttempts = 3;
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

		protected bool mDisconnecting = false;
		
		private readonly List<Action> mMainThreadActions = new List<Action>();
		private Coroutine mMainThreadCoroutine;
		
		/// <summary>
		/// Should be called from the main Unity thread
		/// </summary>
		public virtual void StartCommunicator()
		{
			DontDestroyOnLoad(this.gameObject);
			
			if (CheckLoginParams())
			{
				InitClient();
			}
			else
			{
				Authorize();
			}
			
			if (mMainThreadCoroutine == null)
			{
				mMainThreadCoroutine = StartCoroutine(MainThreadCoroutine());
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

		protected void OnAuthSucceeded()
		{
			InitClient();
		}

		protected void OnAuthFailed()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnAuthFailed");

			if (ConnectionFailed != null)
			{
				lock (mMainThreadActions)
				{
					mMainThreadActions.Add(() =>
					{
						ConnectionFailed?.Invoke(false);
					});
				}
			}
		}

		protected virtual void InitClient()
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

		public virtual void Disconnect()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) {this.name} Disconnect");

			mRoomId = 0;
			mDisconnecting = true;
			UserName = "";

			if (mClient != null)
				mClient.Disconnect();
		}

		protected virtual void OnDestroy()
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
			
			InvokeInMainThread(() =>
			{
				AnalyticsTrackConnectionSucceeded();
				OnConnectionSucceeded();
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
		protected virtual void OnConnectionSucceeded()
		{
			mRestoreConnectionAttempt = 0;
			mDisconnecting = false;

			ConnectionSucceeded?.Invoke();
		}
		
		// Main thread
		protected virtual void OnConnectionFailed()
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
			}
		}

		private void OnMessageReceived(string message_type, string error_code, ExpandoObject data, int request_id)
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{mClient?.ConnectionId}] OnMessageReceived {request_id} {message_type} {error_code} " + (data != null ? data.ToJSONString() : "null"));

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
			else if (mRoomId != 0 && message_type == SnipeMessageTypes.ROOM_DEAD || (message_type == SnipeMessageTypes.ROOM_LOGOUT && error_code == SnipeErrorCodes.OK))
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
		}
		
		#region Main Thread
		
		protected void InvokeInMainThread(Action action)
		{
			lock (mMainThreadActions)
			{
				mMainThreadActions.Add(action);
			}
		}

		protected void ClearMainThreadActionsQueue()
		{
			lock (mMainThreadActions)
			{
				mMainThreadActions.Clear();
			}
		}

		protected IEnumerator MainThreadCoroutine()
		{
			while (true)
			{
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
				}
				
				yield return null;
			}
		}

		#endregion // Main Thread

		private IEnumerator WaitAndInitClient()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) WaitAndInitClient - start delay");
			yield return new WaitForSecondsRealtime(RETRY_INIT_CLIENT_DELAY);
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
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeOfflineRequests - begin");
			
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
			
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeOfflineRequests - done");
		}
		
		private void DisposeRoomRequests()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeRoomRequests - begin");
			
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
			
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeRoomRequests - done");
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
		
		public virtual void Dispose()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Dispose");
			
			StopAllCoroutines();
			mMainThreadCoroutine = null;
			
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
			
			var temp_requests = mRequests;
			mRequests = null;
			foreach (var request in temp_requests)
			{
				request?.Dispose();
			}
			//temp_requests.Clear();
		}
		
		#region Analytics
		
		protected virtual void AnalyticsTrackConnectionSucceeded()
		{
			Analytics.TrackEvent(Analytics.EVENT_COMMUNICATOR_CONNECTED, new ExpandoObject()
			{
				["connection_type"] = "websocket",
			});
		}
		
		protected virtual void AnalyticsTrackConnectionFailed()
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