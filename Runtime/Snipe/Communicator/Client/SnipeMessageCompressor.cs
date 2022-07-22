using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MiniIT.Snipe
{
	public static class SnipeMessageCompressor
	{
		public static ArraySegment<byte> Compress(ArraySegment<byte> msg_data)
		{
			using (var stream = new MemoryStream())
			{
				using (GZipStream gzip = new GZipStream(stream, CompressionLevel.Fastest))
				{
					gzip.Write(msg_data.Array, msg_data.Offset, msg_data.Count);
				}

				return new ArraySegment<byte>(stream.ToArray());
			}
		}

		public static ArraySegment<byte> Decompress(ref byte[] buffer, ArraySegment<byte> compressed)
		{
			int length = 0;

			using (var stream = new MemoryStream(compressed.Array, compressed.Offset, compressed.Count))
			{
				using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
				{
					const int portion_size = 1024;
					while (gzip.CanRead)
					{
						if (buffer.Length < length + portion_size)
							Array.Resize(ref buffer, length + portion_size);

						int bytes_read = gzip.Read(buffer, length, portion_size);
						if (bytes_read == 0)
							break;

						length += bytes_read;
					}
				}
			}

			return new ArraySegment<byte>(buffer, 0, length);
		}
	}
}