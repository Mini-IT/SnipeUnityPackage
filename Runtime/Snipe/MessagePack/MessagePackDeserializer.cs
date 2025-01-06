//
//  MessagePack serialization format specification can be found here:
//  https://github.com/msgpack/msgpack/blob/master/spec.md
//
//  This implementation is inspired by
//  https://github.com/ymofen/SimpleMsgPack.Net
//


using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace MiniIT.MessagePack
{
	public static class MessagePackDeserializer
	{
		// cached encoding
		private static readonly Encoding ENCODING_UTF8 = Encoding.UTF8;

		public static object Parse(byte[] array) => Parse(array.AsSpan());

		public static object Parse(ArraySegment<byte> buffer) => Parse(buffer.AsSpan());

		public static object Parse(ReadOnlySpan<byte> input)
		{
			int position = 0;
			return Parse(input, ref position);
		}

		private static object Parse(ReadOnlySpan<byte> input, ref int position)
		{
			byte formatByte = input[position++];

			switch (formatByte)
			{
				case <= 0x7F: // positive fixint	0xxxxxxx	0x00 - 0x7f
				{
					return Convert.ToInt32(formatByte);
				}

				case <= 0x8F: // fixmap	1000xxxx	0x80 - 0x8f
				{
					int len = formatByte & 0b00001111;
					return ReadMap(input, ref position, len);
				}

				case <= 0x9F: // fixarray	1001xxxx	0x90 - 0x9f
				{
					int len = formatByte & 0b00001111;
					return ReadArray(input, ref position, len);
				}

				case <= 0xBF: // fixstr	101xxxxx	0xa0 - 0xbf
				{
					int len = formatByte & 0b00011111;
					return ReadString(input, ref position, len);
				}

				case >= 0xE0: // negative fixint	111xxxxx	0xe0 - 0xff (5-bit negative integer)
				{
					return Convert.ToInt32(unchecked((sbyte)formatByte));
				}

				case 0xC0:
				{
					return null;
				}

				//case 0xC1:
				//{
				//    throw new ArgumentException("(never used) 0xc1");
				//}

				//case 0xC7:
				//case 0xC8:
				//case 0xC9:
				//{
				//    throw new ArgumentException("(ext8, ext16, ex32) type 0xc7, 0xc8, 0xc9");
				//}

				case 0xC2:
				{
					return false;
				}

				case 0xC3:
				{
					return true;
				}

				case 0xC4: // bin 8
				{
					int len = input[position++];
					return ReadBytes(input, ref position, len);
				}

				case 0xC5: // bin 16
				{
					var rawBytes = InternalReadBytes(input, ref position, 2);

					int len = EndianBitConverter.Big.ToInt16(rawBytes);
					return ReadBytes(input, ref position, len);
				}

				case 0xC6: // bin 32
				{
					var rawBytes = InternalReadBytes(input, ref position, 4);
					int len = Convert.ToInt32(EndianBitConverter.Big.ToUInt32(rawBytes));
					return ReadBytes(input, ref position, len);
				}

				case 0xCA: // float 32
				{
					var rawBytes = InternalReadBytes(input, ref position, 4);
					return EndianBitConverter.Big.ToSingle(rawBytes);
				}

				case 0xCB: // float 64
				{
					var rawBytes = InternalReadBytes(input, ref position, 8);
					return EndianBitConverter.Big.ToDouble(rawBytes);
				}

				case 0xCC: // uint8
				{
					return Convert.ToInt32(input[position++]);
				}

				case 0xCD: // uint16
				{
					var rawBytes = InternalReadBytes(input, ref position, 2);
					return EndianBitConverter.Big.ToUInt16(rawBytes);
				}

				case 0xCE: // uint 32
				{
					var rawBytes = InternalReadBytes(input, ref position, 4);
					return EndianBitConverter.Big.ToUInt32(rawBytes);
				}

				case 0xCF: // uint 64
				{
					var rawBytes = InternalReadBytes(input, ref position, 8);
					return EndianBitConverter.Big.ToUInt64(rawBytes);
				}

				case 0xD9: // str 8
				case 0xDA: // str 16
				case 0xDB: // str 32
				{
					return ReadString(formatByte, input, ref position);
				}

				case 0xDC: // array 16
				{
					var rawBytes = InternalReadBytes(input, ref position, 2);
					int len = EndianBitConverter.Big.ToUInt16(rawBytes);
					return ReadArray(input, ref position, len);
				}

				case 0xDD: // array 32
				{
					var rawBytes = InternalReadBytes(input, ref position, 4);
					int len = EndianBitConverter.Big.ToInt32(rawBytes);
					return ReadArray(input, ref position, len);
				}

				case 0xDE: // map 16
				{
					var rawBytes = InternalReadBytes(input, ref position, 2);
					int len = EndianBitConverter.Big.ToUInt16(rawBytes);
					return ReadMap(input, ref position, len);
				}

				case 0xDF: // map 32
				{
					var rawBytes = InternalReadBytes(input, ref position, 4);
					int len = EndianBitConverter.Big.ToInt32(rawBytes);
					return ReadMap(input, ref position, len);
				}

				case 0xD0: // int 8
				{
					return Convert.ToInt32((sbyte)input[position++]);
				}

				case 0xD1: // int 16
				{
					var rawBytes = InternalReadBytes(input, ref position, 2);
					return Convert.ToInt32(EndianBitConverter.Big.ToInt16(rawBytes));
				}

				case 0xD2: // int 32
				{
					var rawBytes = InternalReadBytes(input, ref position, 4);
					return Convert.ToInt32(EndianBitConverter.Big.ToInt32(rawBytes));
				}

				case 0xD3: // int 64
				{
					var rawBytes = InternalReadBytes(input, ref position, 8);
					return EndianBitConverter.Big.ToInt64(rawBytes);
				}

				default:
				{
					return null;
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static ReadOnlySpan<byte> InternalReadBytes(ReadOnlySpan<byte> buffer, ref int position, int length)
		{
			var result = buffer.Slice(position, length);
			position += length;
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static byte[] ReadBytes(ReadOnlySpan<byte> input, ref int position, int len)
		{
			var rawBytes = InternalReadBytes(input, ref position, len);
			return rawBytes.ToArray();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static List<object> ReadArray(ReadOnlySpan<byte> input, ref int position, int len)
		{
			var data = new List<object>(len);
			for (int i = 0; i < len; i++)
			{
				data.Add(Parse(input, ref position));
			}
			return data;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static SnipeObject ReadMap(ReadOnlySpan<byte> input, ref int position, int len)
		{
			var data = new SnipeObject();
			for (int i = 0; i < len; i++)
			{
				string key = ReadString(input, ref position);
				data[key] = Parse(input, ref position);
			}
			return data;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static string ReadString(ReadOnlySpan<byte> input, ref int position, int len)
		{
			ReadOnlySpan<byte> data = InternalReadBytes(input, ref position, len);
			return ENCODING_UTF8.GetString(data);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static string ReadString(ReadOnlySpan<byte> input, ref int position)
		{
			byte flag = input[position++];
			return ReadString(flag, input, ref position);
		}

		private static string ReadString(byte flag, ReadOnlySpan<byte> input, ref int position)
		{
			int len = 0;
			if (flag >= 0xA0 && flag <= 0xBF)  // fixstr stores a byte array whose length is upto 31 bytes:
			{
				len = flag & 0b00011111;
			}
			else if (flag == 0xD9)             // str 8 stores a byte array whose length is upto (2^8)-1 bytes:
			{
				len = input[position++];
			}
			else if (flag == 0xDA)             // str 16 stores a byte array whose length is upto (2^16)-1 bytes:
			{
				var rawBytes = InternalReadBytes(input, ref position, 2);
				len = Convert.ToInt32(EndianBitConverter.Big.ToInt16(rawBytes));
			}
			else if (flag == 0xDB)             // str 32 stores a byte array whose length is upto (2^32)-1 bytes:
			{
				var rawBytes = InternalReadBytes(input, ref position, 4);
				len = Convert.ToInt32(EndianBitConverter.Big.ToUInt32(rawBytes));
			}

			return ReadString(input, ref position, len);
		}
	}
}
