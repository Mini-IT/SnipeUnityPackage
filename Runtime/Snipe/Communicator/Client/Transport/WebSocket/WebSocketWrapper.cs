using System;

namespace MiniIT.Snipe
{
	public abstract class WebSocketWrapper : IDisposable
	{
		protected const int CONNECTION_TIMEOUT = 5000;

		public bool Connected => IsConnected();
		
		#pragma warning disable 0067

		public Action OnConnectionOpened;
		public Action OnConnectionClosed;
		public Action<byte[]> ProcessMessage;

		#pragma warning restore 0067

		public abstract void Connect(string url);

		public abstract void Disconnect();

		// public abstract void SendRequest(string message);

		public abstract void SendRequest(byte[] bytes);

		public abstract void SendRequest(ArraySegment<byte> data);

		public abstract void Ping(Action<bool> callback = null);

		protected abstract bool IsConnected();

		public virtual void Dispose()
		{
			this.OnConnectionOpened = null;
			this.OnConnectionClosed = null;
			this.ProcessMessage = null;

			Disconnect();
		}
	}

}