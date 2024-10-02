using System;
using System.Collections.Generic;
using MiniIT.Snipe;
using MiniIT.Snipe.Internal;
using MiniIT.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace MiniIT
{
	public class LogReporter : IDisposable
	{
		internal class LogRecord
		{
			internal long _time;
			internal string _type;
			internal string _message;
			internal string _stackTrace;
		}

		private static readonly List<LogRecord> s_log = new List<LogRecord>();
		private static readonly AlterSemaphore s_semaphore = new AlterSemaphore(1, 1);

		private readonly LogSender _sender;

		static LogReporter()
		{
			Application.logMessageReceivedThreaded += OnLogMessageReceived;
		}

		public LogReporter()
		{
			_sender = new LogSender(s_semaphore);
		}

		internal void SetSnipeContext(SnipeContext snipeContext)
		{
			_sender.SetSnipeContext(snipeContext);
		}

		public async UniTask<bool> SendAsync()
		{
			return await _sender.SendAsync(s_log);
		}

		private static async void OnLogMessageReceived(string condition, string stackTrace, LogType type)
		{
			bool semaphoreOccupied = false;

			try
			{
				await s_semaphore.WaitAsync();
				semaphoreOccupied = true;

				s_log.Add(new LogRecord()
				{
					_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
					_type = type.ToString(),
					_message = condition,
					_stackTrace = stackTrace,
				});
			}
			finally
			{
				if (semaphoreOccupied)
				{
					s_semaphore.Release();
				}
			}
		}

		public void Dispose()
		{
			_sender.Dispose();
		}
	}
}
