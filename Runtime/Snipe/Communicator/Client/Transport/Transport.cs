using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using MiniIT.MessagePack;

namespace MiniIT.Snipe
{
	public abstract class Transport
	{
		public Action ConnectionOpenedHandler;
		public Action ConnectionClosedHandler;
		public Action<SnipeObject> MessageReceivedHandler;

		public virtual bool Started { get; } = false;
		public virtual bool Connected { get; } = false;

		protected readonly SnipeConfig _config;
		protected readonly Analytics _analytics;
		protected readonly ILogger _logger;

		internal Transport(SnipeConfig config, Analytics analytics)
		{
			_config = config;
			_analytics = analytics;
			_logger = SnipeServices.Instance.LogService.GetLogger(GetType().Name);
		}

		public abstract void Connect();
		public abstract void Disconnect();
		public abstract void SendMessage(SnipeObject message);
		public abstract void SendBatch(List<SnipeObject> messages);

		protected readonly SnipeMessageCompressor _messageCompressor = new SnipeMessageCompressor();
		protected readonly MessagePackSerializerNonAlloc _messageSerializer = new MessagePackSerializerNonAlloc();
		protected byte[] _messageSerializationBuffer = new byte[10240];

		protected readonly SemaphoreSlim _messageSerializationSemaphore = new SemaphoreSlim(1, 1);
		protected readonly SemaphoreSlim _messageProcessingSemaphore = new SemaphoreSlim(1, 1);
	}
}
