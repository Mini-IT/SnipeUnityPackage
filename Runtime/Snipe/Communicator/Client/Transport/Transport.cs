using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MiniIT.MessagePack;
using MiniIT.Threading;

namespace MiniIT.Snipe
{
	public enum TransportProtocol
	{
		Undefined,
		Kcp,
		WebSocket,
		Http,
	}

	public struct TransportInfo
	{
		public TransportProtocol Protocol;
		public string ClientImplementation;
	}

	public abstract class Transport : IDisposable
	{
		public Action<Transport> ConnectionOpenedHandler;
		public Action<Transport> ConnectionClosedHandler;
		public Action<SnipeObject> MessageReceivedHandler;

		public virtual bool Started { get; } = false;
		public virtual bool Connected { get; } = false;
		public virtual bool ConnectionEstablished { get; } = false;
		public virtual TransportInfo Info { get; protected set; }

		protected readonly SnipeConfig _config;
		protected readonly SnipeAnalyticsTracker _analytics;
		protected readonly ILogger _logger;

		internal Transport(SnipeConfig config, SnipeAnalyticsTracker analytics)
		{
			_config = config;
			_analytics = analytics;
			_logger = SnipeServices.LogService.GetLogger(GetType().Name);
		}

		public abstract void Connect();
		public abstract void Disconnect();
		public abstract void SendMessage(SnipeObject message);
		public abstract void SendBatch(IList<SnipeObject> messages);

		protected readonly SnipeMessageCompressor _messageCompressor = new SnipeMessageCompressor();
		protected readonly MessagePackSerializer _messageSerializer = new MessagePackSerializer();

		protected readonly AlterSemaphore _messageSerializationSemaphore = new AlterSemaphore(1, 1);
		protected readonly AlterSemaphore _messageProcessingSemaphore = new AlterSemaphore(1, 1);

		public virtual void Dispose()
		{
			Disconnect();
			ConnectionOpenedHandler = null;
			ConnectionClosedHandler = null;
			MessageReceivedHandler = null;
			_messageSerializationSemaphore.Dispose();
			_messageProcessingSemaphore.Dispose();
		}
	}
}
