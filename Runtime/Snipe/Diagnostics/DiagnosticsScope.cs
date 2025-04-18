using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Diagnostics
{
	public class DiagnosticsScope : IDisposable
	{
		public EventId EventId { get; }
		public IList<DiagnosticsMessage> Messages { get; }

		public DiagnosticsScope(EventId eventId)
		{
			EventId = eventId;
			Messages = new List<DiagnosticsMessage>();
		}

		public void Dispose()
		{
			Messages.Clear();
		}
	}
}
