using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniIT;
using UnityEngine;

namespace MiniIT.Snipe
{
	public class SnipeServiceCommunicator : MonoBehaviour
	{
		public event Action<ExpandoObject> MessageReceived;
		public event Action ConnectionClosed;

		public bool Ready { get { return Client != null && Client.LoggedIn; } }

		internal SnipeServiceClient Client { get; private set; }
		
		internal readonly List<SnipeServiceRequest> Requests = new List<SnipeServiceRequest>();
		
		private Queue<ExpandoObject> mReceivedMessages = null;

		private List<Action> mReadyCallbacks;

		public void StartCommunicator(Action ready_callback = null)
		{
			AddReadyCallback(ready_callback);

			if (Ready)
			{
				return; // ready_callback will be invoked in Update (main thread)
			}

			Connect();
		}
		
		public void AddReadyCallback(Action ready_callback)
		{
			if (ready_callback != null)
			{
				if (mReadyCallbacks == null)
					mReadyCallbacks = new List<Action>();
				
				lock (mReadyCallbacks)
				{
					if (!mReadyCallbacks.Contains(ready_callback))
						mReadyCallbacks.Add(ready_callback);
				}
			}
		}
		
		private void Connect()
		{
			if (Client == null)
			{
				Client = new SnipeServiceClient();
			}
			
			lock (Client)
			{
				if (!Client.Connected)
				{
					Client.LoginSucceeded -= OnLoginSucceeded;
					Client.LoginFailed -= OnLoginFailed;
					Client.LoginSucceeded += OnLoginSucceeded;
					Client.LoginFailed += OnLoginFailed;
					Client.Connect();
				}
			}
		}

		public void DisposeClient()
		{
			mReceivedMessages = null;

			if (Client != null)
			{
				Client.LoginSucceeded -= OnLoginSucceeded;
				Client.LoginFailed -= OnLoginFailed;
				Client.MessageReceived -= OnMessageReceived;
				Client.ConnectionClosed -= OnConnectionClosed;
				Client.Disconnect();
				Client = null;
			}
		}

		private void OnLoginSucceeded()
		{
			DebugLogger.Log("[SnipeServiceCommunicator] OnLoginSucceeded");
			
			if (mReconnectCancellation != null)
			{
				mReconnectCancellation.Cancel();
				mReconnectCancellation = null;
			}
			
			Client.LoginSucceeded -= OnLoginSucceeded;
			Client.LoginFailed -= OnLoginFailed;
			Client.MessageReceived += OnMessageReceived;
			Client.ConnectionClosed += OnConnectionClosed;
			mReceivedMessages = new Queue<ExpandoObject>();
		}

		private void OnLoginFailed(string error_code)
		{
			DebugLogger.Log("[SnipeServiceCommunicator] OnLoginFailed " + error_code);
			
			Client.LoginSucceeded -= OnLoginSucceeded;
			Client.LoginFailed -= OnLoginFailed;

			// TODO: process error
		}
		
		private void OnConnectionClosed()
		{
			Client.LoginSucceeded -= OnLoginSucceeded;
			Client.LoginFailed -= OnLoginFailed;
			Client.MessageReceived -= OnMessageReceived;
			Client.ConnectionClosed -= OnConnectionClosed;
			
			try
			{
				ConnectionClosed?.Invoke();
			}
			catch (Exception e)
			{
				DebugLogger.Log("[SnipeServiceCommunicator] OnConnectionClosed - ConnectionClosed invokation error: " + e.Message);
			}
			
			Reconnect();
		}
		
		#region Reconnect
		private CancellationTokenSource mReconnectCancellation;
		
		private void Reconnect()
		{
			if (mReconnectCancellation != null)
				return;

			mReconnectCancellation = new CancellationTokenSource();
			_ = ReconnectTask(mReconnectCancellation.Token);
		}
		
		private async Task ReconnectTask(CancellationToken cancellation)
		{
			await Task.Delay(1000, cancellation);
			
			while (!cancellation.IsCancellationRequested && Client != null && !Client.Connected)
			{
				Connect();
				await Task.Delay(1000, cancellation);
			}
			
			mReconnectCancellation = null;
		}
		
		#endregion // Reconnect

		protected void OnDestroy()
		{
			for (int i = Requests.Count - 1; i >= 0; i--)
			{
				Requests[i]?.Dispose(false);
			}
			Requests.Clear();
			
			DisposeClient();
		}

		private void OnMessageReceived(string message_type, string error_code, ExpandoObject data, int request_id)
		{
#if UNITY_EDITOR
			DebugLogger.Log($"[SnipeServiceCommunicator] OnMessageReceived: {message_type}" + data?.ToJSONString());
#endif

			if (mReceivedMessages != null)
			{
				lock (mReceivedMessages)
				{
					mReceivedMessages.Enqueue(data);
				}
			}
		}

		private void Update()
		{
			if (Ready && mReadyCallbacks != null)
			{
				lock (mReadyCallbacks)
				{
					for (int i = 0; i < mReadyCallbacks.Count; i++)
					{
						try
						{
							mReadyCallbacks[i]?.Invoke();
						}
						catch (Exception e)
						{
							DebugLogger.Log("[SnipeServiceCommunicator] ReadyCallback Invoke Error: " + e.Message);
						}
					}
				}
				mReadyCallbacks = null;
			}

			if (mReceivedMessages != null)
			{
				lock (mReceivedMessages)
				{
					while (mReceivedMessages.Count > 0)
					{
						try
						{
							MessageReceived?.Invoke(mReceivedMessages.Dequeue());
						}
						catch (Exception ex)
						{
							DebugLogger.Log("[SnipeServiceCommunicator] MessageReceived Invoke Error: " + ex.Message);
						}

					}
				}
			}
		}

		public SnipeServiceRequest CreateRequest(string message_type = null)
		{
			return new SnipeServiceRequest(this, message_type);
		}

		public void Request(string message_type, ExpandoObject data, Action<ExpandoObject> callback = null)
		{
			new SnipeServiceRequest(this, message_type).Request(data, callback);
		}
	}
}