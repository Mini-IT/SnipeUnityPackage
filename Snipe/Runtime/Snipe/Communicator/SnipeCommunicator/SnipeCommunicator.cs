using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeCommunicator : MonoBehaviour, IDisposable
	{
		protected readonly int INSTANCE_ID = new System.Random().Next();
		
		public delegate void MessageReceivedHandler(ExpandoObject data, bool original = false);
		public delegate void ConnectionSucceededHandler();
		public delegate void ConnectionFailedHandler(bool will_restore = false);
		public delegate void LoginSucceededHandler();
		public delegate void PreDestroyHandler();

		public event ConnectionSucceededHandler ConnectionSucceeded;
		public event ConnectionFailedHandler ConnectionFailed;
		public event LoginSucceededHandler LoginSucceeded;
		public event MessageReceivedHandler MessageReceived;
		public event PreDestroyHandler PreDestroy;

		public string LoginName { get; private set; }

		public SnipeClient Client { get; protected set; }
		public SnipeServiceCommunicator ServiceCommunicator { get; private set; }

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
		

		public virtual void StartCommunicator()
		{
			DontDestroyOnLoad(this.gameObject);

			if (ServiceCommunicator == null)
				ServiceCommunicator = this.gameObject.AddComponent<SnipeServiceCommunicator>();
			else
				ServiceCommunicator.DisposeClient();

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

		protected void OnAuthSucceeded()
		{
			InitClient();
		}

		protected void OnAuthFailed()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnAuthFailed");

			if (ConnectionFailed != null)
				ConnectionFailed.Invoke(false);
		}

		protected virtual void InitClient()
		{
			InitClient(SnipeConfig.Instance.ServerWebsocketURL);
		}

		protected virtual void InitClient(string web_socket_url)
		{
			if (LoggedIn)
			{
				DebugLogger.LogWarning($"[SnipeCommunicator] ({INSTANCE_ID}) InitClient - already logged in");
				return;
			}
			
			if (Client == null)
			{
				Client = SnipeClient.CreateInstance(SnipeConfig.Instance.ClientKey, this.gameObject);
				Client.AppInfo = SnipeConfig.Instance.AppInfo;
				Client.Init(web_socket_url);
				Client.ConnectionSucceeded += OnClientConnectionSucceeded;
				Client.ConnectionFailed += OnClientConnectionFailed;
				Client.ConnectionLost += OnClientConnectionFailed;
			}

			mDisconnecting = false;

			if (Client.Connected)
				RequestLogin();
			else
				Client.Connect();
		}

		public virtual void Reconnect()
		{
			if (Client == null)
				return;

			Client.Reconnect();
		}

		public virtual void Disconnect()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) {this.name} Disconnect");

			mDisconnecting = true;
			LoginName = "";

			if (Client != null)
				Client.Disconnect();
		}

		protected virtual void OnDestroy()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnDestroy");
			
			DisposeRequests();

			try
			{
				PreDestroy?.Invoke();
			}
			catch (Exception) { }

			if (Client != null)
			{
				Client.ConnectionSucceeded -= OnClientConnectionSucceeded;
				Client.ConnectionFailed -= OnClientConnectionFailed;
				Client.ConnectionLost -= OnClientConnectionFailed;
				Client.MessageReceived -= OnSnipeResponse;
				Client.Disconnect();
				Client = null;
			}
		}
		
		private void OnClientConnectionSucceeded(ExpandoObject data)
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) {this.name} Connection succeeded");
			
			AnalyticsTrackConnectionSucceeded();
			
			OnConnectionSucceeded(data);
		}
		
		private void OnClientConnectionFailed(ExpandoObject data = null)
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) {this.name} [{Client?.ConnectionId}] Game Connection failed. Reason: {Client?.DisconnectReason}");
			
			AnalyticsTrackConnectionFailed();
			
			OnConnectionFailed(data);
		}
		
		protected virtual void OnConnectionSucceeded(ExpandoObject data)
		{
			mRestoreConnectionAttempt = 0;
			mDisconnecting = false;

			if (ConnectionSucceeded != null)
				ConnectionSucceeded.Invoke();

			Client.MessageReceived += OnSnipeResponse;
			
			RequestLogin();
		}

		protected virtual void OnConnectionFailed(ExpandoObject data = null)
		{
			if (Client != null)
				Client.MessageReceived -= OnSnipeResponse;

			if (mRestoreConnectionAttempt < RestoreConnectionAttempts && !mDisconnecting)
			{
				ConnectionFailed?.Invoke(true);
				
				mRestoreConnectionAttempt++;
				DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Attempt to restore connection {mRestoreConnectionAttempt}");
				StartCoroutine(WaitAndInitClient());
			}
			else
			{
				ConnectionFailed?.Invoke(false);
			}
		}
		
		internal void OnRoomConnectionFailed(ExpandoObject data = null)
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) OnRoomConnectionFailed");
			if (Connected)
			{
				Client.Disconnect();
				OnConnectionFailed(data);
			}
		}

		private void OnSnipeResponse(ExpandoObject data)
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) [{Client?.ConnectionId}] OnSnipeResponse " + (data != null ? data.ToJSONString() : "null"));

			ProcessSnipeMessage(data, true);

			if (data["serverNotify"] is IList notification_messages)
			{
				foreach (ExpandoObject notification_data in notification_messages)
				{
					if (notification_data.ContainsKey("_type"))
						notification_data["type"] = notification_data["_type"];

					ProcessSnipeMessage(notification_data, false);
				}
			}
		}

		protected virtual void ProcessSnipeMessage(ExpandoObject data, bool original = false)
		{
			string message_type = data.SafeGetString("type");
			string error_code = data.SafeGetString("errorCode");

			if (!string.IsNullOrEmpty(error_code) && error_code != "ok")
			{
				DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) errorCode = " + error_code);
			}

			switch (message_type)
			{
				case "user.login":
					if (error_code == "ok")
					{
						LoginName = data.SafeGetString("name");

						if (LoginSucceeded != null)
							LoginSucceeded.Invoke();
					}
					else if (error_code == "wrongToken" || error_code == "userNotFound")
					{
						Authorize();
					}
					else if (error_code == "userDisconnecting")
					{
						StartCoroutine(WaitAndRequestLogin());
					}
					else if (error_code == "userOnline")
					{
						RequestLogout();
						StartCoroutine(WaitAndRequestLogin());
					}
					break;
			}

			MessageReceived?.Invoke(data, original);
		}

		private IEnumerator WaitAndInitClient()
		{
			yield return new WaitForSeconds(0.5f);
			InitClient();
		}

		private IEnumerator WaitAndRequestLogin()
		{
			yield return new WaitForSeconds(1.0f);
			RequestLogin();
		}

		protected void RequestLogin()
		{
			if (ServiceCommunicator != null)
				ServiceCommunicator.DisposeClient();

			ExpandoObject data = new ExpandoObject();
			data["id"] = SnipeAuthCommunicator.UserID;
			data["token"] = SnipeAuthCommunicator.LoginToken;
			//data["lang"] = "ru";

			Client.SendRequest("user.login", data);
		}

		protected void RequestLogout()
		{
			Client.SendRequest("kit/user.logout");
		}
		
		// [Obsolete("Use CreateRequest instead.", false)]
		public void Request(string message_type, ExpandoObject parameters = null)
		{
			CreateRequest(message_type, parameters).Request();
		}

		internal int Request(SnipeCommunicatorRequest request)
		{
			if (Client == null || request == null || !Client.LoggedIn)
				return 0;

			return Client.SendRequest(request.MessageType, request.Data);
		}

		public SnipeCommunicatorRequest CreateRequest(string message_type = null, ExpandoObject parameters = null)
		{
			var request = new SnipeCommunicatorRequest(this, message_type);
			request.Data = parameters;
			return request;
		}
		
		public void ResendOfflineRequests()
		{
			DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) ResendOfflineRequests - begin");
			
			foreach (var request in Requests)
			{
				if (request != null && !request.Active)
				{
					request.ResendInactive();
				}
			}
			
			DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) ResendOfflineRequests - done");
		}
		
		public void DisposeOfflineRequests()
		{
			DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeOfflineRequests - begin");
			
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
			
			DebugLogger.LogError($"[SnipeCommunicator] ({INSTANCE_ID}) DisposeOfflineRequests - done");
		}

		#region Kit Requests

		public void RequestKitActionSelf(string action_id, ExpandoObject parameters = null)
		{
			if (Client == null || !Client.LoggedIn)
				return;
			
			CreateKitActionSelfRequest(action_id, parameters).Request();
		}

		public void RequestKitAttrSet(string key, object value)
		{
			ExpandoObject parameters = new ExpandoObject()
			{
				["key"] = key,
				["val"] = value,
			};
			CreateRequest("kit/attr.set", parameters).Request();
		}

		public void RequestKitAttrGetAll()
		{
			Client.SendRequest("kit/attr.getAll");
		}

		public SnipeRequest CreateKitActionSelfRequest(string action_id, ExpandoObject parameters = null)
		{
			SnipeKitActionSelfRequest request = new SnipeKitActionSelfRequest(this, action_id);
			request.Data = parameters;
			return request;
		}

		#endregion // Kit Requests

		public virtual void Dispose()
		{
			DebugLogger.Log($"[SnipeCommunicator] ({INSTANCE_ID}) Dispose");
			
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
				["communicator"] = this.name,
				["connection_id"] = Client?.ConnectionId,
				["disconnect_reason"] = Client?.DisconnectReason,
				["check_connection_message"] = Client?.CheckConnectionMessageType,
			});
		}
		
		#endregion Analytics
	}
}