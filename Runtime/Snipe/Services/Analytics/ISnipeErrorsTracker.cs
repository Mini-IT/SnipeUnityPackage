using System.Collections.Generic;

namespace MiniIT.Snipe.Debugging
{
	public interface ISnipeErrorsTracker
	{
		public List<IDictionary<string, object>> Items { get; }
		void TrackNotOk(IDictionary<string, object> properties);
		void Clear();
	}
}
