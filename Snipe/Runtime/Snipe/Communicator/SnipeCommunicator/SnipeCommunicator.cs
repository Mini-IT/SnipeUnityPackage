using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeCommunicator : MonoBehaviour, IDisposable
	{
		protected readonly int INSTANCE_ID = new System.Random().Next();
		
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

		internal SnipeServiceClient Client { get; private set; }

		public int RestoreConnectionAttempts = 3;
		private int mRestoreConnectionAttempt;
		
		public bool AllowRequestsToWaitForLogin = true;
		public bool KeepOfflineRequests = false; // works only if AllowRequestsToWaitForLogin == false
		
		public readonly List<SnipeCommunicatorRequest> Requests = new List<SnipeCommunicatorRequest>();

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

			if (Client == null)
			{
				Client = new SnipeServiceClient();
			}

			lock (Client)
			{
				if (!Client.Connected)
				{
					mDisconnecting = false;
					
					Client.ConnectionOpened -= OnClientConnectionSucceeded;
					Client.ConnectionClosed -= OnClientConnectionFailed;
					Client.ConnectionOpened += OnClientConnectionSucceeded;
					Client.ConnectionClosed += OnClientConnectionFailed;
					Client.Connect();
				}
			}
		}

		public virtual void Disconnect()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) {this.name} Disconnect");

			mDisconnecting = true;
			UserName = "";

			if (Client != null)
				Client.Disconnect();
		}

		protected virtual void OnDestroy()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnDestroy");
			
			ClearMainThreadActionsQueue();
			
			DisposeRequests();

			try
			{
				PreDestroy?.Invoke();
			}
			catch (Exception) { }

			if (Client != null)
			{
				Client.ConnectionOpened -= OnClientConnectionSucceeded;
				Client.ConnectionClosed -= OnClientConnectionFailed;
				Client.MessageReceived -= OnMessageReceived;
				Client.Disconnect();
				Client = null;
			}
		}
		
		private void OnClientConnectionSucceeded()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Connection succeeded");
			
			AnalyticsTrackConnectionSucceeded();
			
			OnConnectionSucceeded();
		}
		
		private void OnClientConnectionFailed()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{Client?.ConnectionId}] Game Connection failed");

			InvokeInMainThread(() =>
			{
				AnalyticsTrackConnectionFailed();
				OnConnectionFailed();
			});
		}
		
		protected virtual void OnConnectionSucceeded()
		{
			mRestoreConnectionAttempt = 0;
			mDisconnecting = false;

			if (ConnectionSucceeded != null)
			{
				InvokeInMainThread(() =>
				{
					ConnectionSucceeded?.Invoke();
				});
			}
			
			Client.MessageReceived -= OnMessageReceived;
			Client.MessageReceived += OnMessageReceived;
		}

		protected virtual void OnConnectionFailed()
		{
			if (Client != null)
				Client.MessageReceived -= OnMessageReceived;
			
			//ClearMainThreadActionsQueue();

			if (mRestoreConnectionAttempt < RestoreConnectionAttempts && !mDisconnecting)
			{
				if (ConnectionFailed != null)
				{
					InvokeInMainThread(() =>
					{
						ConnectionFailed?.Invoke(true);
					});
				}
				
				mRestoreConnectionAttempt++;
				DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Attempt to restore connection {mRestoreConnectionAttempt}");
				
				WaitAndInitClient();
			}
			else if (ConnectionFailed != null)
			{
				InvokeInMainThread(() =>
				{
					ConnectionFailed?.Invoke(true);
				});
			}
		}

		private void OnMessageReceived(string message_type, string error_code, ExpandoObject data, int request_id)
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{Client?.ConnectionId}] OnMessageReceived {request_id} {message_type} {error_code} " + (data != null ? data.ToJSONString() : "null"));

			InvokeInMainThread(() =>
			{
				ProcessSnipeMessage(message_type, error_code, data);
				MessageReceived?.Invoke(message_type, error_code, data, request_id);
			});
		}

		protected virtual void ProcessSnipeMessage(string message_type, string error_code, ExpandoObject data)
		{
			if (!string.IsNullOrEmpty(error_code) && error_code != "ok")
			{
				DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) errorCode = " + error_code);
			}

			switch (message_type)
			{
				case "user.login":
					if (error_code == "ok")
					{
						UserName = data.SafeGetString("name");

						LoginSucceeded?.Invoke();
					}
					else if (error_code == "wrongToken" || error_code == "userNotFound")
					{
						Authorize();
					}
					else if (error_code == "userDisconnecting")
					{
						//StartCoroutine(WaitAndRequestLogin());
					}
					else if (error_code == "userOnline")
					{
						//RequestLogout();
						//StartCoroutine(WaitAndRequestLogin());
					}
					break;
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
						if (!Connected)
						{
							mMainThreadCoroutine = null;
							yield break;
						}
					}
					
					mMainThreadActions.Clear();
				}
				
				yield return null;
			}
		}

		#endregion // Main Thread

		private async void WaitAndInitClient()
		{
			await Task.Delay(500);
			InitClient();
		}

		//private IEnumerator WaitAndRequestLogin()
		//{
		//	yield return new WaitForSeconds(1.0f);
		//	RequestLogin();
		//}

		//protected void RequestLogin()
		//{
		//	ExpandoObject data = new ExpandoObject();
		//	data["id"] = SnipeAuthCommunicator.UserID;
		//	data["token"] = SnipeAuthCommunicator.LoginToken;
		//	//data["lang"] = "ru";

		//	Client.SendRequest("user.login", data);
		//}
		
		public void Request(string message_type, ExpandoObject parameters = null)
		{
			CreateRequest(message_type, parameters).Request();
		}

		//internal int Request(SnipeCommunicatorRequest request)
		//{
		//	if (Client == null || request == null || !Client.LoggedIn)
		//		return 0;

		//	return Client.SendRequest(request.MessageType, request.Data);
		//}

		public SnipeCommunicatorRequest CreateRequest(string message_type = null, ExpandoObject parameters = null)
		{
			var request = new SnipeCommunicatorRequest(this, message_type);
			request.Data = parameters;
			return request;
		}
		
		//public void ResendOfflineRequests()
		//{
		//	DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) ResendOfflineRequests - begin");
			
		//	foreach (var request in Requests)
		//	{
		//		if (request != null && !request.Active)
		//		{
		//			request.ResendInactive();
		//		}
		//	}
			
		//	DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) ResendOfflineRequests - done");
		//}
		
		//public void DisposeOfflineRequests()
		//{
		//	DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeOfflineRequests - begin");
			
		//	List<SnipeCommunicatorRequest> inactive_requests = null;
		//	foreach (var request in Requests)
		//	{
		//		if (request != null && !request.Active)
		//		{
		//			if (inactive_requests == null)
		//				inactive_requests = new List<SnipeCommunicatorRequest>();
					
		//			inactive_requests.Add(request);
		//		}
		//	}
		//	if (inactive_requests != null)
		//	{
		//		foreach (var request in inactive_requests)
		//		{
		//			request?.Dispose();
		//		}
		//	}
			
		//	DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeOfflineRequests - done");
		//}

		#region ActionRun Requests
		
		public void RequestActionRun(string action_id, ExpandoObject parameters = null)
		{
			if (Client == null || !Client.LoggedIn)
				return;
			
			CreateActionRunRequest(action_id, parameters).Request();
		}
		
		public SnipeCommunicatorRequest CreateActionRunRequest(string action_id, ExpandoObject parameters = null)
		{
			if (parameters == null)
				parameters = new ExpandoObject() { ["actionID"] = action_id };
			else
				parameters["actionID"] = action_id;
			
			return CreateRequest("action.run", parameters);
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
			
			foreach (var request in Requests)
			{
				request?.Dispose(false);
			}
			Requests.Clear();
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
				["connection_id"] = Client?.ConnectionId,
				//["disconnect_reason"] = Client?.DisconnectReason,
				//["check_connection_message"] = Client?.CheckConnectionMessageType,
			});
		}
		
		#endregion Analytics
	}
}