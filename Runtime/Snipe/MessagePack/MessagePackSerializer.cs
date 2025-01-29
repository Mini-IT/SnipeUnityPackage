//
//  MessagePack serialization format specification can be found here:
//  https://github.com/msgpack/msgpack/blob/master/spec.md
//
//  This implementation is inspired by
//  https://github.com/ymofen/SimpleMsgPack.Net
//


using System.Buffers;
using System.Collections.Generic;

namespace MiniIT.MessagePack
{
	public static class MessagePackSerializer
	{
		private const int BUFFER_SIZE = 10240;

		private static MessagePackSerializerNonAlloc s_serializer;

		public static byte[] Serialize(Dictionary<string, object> data, bool throwUnsupportedType = true)
		{
			var pool = ArrayPool<byte>.Shared;
			byte[] buffer = pool.Rent(BUFFER_SIZE);

			try
			{
				s_serializer ??= new MessagePackSerializerNonAlloc();
				var result = s_serializer.Serialize(ref buffer, data, throwUnsupportedType);
				return result.ToArray();
			}
			finally
			{
				pool.Return(buffer);
			}
		}
	}
}
