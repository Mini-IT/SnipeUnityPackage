using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MiniIT.MessagePack;
using MiniIT.Threading;

namespace MiniIT.Snipe
{
	public struct TransportOptions
	{
		public SnipeOptions SnipeOptions;
		public IAnalyticsContext AnalyticsContext;
		public ISnipeServices SnipeServices;
		public Action<Transport> ConnectionOpenedHandler;
		public Action<Transport> ConnectionClosedHandler;
		public Action<IDictionary<string, object>> MessageReceivedHandler;
	}

	public abstract class Transport : IDisposable
	{
		public virtual bool Started { get; } = false;
		public virtual bool Connected { get; } = false;
		public virtual bool ConnectionEstablished { get; } = false;

		public TransportInfo Info { get; protected set; }

		protected readonly SnipeOptions _snipeOptions;
		protected readonly IAnalyticsContext _analytics;
		protected readonly ILogger _logger;
		protected readonly ISnipeServices _services;

		protected Action<Transport> _connectionOpenedHandler;
		protected Action<Transport> _connectionClosedHandler;
		protected Action<IDictionary<string, object>> _messageReceivedHandler;

		protected bool _disposed = false;

		internal Transport(TransportOptions options)
		{
			if (options.SnipeServices == null)
			{
				throw new ArgumentNullException(nameof(options.SnipeServices));
			}

			_snipeOptions = options.SnipeOptions;
			_analytics = options.AnalyticsContext;
			_services = options.SnipeServices;
			_logger = _services.LogService.GetLogger(GetType().Name);

			_connectionOpenedHandler = options.ConnectionOpenedHandler;
			_connectionClosedHandler = options.ConnectionClosedHandler;
			_messageReceivedHandler = options.MessageReceivedHandler;
		}

		public abstract void Connect(string endpoint, ushort port = 0);
		public abstract void Disconnect();
		public abstract void SendMessage(IDictionary<string, object> message);
		public abstract void SendBatch(IList<IDictionary<string, object>> messages);

		protected readonly SnipeMessageCompressor _messageCompressor = new SnipeMessageCompressor();
		protected readonly MessagePackSerializer _messageSerializer = new MessagePackSerializer();

		protected readonly AlterSemaphore _messageSerializationSemaphore = new AlterSemaphore(1, 1);
		protected readonly AlterSemaphore _messageProcessingSemaphore = new AlterSemaphore(1, 1);

		public virtual void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;

			// Remove connection events handlers before calling Disconnect()
			// to prevent attempts to start next transport on disconnection
			_connectionOpenedHandler = null;
			_connectionClosedHandler = null;
			_messageReceivedHandler = null;

			Disconnect();

			_messageSerializationSemaphore.Dispose();
			_messageProcessingSemaphore.Dispose();
		}
	}
}
