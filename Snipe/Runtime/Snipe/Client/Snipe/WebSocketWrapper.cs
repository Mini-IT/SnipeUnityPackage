using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using MiniIT;
using WebSocketSharp;

namespace MiniIT.Snipe
{
	public partial class WebSocketWrapper : IDisposable
	{
		#pragma warning disable 0067

		public Action OnConnectionOpened;
		public Action OnConnectionClosed;
		public Action<byte[]> ProcessMessage;

		#pragma warning restore 0067

		private WebSocket mWebSocket = null;

		public WebSocketWrapper()
		{
		}

		public void Connect(string url)
		{
			Disconnect();

			mWebSocket = new WebSocket(url);
			mWebSocket.OnOpen += OnWebSocketConnected;
			mWebSocket.OnClose += OnWebSocketClosed;
			mWebSocket.OnMessage += OnWebSocketMessage;
			mWebSocket.ConnectAsync();
		}

		public void Disconnect()
		{
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

		public void Ping()
		{
			if (!Connected)
				return;

			mWebSocket.Ping();
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