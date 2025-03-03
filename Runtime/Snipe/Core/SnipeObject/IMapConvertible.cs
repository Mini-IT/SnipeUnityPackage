
using System.Collections.Generic;

namespace MiniIT
{
	public interface IMapConvertible
	{
		IDictionary<string, object> ToMap();
	}
}
