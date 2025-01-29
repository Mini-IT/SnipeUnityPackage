using System;
using System.Runtime.Serialization;

namespace MiniIT.MessagePack
{
	public class MessagePackException : Exception
	{
		public MessagePackException()
		{
		}

		public MessagePackException(string message) : base(message)
		{
		}

		public MessagePackException(string message, Exception innerException) : base(message, innerException)
		{
		}

		protected MessagePackException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
		}
	}

	public class MessagePackSerializationUnsupportedTypeException : MessagePackException
	{
	}
}
