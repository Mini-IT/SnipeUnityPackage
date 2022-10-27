using System;
using System.Buffers;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class MessageBufferProvider
	{
		private Dictionary<string, int> mBufferSizes = new Dictionary<string, int>();
		private readonly object mDictionaryLock = new object();
		
		public byte[] GetBuffer(string message_type)
		{
			int buffer_size;
			if (!mBufferSizes.TryGetValue(message_type, out buffer_size))
			{
				buffer_size = 1024;
			}
			
			return ArrayPool<byte>.Shared.Rent(buffer_size);
		}
		
		public void ReturnBuffer(string message_type, byte[] buffer)
		{
			// if buffer.Length > mBytesPool's max bucket size (1024*1024 = 1048576)
			// then the buffer can not be returned to the pool. It will be dropped.
			// And ArgumentException will be thown.
			
			const int MAX_SIZE = 1048576;
			
			if (buffer.Length > MAX_SIZE)
			{
				lock (mDictionaryLock)
				{
					mBufferSizes[message_type] = MAX_SIZE;
				}
				return;
			}
			
			try
			{
				ArrayPool<byte>.Shared.Return(buffer);

				lock (mDictionaryLock)
				{
					if (mBufferSizes.TryGetValue(message_type, out int buffer_size) && buffer.Length > buffer_size)
					{
						mBufferSizes[message_type] = buffer.Length;
					}
				}
			}
			catch (Exception)
			{
				// ignore
			}
		}
	}
}