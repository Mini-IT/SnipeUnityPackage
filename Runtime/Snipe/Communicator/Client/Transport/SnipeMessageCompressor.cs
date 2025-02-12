using System;
using System.IO;
using System.IO.Compression;

namespace MiniIT.Snipe
{
	public class SnipeMessageCompressor
	{
		private byte[] _decompressionBuffer;

		public byte[] Compress(ReadOnlySpan<byte> msgData)
		{
			using var stream = new MemoryStream();
			using (var deflate = new DeflateStream(stream, CompressionLevel.Fastest))
			{
				deflate.Write(msgData);
			}

			return stream.ToArray();
		}

		public ArraySegment<byte> Decompress(ArraySegment<byte> compressed)
		{
			int length = 0;

			using (var stream = new MemoryStream(compressed.Array, compressed.Offset, compressed.Count))
			{
				using (var deflate = new DeflateStream(stream, CompressionMode.Decompress))
				{
					const int PORTION_SIZE = 1024;

					while (deflate.CanRead)
					{
						if (_decompressionBuffer == null)
						{
							_decompressionBuffer = new byte[PORTION_SIZE];
						}
						else if (_decompressionBuffer.Length < length + PORTION_SIZE)
						{
							Array.Resize(ref _decompressionBuffer, length + PORTION_SIZE);
						}

						int bytesRead = deflate.Read(_decompressionBuffer, length, PORTION_SIZE);
						if (bytesRead == 0)
						{
							break;
						}

						length += bytesRead;
					}
				}
			}

			return new ArraySegment<byte>(_decompressionBuffer, 0, length);
		}
	}
}
