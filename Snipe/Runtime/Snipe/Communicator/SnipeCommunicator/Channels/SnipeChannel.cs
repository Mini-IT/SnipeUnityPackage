using System;
using System.Collections.Generic;
using MiniIT;

namespace MiniIT.Snipe
{
	public class SnipeChannel
	{
		public string Name;
		public bool KeepRequestsIfNotReady = true; // Requests created before the channel is ready will be sent after the channel is ready
		public List<SnipeRequest> Requests  { get; private set; }
		
		protected List<string> mNoScopeMessageTypes;
		
		public SnipeChannel()
		{
			Requests = new List<SnipeRequest>();
			
			// if (SnipeCommunicator.InstanceInitialized)
			// {
				// SnipeCommunicator.Instance.ConnectionSucceeded += OnCommunicatorConnectionSucceeded;
				// SnipeCommunicator.Instance.ConnectionFailed += OnCommunicatorConnectionFailed;
				// SnipeCommunicator.Instance.LoginSucceeded += OnCommunicatorLoggedIn;
				// SnipeCommunicator.Instance.MessageReceived += OnCommunicatorMessageReceived;
			// }
			
			Initialize();
		}
		
		public virtual bool CheckReady()
		{
			return SnipeCommunicator.InstanceInitialized && SnipeCommunicator.Instance.LoggedIn;
		}
		
		public virtual bool CheckReady(string message_type)
		{
			if (CheckReady())  // CheckReady may require more than just login
				return true;
			
			return SnipeCommunicator.InstanceInitialized && SnipeCommunicator.Instance.LoggedIn && CheckNoScopeMessageType(message_type);
		}
		
		public virtual bool CheckNoScopeMessageType(string message_type)
		{
			return mNoScopeMessageTypes != null && mNoScopeMessageTypes.Contains(message_type);
		}
		
		public void AddNoScopeMessageTypes(params string[] message_types)
		{
			// if (mNoScopeMessageTypes == null)
				// mNoScopeMessageTypes = new List<string>();
			// foreach (var type in message_types)
			// {
				// if (!string.IsNullOrEmpty(type))
				// {
					// mNoScopeMessageTypes.Add(type);
				// }
			// }
			AddStringItems(ref mNoScopeMessageTypes, message_types);
		}
		
		protected void AddStringItems(ref List<string> list, params string[] items)
		{
			if (items == null || items.Length == 0)
				return;
			if (list == null)
				list = new List<string>();
			foreach (var item in items)
			{
				if (!string.IsNullOrEmpty(item))
				{
					list.Add(item);
				}
			}
		}
		
		public void Request(string message_type, SnipeObject parameters = null)
		{
			CreateRequest(message_type, parameters).Request();
		}
		
		public SnipeRequest CreateRequest(string message_type = null, SnipeObject parameters = null)
		{
			var request = new SnipeRequest(this, message_type);
			request.Data = parameters;
			return request;
		}
		
		public void DisposeRequests()
		{
			DisposeRequests(false);
		}
		
		private void DisposeRequests(bool destroy)
		{
			DebugLogger.Log($"[SnipeChannel] ({Name}) DisposeRequests");
			
			if (Requests.Count > 0)
			{
				var temp_requests = Requests;
				
				Requests = destroy ? null : new List<SnipeRequest>();
				
				foreach (var request in temp_requests)
				{
					request?.Dispose();
				}
			}
		}
		
		public void Dispose()
		{
			DisposeRequests(true);
		}
		
		protected virtual void Initialize()
		{
			if (CheckReady())
				return;
			
			if (SnipeCommunicator.InstanceInitialized)
			{
				SnipeCommunicator.Instance.LoginSucceeded += OnCommunicatorLoggedIn;
				
			}
		}
		
		private void OnCommunicatorLoggedIn()
		{
			RaiseGotReady();
		}
		
		protected void RaiseGotReady()
		{
			// if (GotReady != null)
			// {
				// SnipeCommunicator.Instance.InvokeInMainThread(() =>
				// {
					// GotReady?.Invoke();
				// });
			// }
			
			foreach (var request in Requests)
			{
				request.OnChannelReady();
			}
		}
	}
}