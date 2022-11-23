using System;
using System.Runtime.CompilerServices;
using MiniIT.MessagePack;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	internal class KcpMessageSerializer : IDisposable
	{
		private SnipeMessageCompressor _messageCompressor;
		private MessageBufferProvider _messageBufferProvider;

		private SnipeObject _message;
		private string _messageType;
		private byte[] _buffer;

		public KcpMessageSerializer(SnipeObject message, SnipeMessageCompressor compressor, MessageBufferProvider bufferProvider)
		{
			_message = message;
			_messageCompressor = compressor;
			_messageBufferProvider = bufferProvider;
		}

		public async Task<ArraySegment<byte>> Run(bool writeLength = true)
		{
			_messageType = _message.SafeGetString("t");

			_buffer = _messageBufferProvider.GetBuffer(_messageType);

			int offset = writeLength ?
				5 : // = opcode (1 byte) + length (4 bytes)
				1;  // = opcode (1 byte)
			ArraySegment<byte> msg_data = await Task.Run(() => MessagePackSerializerNonAlloc.Serialize(ref _buffer, offset, _message));

			if (SnipeConfig.CompressionEnabled && msg_data.Count >= SnipeConfig.MinMessageSizeToCompress) // compression needed
			{
				await Task.Run(() =>
				{
					DebugLogger.Log("[SnipeClient] compress message");
					// DebugLogger.Log("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

					ArraySegment<byte> msg_content = new ArraySegment<byte>(_buffer, offset, msg_data.Count - offset);
					ArraySegment<byte> compressed = _messageCompressor.Compress(msg_content);

					_messageBufferProvider.ReturnBuffer(_messageType, _buffer);
					_messageType = null; // for correct disposing

					_buffer = _messageBufferProvider.GetBuffer(compressed.Count + offset);
					_buffer[0] = (byte)KcpOpCodes.SnipeRequestCompressed;

					if (writeLength)
					{
						WriteInt(_buffer, 1, compressed.Count + 4); // msg_data = opcode + length (4 bytes) + msg
					}
					Array.ConstrainedCopy(compressed.Array, compressed.Offset, _buffer, offset, compressed.Count);

					msg_data = new ArraySegment<byte>(_buffer, 0, compressed.Count + offset);

					// DebugLogger.Log("Compressed:   " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));
				});
			}
			else // compression not needed
			{
				_buffer[0] = (byte)KcpOpCodes.SnipeRequest;
				if (writeLength)
				{
					WriteInt(_buffer, 1, msg_data.Count - 1); // msg_data.Count = opcode (1 byte) + length (4 bytes) + msg.Lenght
				}
			}

			return msg_data;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void WriteInt(byte[] data, int position, int value)
		{
			//unsafe
			//{
			//	fixed (byte* dataPtr = &data[position])
			//	{
			//		int* valuePtr = (int*)dataPtr;
			//		*valuePtr = value;
			//	}
			//}
			data[position + 0] = (byte)(value);
			data[position + 1] = (byte)(value >> 8);
			data[position + 2] = (byte)(value >> 0x10);
			data[position + 3] = (byte)(value >> 0x18);
		}

		public void Dispose()
		{
			if (_buffer != null)
			{
				_messageBufferProvider?.ReturnBuffer(_messageType, _buffer);
				_buffer = null;
			}
			_message = null;
		}
	}
}