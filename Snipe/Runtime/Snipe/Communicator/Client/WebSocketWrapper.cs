using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MiniIT;
using WebSocketSharp;

namespace MiniIT.Snipe
{
	public class WebSocketWrapper : IDisposable
	{
		protected const int CONNECTION_TIMEOUT = 3000;
		
		#pragma warning disable 0067

		public Action OnConnectionOpened;
		public Action OnConnectionClosed;
		public Action<byte[]> ProcessMessage;

		#pragma warning restore 0067

		private WebSocket mWebSocket = null;
		private CancellationTokenSource mConnectionWaitingCancellation;

		public WebSocketWrapper()
		{
		}

		public async void Connect(string url)
		{
			Disconnect();

			mWebSocket = new WebSocket(url);
			mWebSocket.OnOpen += OnWebSocketConnected;
			mWebSocket.OnClose += OnWebSocketClosed;
			mWebSocket.OnMessage += OnWebSocketMessage;
			mWebSocket.NoDelay = true;
			mWebSocket.ConnectAsync();
			
			mConnectionWaitingCancellation = new CancellationTokenSource();
			await WaitForConnection(mConnectionWaitingCancellation.Token);
			mConnectionWaitingCancellation = null;
		}
		
		private async Task WaitForConnection(CancellationToken cancellation)
		{
			try
			{
				await Task.Delay(CONNECTION_TIMEOUT, cancellation);
			}
			catch (TaskCanceledException)
			{
				// This is OK. Just terminating the task
				return;
			}
			
			if (!cancellation.IsCancellationRequested && !Connected)
			{
				DebugLogger.Log("[WebSocketWrapper] WaitForConnection - Connection timed out");
				OnWebSocketClosed(this, null);
			}
		}

		public void Disconnect()
		{
			if (mConnectionWaitingCancellation != null)
			{
				mConnectionWaitingCancellation.Cancel();
				mConnectionWaitingCancellation = null;
			}
			
			if (mWebSocket != null)
			{
				mWebSocket.OnOpen -= OnWebSocketConnected;
				mWebSocket.OnClose -= OnWebSocketClosed;
				mWebSocket.OnMessage -= OnWebSocketMessage;
				mWebSocket.CloseAsync();
				mWebSocket = null;
			}
		}

		protected void OnWebSocketConnected(object sender, EventArgs e)
		{
			if (mConnectionWaitingCancellation != null)
			{
				mConnectionWaitingCancellation.Cancel();
				mConnectionWaitingCancellation = null;
			}
			
			OnConnectionOpened?.Invoke();
		}

		protected void OnWebSocketClosed(object sender, EventArgs e)
		{
			Disconnect();

			OnConnectionClosed?.Invoke();
		}

		private void OnWebSocketMessage(object sender, MessageEventArgs e)
		{
			ProcessMessage(e.RawData);
		}
		
		public void SendRequest(string message)
		{
			if (!Connected)
				return;

			lock (mWebSocket)
			{
				mWebSocket.SendAsync(message, null);
			}
		}

		public void SendRequest(byte[] bytes)
		{
			if (!Connected)
				return;

			lock (mWebSocket)
			{
				mWebSocket.SendAsync(bytes, null);
			}
		}

		public void Ping(Action<bool> callback = null)
		{
			if (!Connected)
			{
				callback?.Invoke(false);
				return;
			}

			mWebSocket.PingAsync(callback);
		}

		public bool Connected
		{
			get
			{
				return (mWebSocket != null && mWebSocket.ReadyState == WebSocketState.Open);
			}
		}

		#region IDisposable implementation
		
		public void Dispose()
		{
			this.OnConnectionOpened = null;
			this.OnConnectionClosed = null;
			this.ProcessMessage = null;
			
			Disconnect();
		}
		
		#endregion
	}

}