using System;
using System.Buffers;
using System.Collections.Generic;

namespace MiniIT.Snipe
{
	public class MessageBufferProvider
	{
		private Dictionary<string, int> _bufferSizes = new Dictionary<string, int>();
		private readonly object _dictionaryLock = new object();
		
		public byte[] GetBuffer(string message_type)
		{
			int buffer_size;
			if (!_bufferSizes.TryGetValue(message_type, out buffer_size))
			{
				buffer_size = 1024;
			}
			
			return ArrayPool<byte>.Shared.Rent(buffer_size);
		}

		public byte[] GetBuffer(int buffer_size)
		{
			return ArrayPool<byte>.Shared.Rent(buffer_size);
		}

		public void ReturnBuffer(byte[] buffer)
		{
			ReturnBuffer(null, buffer);
		}

		public void ReturnBuffer(string message_type, byte[] buffer)
		{
			// if buffer.Length > mBytesPool's max bucket size (1024*1024 = 1048576)
			// then the buffer can not be returned to the pool. It will be dropped.
			// And ArgumentException will be thown.
			
			const int MAX_SIZE = 1048576;
			
			if (buffer.Length > MAX_SIZE)
			{
				if (!string.IsNullOrEmpty(message_type))
				{
					lock (_dictionaryLock)
					{
						_bufferSizes[message_type] = MAX_SIZE;
					}
				}
				return;
			}
			
			try
			{
				ArrayPool<byte>.Shared.Return(buffer);

				if (!string.IsNullOrEmpty(message_type))
				{
					lock (_dictionaryLock)
					{
						if (_bufferSizes.TryGetValue(message_type, out int buffer_size) && buffer.Length > buffer_size)
						{
							_bufferSizes[message_type] = buffer.Length;
						}
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