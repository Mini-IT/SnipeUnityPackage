using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using Cysharp.Threading.Tasks;

namespace MiniIT.Snipe.Tables
{
	public static class SnipeTableGZipReader
	{
		public static async UniTask<bool> TryReadAsync(Type wrapperType, IDictionary items, Stream stream)
		{
			try
			{
				await ReadAsync(wrapperType, items, stream);
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		public static bool TryRead(Type wrapperType, IDictionary items, Stream stream)
		{
			try
			{
				Read(wrapperType, items, stream);
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		public static async UniTask ReadAsync(Type wrapperType, IDictionary items, Stream stream)
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress, true))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json = await reader.ReadToEndAsync();
					SnipeTableParser.Parse(wrapperType, items, json);
				}
			}
		}

		public static void Read(Type wrapperType, IDictionary items, Stream stream)
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress, true))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json = reader.ReadToEnd();
					SnipeTableParser.Parse(wrapperType, items, json);
				}
			}
		}
	}
}
