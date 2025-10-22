using System.Collections.Generic;

namespace Snipe.Services.Analytics
{
	public interface ITestSnipeErrorTracker
	{
		public IReadOnlyList<IDictionary<string, object>> GetItems();
	}
}
