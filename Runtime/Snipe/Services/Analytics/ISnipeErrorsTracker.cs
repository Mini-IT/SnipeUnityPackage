using System.Collections.Generic;

namespace MiniIT.Snipe.Debugging
{
	public interface ISnipeErrorsTracker
	{
		void TrackNotOk(IDictionary<string, object> properties);
	}
}
