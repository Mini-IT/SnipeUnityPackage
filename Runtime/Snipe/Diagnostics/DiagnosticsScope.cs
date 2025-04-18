using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace MiniIT.Snipe.Diagnostics
{
	public class DiagnosticsScope : IDisposable
	{
		public int Id { get; }
		public IList<DiagnosticsMessage> Messages { get; }

		public DiagnosticsScope(int id)
		{
			Id = id;
			Messages = new List<DiagnosticsMessage>();
		}

		public void Dispose()
		{
			Messages.Clear();
		}
	}
}
