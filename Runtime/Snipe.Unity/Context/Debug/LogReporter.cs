using System;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe;
using UnityEngine;

namespace MiniIT
{
	public class LogReporter : ILogReporter
	{
		private readonly ISnipeLogPipeline _pipeline;
		private bool _subscribed;
		private bool _disposed;

		public LogReporter()
			: this(new SnipeLogPipeline())
		{
		}

		internal LogReporter(ISnipeLogPipeline pipeline)
		{
			_pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
			Application.logMessageReceivedThreaded += HandleLogMessageReceived;
			_subscribed = true;
		}

		public void Initialize(SnipeContext context, SnipeOptions options)
		{
			_pipeline.Initialize(context, options);
		}

		public UniTask<bool> SendAsync()
		{
			return _pipeline.SendAsync();
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			if (_subscribed)
			{
				Application.logMessageReceivedThreaded -= HandleLogMessageReceived;
				_subscribed = false;
			}

			_pipeline.Dispose();
		}

		private void HandleLogMessageReceived(string condition, string stackTrace, LogType type)
		{
			if (SnipeLogPipeline.IsDiagnosticLog(condition))
			{
				return;
			}

			_pipeline.Append(new SnipeLogRecord(
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				type,
				condition,
				stackTrace));
		}
	}
}
