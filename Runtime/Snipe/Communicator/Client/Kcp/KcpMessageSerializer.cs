using System;
using System.Runtime.CompilerServices;
using MiniIT.MessagePack;
using System.Threading.Tasks;

namespace MiniIT.Snipe
{
	internal class KcpMessageSerializer : IDisposable
	{
		private SnipeMessageCompressor mMessageCompressor;
		private MessageBufferProvider mMessageBufferProvider;

		private SnipeObject mMessage;
		private string mMessageType;
		private byte[] mBuffer;

		public KcpMessageSerializer(SnipeObject message, SnipeMessageCompressor compressor, MessageBufferProvider bufferProvider)
		{
			mMessage = message;
			mMessageCompressor = compressor;
			mMessageBufferProvider = bufferProvider;
		}

		public async Task<ArraySegment<byte>> Run()
		{
			mMessageType = mMessage.SafeGetString("t");

			mBuffer = mMessageBufferProvider.GetBuffer(mMessageType);

			// offset = opcode (1 byte) + length (4 bytes) = 5
			ArraySegment<byte> msg_data = await Task.Run(() => MessagePackSerializerNonAlloc.Serialize(ref mBuffer, 5, mMessage));

			if (SnipeConfig.CompressionEnabled && msg_data.Count >= SnipeConfig.MinMessageSizeToCompress) // compression needed
			{
				await Task.Run(() =>
				{
					DebugLogger.Log("[SnipeClient] compress message");
					// DebugLogger.Log("Uncompressed: " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));

					ArraySegment<byte> msg_content = new ArraySegment<byte>(mBuffer, 5, msg_data.Count - 5);
					ArraySegment<byte> compressed = mMessageCompressor.Compress(msg_content);

					mMessageBufferProvider.ReturnBuffer(mMessageType, mBuffer);
					mMessageType = null; // for correct disposing

					mBuffer = mMessageBufferProvider.GetBuffer(compressed.Count + 5);
					mBuffer[0] = KcpTransport.OPCODE_SNIPE_REQUEST_COMPRESSED;
					WriteInt(mBuffer, 1, compressed.Count + 4); // msg_data = opcode + length (4 bytes) + msg
					Array.ConstrainedCopy(compressed.Array, compressed.Offset, mBuffer, 5, compressed.Count);

					msg_data = new ArraySegment<byte>(mBuffer, 0, compressed.Count + 5);

					// DebugLogger.Log("Compressed:   " + BitConverter.ToString(msg_data.Array, msg_data.Offset, msg_data.Count));
				});
			}
			else // compression not needed
			{
				mBuffer[0] = KcpTransport.OPCODE_SNIPE_REQUEST;
				WriteInt(mBuffer, 1, msg_data.Count - 1); // msg_data.Count = opcode (1 byte) + length (4 bytes) + msg.Lenght
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
			data[position] = (byte)value;
			data[position + 1] = (byte)(value >> 8);
			data[position + 2] = (byte)(value >> 0x10);
			data[position + 3] = (byte)(value >> 0x18);
		}

		public void Dispose()
		{
			if (mBuffer != null)
			{
				mMessageBufferProvider?.ReturnBuffer(mMessageType, mBuffer);
				mBuffer = null;
			}
			mMessage = null;
		}
	}
}