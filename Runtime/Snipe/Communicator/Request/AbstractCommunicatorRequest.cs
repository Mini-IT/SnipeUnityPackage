using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe
{
	public abstract class AbstractCommunicatorRequest : IDisposable
	{
		protected const int RETRIES_COUNT = 3;
		protected const int RETRY_DELAY_MS = 1000;

		protected static readonly SnipeObject EMPTY_DATA = new SnipeObject();
		
		public string MessageType { get; private set; }
		public SnipeObject Data { get; set; }
		
		public delegate void ResponseHandler(string error_code, SnipeObject data);

		protected SnipeCommunicator _communicator;
		protected ResponseHandler _callback;

		protected int _requestId { get; private set; }
		private int _retriesLeft = RETRIES_COUNT;
		
		private bool _sent = false;
		protected bool _waitingForResponse = false;

		private ILogger _logger;

		public AbstractCommunicatorRequest(SnipeCommunicator communicator, string messageType = null, SnipeObject data = null)
		{
			_communicator = communicator;
			MessageType = messageType;
			Data = data;
			
			_communicator?.Requests.Add(this);
		}

		public void Request(SnipeObject data, ResponseHandler callback = null)
		{
			Data = data;
			Request(callback);
		}

		public void Request(ResponseHandler callback = null)
		{
			if (_sent)
				return;
				
			_callback = callback;
			SendRequest();
		}
		
		private void SendRequest()
		{
			_sent = true;
			
			if (!CheckCommunicatorValid())
			{
				InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
				return;
			}

			if (string.IsNullOrEmpty(MessageType))
			{
				MessageType = Data?.SafeGetString("t");
			}

			if (string.IsNullOrEmpty(MessageType))
			{
				InvokeCallback(SnipeErrorCodes.INVALIND_DATA, EMPTY_DATA);
				return;
			}
			
			if (CheckCommunicatorReady())
			{
				OnCommunicatorReady();
			}
			else
			{
				OnConnectionClosed(true);
			}
		}
		
		protected virtual bool CheckCommunicatorValid()
		{
			return _communicator != null;
		}

		protected virtual bool CheckCommunicatorReady()
		{
			return _communicator.Connected;
		}

		protected virtual void OnCommunicatorReady()
		{
			_communicator.ConnectionFailed -= OnConnectionClosed;
			_communicator.ConnectionFailed += OnConnectionClosed;
			
			if (_callback != null)
			{
				_waitingForResponse = true;
				_communicator.MessageReceived -= OnMessageReceived;
				_communicator.MessageReceived += OnMessageReceived;
			}
			
			DoSendRequest();
		}
		
		protected void DoSendRequest()
		{
			_requestId = 0;
			
			bool check_duplication = false;
			
			foreach (SnipeRequestDescriptor descriptor in _communicator.MergeableRequestTypes)
			{
				string mergeble_type = descriptor?.MessageType;
					
				if (mergeble_type != null && string.Equals(mergeble_type, MessageType, StringComparison.Ordinal))
				{
					bool matched = true;
						
					if (descriptor.Data != null && Data != null)
					{
						foreach (var pair in descriptor.Data)
						{
							if (Data[pair.Key] != pair.Value)
							{
								matched = false;
								break;
							}
						}
					}
						
					if (matched)
					{
						check_duplication = true;
						break;
					}
				}
			}
			
			if (check_duplication)
			{
				for (int i = 0; i < _communicator.Requests.Count; i++)
				{
					var request = _communicator.Requests[i];
					
					if (request == null)
						continue;
					
					if (object.ReferenceEquals(request, this))
						break;
					
					if (request.Equals(this))
					{
						_requestId = request._requestId;
						break;
					}
				}
			}
			
			if (_requestId != 0)
			{
				GetLogger().LogTrace($"DoSendRequest - Same request found: {MessageType}, id = {_requestId}, mWaitingForResponse = {_waitingForResponse}");
				
				if (!_waitingForResponse)
				{
					Dispose();
				}
				return;
			}
			
			if (CheckCommunicatorReady())
			{
				_requestId = _communicator.Client.SendRequest(MessageType, Data);
			}
			
			if (_requestId == 0)
			{
				InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
			}
			
			if (!_waitingForResponse)
			{
				// keep this instance for a while to prevent duplicate requests
				DelayedDispose();
			}
		}

		protected void OnConnectionClosed(bool will_retry = false)
		{
			if (will_retry && _requestId == 0)
			{
				_waitingForResponse = false;
				OnWillReconnect();
			}
			else
			{
				if (_requestId != 0) // if the request is considered sent but not responsed yet
				{
					GetLogger().LogTrace($"Disposing request {_requestId} {MessageType}");
					InvokeCallback(SnipeErrorCodes.NOT_READY, EMPTY_DATA);
				}
				Dispose();
			}
		}

		protected virtual void OnWillReconnect()
		{
			_communicator.ConnectionSucceeded -= OnCommunicatorReady;
			_communicator.MessageReceived -= OnMessageReceived;

			GetLogger().LogTrace($"Waiting for connection - {MessageType}");

			_communicator.ConnectionSucceeded += OnCommunicatorReady;
		}

		protected virtual void OnMessageReceived(string message_type, string error_code, SnipeObject response_data, int request_id)
		{
			if (_communicator == null)
				return;
			
			if ((request_id == 0 || request_id == _requestId) && message_type == MessageType)
			{
				if (error_code == SnipeErrorCodes.SERVICE_OFFLINE && _retriesLeft > 0)
				{
					_retriesLeft--;
					DelayedRetryRequest();
					return;
				}

				InvokeCallback(error_code, response_data);
			}
		}
		
		protected void InvokeCallback(string error_code, SnipeObject response_data)
		{
			var callback = _callback;
			
			Dispose();
			
			if (callback != null)
			{
				try
				{
					callback.Invoke(error_code, response_data);
				}
				catch (Exception e)
				{
					GetLogger().LogTrace($"{MessageType} Callback invokation error: {e}");
				}
			}
		}
		
		private async void DelayedRetryRequest()
		{
			await Task.Delay(RETRY_DELAY_MS);
			
			if (_communicator != null)
			{
				Request(_callback);
			}
		}
		
		private async void DelayedDispose()
		{
			await Task.Yield();
			Dispose();
		}

		public virtual void Dispose()
		{
			if (_communicator != null)
			{
				if (_communicator.Requests != null)
				{
					_communicator.Requests.Remove(this);
				}
				
				_communicator.ConnectionSucceeded -= OnCommunicatorReady;
				_communicator.ConnectionFailed -= OnConnectionClosed;
				_communicator.MessageReceived -= OnMessageReceived;
				_communicator = null;
			}
			
			_callback = null;
			_waitingForResponse = false;
		}

		public override bool Equals(object obj) => obj is AbstractCommunicatorRequest request
			&& string.Equals(request.MessageType, MessageType, StringComparison.Ordinal)
			&& SnipeObject.ContentEquals(request.Data, Data);

		public override int GetHashCode()
		{
			// Autogenerated
			int hashCode = -1826397615;
			hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(MessageType);
			hashCode = hashCode * -1521134295 + EqualityComparer<SnipeObject>.Default.GetHashCode(Data);
			return hashCode;
		}

		private ILogger GetLogger()
		{
			_logger ??= SnipeServices.Instance.LogService.GetLogger(nameof(AbstractCommunicatorRequest));
			return _logger;
		}
	}
}
