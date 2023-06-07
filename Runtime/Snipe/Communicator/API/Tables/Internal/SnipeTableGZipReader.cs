using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MiniIT.Snipe.Tables
{
	public class SnipeTableGZipReader
	{
		public static async Task<bool> TryReadAsync(Type wrapperType, IDictionary items, Stream stream)
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

		public static async Task ReadAsync(Type wrapperType, IDictionary items, Stream stream)
		{
			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress))
			{
				using (StreamReader reader = new StreamReader(gzip))
				{
					string json = await reader.ReadToEndAsync();
					SnipeTableParser.Parse(wrapperType, items, json);
				}
			}
		}
	}
}
