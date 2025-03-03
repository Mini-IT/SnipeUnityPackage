using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class SnipeRequestDescriptor
	{
		public string MessageType;
		public IDictionary<string, object> Data;

		public static implicit operator SnipeRequestDescriptor(string messageType)
		{
			return new SnipeRequestDescriptor()
			{
				MessageType = messageType,
			};
		}
	}
}
