using System;
using NUnit.Framework;

namespace MiniIT.Snipe.Tests.Editor
{
	public class TestMessageCompressor
	{
		[Test]
		public void TestCompress()
		{
			var data = new byte[1000];
			new Random().NextBytes(data);

			var compressor = new SnipeMessageCompressor();

			var compressed = compressor.Compress(data);

			Assert.IsNotEmpty(compressed);
		}

		[Test]
		public void TestCompressDecompress()
		{
			var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

			var compressor = new SnipeMessageCompressor();

			var compressed = compressor.Compress(data);
			var decompressed = compressor.Decompress(compressed);

			Assert.AreEqual(data.Length, decompressed.Count);
			Assert.AreEqual(data, decompressed.ToArray());
		}
	}
}
