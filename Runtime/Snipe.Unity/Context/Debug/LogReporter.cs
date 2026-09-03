using System;
using Cysharp.Threading.Tasks;
using MiniIT.Snipe;
using UnityEngine;

namespace MiniIT
{
	public class LogReporter : ILogReporter
	{
		private readonly SnipeLogPipeline _pipeline = new SnipeLogPipeline();
		private bool _subscribed;

		public LogReporter()
		{
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
