//
//  MessagePack serialization format specification can be found here:
//  https://github.com/msgpack/msgpack/blob/master/spec.md
//
//  This implementation is inspired by
//  https://github.com/ymofen/SimpleMsgPack.Net
//


using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MiniIT.MessagePack
{
	public class MessagePackSerializerNonAlloc
	{
		private const int STRING_ENCODING_BUFFER_PORTION = 1024;

		private readonly bool _throwUnsupportedType;
		private readonly IBitConverter _bigEndianConverter;
		private readonly Encoding _utf8 = Encoding.UTF8;

		private int _position;
		private byte[] _buffer;
		private byte[] _stringEncodingBuffer;

		public MessagePackSerializerNonAlloc(int initialBufferSize = 10240, bool throwUnsupportedType = true)
		{
			_buffer = new byte[initialBufferSize];
			_stringEncodingBuffer = new byte[STRING_ENCODING_BUFFER_PORTION];
			_throwUnsupportedType = throwUnsupportedType;
			_bigEndianConverter = EndianBitConverter.Big;
		}

		public ArraySegment<byte> GetBufferSegment(int length)
		{
			return new ArraySegment<byte>(_buffer, 0, length);
		}

		public Span<byte> Serialize(Dictionary<string, object> data)
		{
			return Serialize(0, data);
		}

		public Span<byte> Serialize(int position, Dictionary<string, object> data)
		{
			_position = position;
			Span<byte> bufferSpan = _buffer.AsSpan();
			DoSerialize(bufferSpan, data);
			return _buffer.AsSpan(0, _position);
		}

		private void DoSerialize<T>(Span<byte> bufferSpan, T val)
		{
			bufferSpan = EnsureBufferCapacity(bufferSpan, 1);

			if (val == null)
			{
				bufferSpan[_position++] = (byte)0xC0;
				return;
			}

			switch (val)
			{
				case string str:
					WriteString(bufferSpan, str);
					break;
				case IDictionary map:
					WriteMap(bufferSpan, map);
					break;
				case byte[] data:
					WriteBinary(bufferSpan, data);
					break;
				case IList list:
					WirteArray(bufferSpan, list);
					break;
				case ISnipeObjectConvertable soc:
					WriteMap(bufferSpan, soc.ConvertToSnipeObject());
					break;
				default:
					WriteInternalValueType(bufferSpan, val);
					break;
			}
		}

		private void WriteInternalValueType(Span<byte> bufferSpan, object val)
		{
			switch (Type.GetTypeCode(val.GetType()))
			{
				case TypeCode.UInt64:
					WriteInteger(bufferSpan, (ulong)val);
					return;

				case TypeCode.Byte:
				case TypeCode.SByte:
				case TypeCode.UInt16:
				case TypeCode.UInt32:
				case TypeCode.Int16:
				case TypeCode.Int32:
				case TypeCode.Int64:
				case TypeCode.Char:
					WriteInteger(bufferSpan, Convert.ToInt64(val));
					return;

				case TypeCode.Single:
					bufferSpan[_position++] = (byte)0xCA;
					CopyBytes(bufferSpan, _bigEndianConverter.GetBytes((float)val), 4);
					return;

				case TypeCode.Double:
				case TypeCode.Decimal:
					bufferSpan[_position++] = (byte)0xCB;
					CopyBytes(bufferSpan, _bigEndianConverter.GetBytes(Convert.ToDouble(val)), 8);
					return;

				case TypeCode.Boolean:
					bufferSpan[_position++] = (bool)val ? (byte)0xC3 : (byte)0xC2;
					return;
			}

			if (_throwUnsupportedType)
			{
				throw new MessagePackSerializationUnsupportedTypeException();
			}
		}

		private void WriteString(Span<byte> bufferSpan, string str)
		{
			int encodedBytesCount = _utf8.GetByteCount(str);

			if (_stringEncodingBuffer.Length < encodedBytesCount)
			{
				int portions = encodedBytesCount / STRING_ENCODING_BUFFER_PORTION +
				               (encodedBytesCount % STRING_ENCODING_BUFFER_PORTION == 0 ? 0 : 1);
				int bufferSize = portions * STRING_ENCODING_BUFFER_PORTION;
				Array.Resize(ref _stringEncodingBuffer, bufferSize);
			}

			Span<byte> rawBytes = _stringEncodingBuffer.AsSpan(0, encodedBytesCount);
			_ = _utf8.GetBytes(str, rawBytes);
			int len = rawBytes.Length;

			bufferSpan = EnsureBufferCapacity(bufferSpan, len + 5);

			if (len <= 31)
			{
				bufferSpan[_position++] = (byte)(0xA0 | len);
			}
			else if (len <= 0xFF)
			{
				bufferSpan[_position++] = (byte)0xD9;
				bufferSpan[_position++] = (byte)len;
			}
			else if (len <= 0xFFFF)
			{
				bufferSpan[_position++] = (byte)0xDA;
				CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes(Convert.ToUInt16(len)), 2);
			}
			else
			{
				bufferSpan[_position++] = (byte)0xDB;
				CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes(Convert.ToUInt32(len)), 4);
			}

			CopyBytesUnsafe(bufferSpan, rawBytes, len);
		}

		private void WriteMap(Span<byte> bufferSpan, IDictionary map)
		{
			int len = map.Count;

			bufferSpan = EnsureBufferCapacity(bufferSpan, len + 5);

			if (len <= 0x0F)
			{
				bufferSpan[_position++] = (byte)(0x80 | len);
			}
			else if (len <= 0xFFFF)
			{
				bufferSpan[_position++] = (byte)0xDE;
				CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes((UInt16)len), 2);
			}
			else
			{
				bufferSpan[_position] = (byte)0xDF;
				CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes((UInt32)len), 4);
			}

			var enumerator = map.GetEnumerator();

			while (enumerator.MoveNext())
			{
				var item = enumerator.Entry;
				DoSerialize(bufferSpan, item.Key);
				DoSerialize(bufferSpan, item.Value);
			}

			if (enumerator is IDisposable disposable)
			{
				disposable.Dispose();
			}
		}

		private void WirteArray(Span<byte> bufferSpan, IList list)
		{
			int len = list.Count;

			bufferSpan = EnsureBufferCapacity(bufferSpan, len + 5);

			if (len <= 0x0F)
			{
				bufferSpan[_position++] = (byte)(0x90 | len);
			}
			else if (len <= 0xFFFF)
			{
				bufferSpan[_position++] = (byte)0xDC;
				CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes((UInt16)len), 2);
			}
			else
			{
				bufferSpan[_position++] = (byte)0xDD;
				CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes((UInt32)len), 4);
			}

			Type listType = list.GetType();

			if (listType.IsArray)
			{
				if (TryWriteArrayItems(bufferSpan, list))
				{
					return;
				}
			}
			else if (listType.IsGenericType)
			{
				if (TryWriteGenericListItems(bufferSpan, list))
				{
					return;
				}
			}

			for (int i = 0; i < len; i++)
			{
				DoSerialize(bufferSpan, list[i]);
			}
		}

		private bool TryWriteArrayItems(Span<byte> bufferSpan, IList list)
		{
			switch (list)
			{
				case int[] intArray:
					WriteArrayItems(bufferSpan, intArray);
					return true;
				case uint[] uintArray:
					WriteArrayItems(bufferSpan, uintArray);
					return true;
				case char[] charArray:
					WriteArrayItems(bufferSpan, charArray);
					return true;
				case byte[] byteArray:
					WriteArrayItems(bufferSpan, byteArray);
					return true;
				case short[] shortArray:
					WriteArrayItems(bufferSpan, shortArray);
					return true;
				case ushort[] ushortArray:
					WriteArrayItems(bufferSpan, ushortArray);
					return true;
				case long[] longArray:
					WriteArrayItems(bufferSpan, longArray);
					return true;
				case ulong[] ulongArray:
					WriteArrayItems(bufferSpan, ulongArray);
					return true;
				case float[] floatArray:
					WriteArrayItems(bufferSpan, floatArray);
					return true;
				case double[] doubleArray:
					WriteArrayItems(bufferSpan, doubleArray);
					return true;
				case decimal[] decimalArray:
					WriteArrayItems(bufferSpan, decimalArray);
					return true;
				case bool[] boolArray:
					WriteArrayItems(bufferSpan, boolArray);
					return true;
			}

			return false;
		}

		private bool TryWriteGenericListItems(Span<byte> bufferSpan, IList list)
		{
			// CollectionsMarshal.AsSpan is not available in Unity

			switch (list)
			{
				case List<int> intList:
					WriteListItems(bufferSpan, intList);
					return true;
				case List<uint> uintList:
					WriteListItems(bufferSpan, uintList);
					return true;
				case List<char> charList:
					WriteListItems(bufferSpan, charList);
					return true;
				case List<byte> byteList:
					WriteListItems(bufferSpan, byteList);
					return true;
				case List<short> shortList:
					WriteListItems(bufferSpan, shortList);
					return true;
				case List<ushort> ushortList:
					WriteListItems(bufferSpan, ushortList);
					return true;
				case List<long> longList:
					WriteListItems(bufferSpan, longList);
					return true;
				case List<ulong> ulongList:
					WriteListItems(bufferSpan, ulongList);
					return true;
				case List<float> floatList:
					WriteListItems(bufferSpan, floatList);
					return true;
				case List<double> doubleList:
					WriteListItems(bufferSpan, doubleList);
					return true;
				case List<decimal> decimalList:
					WriteListItems(bufferSpan, decimalList);
					return true;
				case List<bool> boolList:
					WriteListItems(bufferSpan, boolList);
					return true;
			}

			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void WriteArrayItems<T>(Span<byte> bufferSpan, T[] list)
		{
			for (int i = 0; i < list.Length; i++)
			{
				DoSerialize(bufferSpan, list[i]);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void WriteListItems<T>(Span<byte> bufferSpan, IList<T> list)
		{
			foreach (var item in list)
			{
				DoSerialize(bufferSpan, item);
			}
		}

		private void WriteBinary(Span<byte> bufferSpan, byte[] data)
		{
			int len = data.Length;

			bufferSpan = EnsureBufferCapacity(bufferSpan, len + 5);

			if (len <= 0xFF)
			{
				bufferSpan[_position++] = (byte)0xC4;
				bufferSpan[_position++] = (byte)len;
			}
			else if (len <= 0xFFFF)
			{
				bufferSpan[_position++] = (byte)0xC5;
				CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes(Convert.ToUInt16(len)), 2);
			}
			else
			{
				bufferSpan[_position++] = (byte)0xC6;
				CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes(Convert.ToUInt32(len)), 4);
			}

			CopyBytesUnsafe(bufferSpan, data, len);
		}

		private void WriteInteger(Span<byte> bufferSpan, ulong val) // uint 64
		{
			bufferSpan = EnsureBufferCapacity(bufferSpan, 9);

			bufferSpan[_position++] = (byte)0xCF;

			var bytes = _bigEndianConverter.GetBytes(val);
			CopyBytesUnsafe(bufferSpan, bytes, bytes.Length);
		}

		private void WriteInteger(Span<byte> bufferSpan, long val)
		{
			bufferSpan = EnsureBufferCapacity(bufferSpan, 9);

			if (val >= 0)
			{
				if (val <= 0x7F)  // positive fixint
				{
					bufferSpan[_position++] = (byte)val;
				}
				else if (val <= 0xFF)  // uint 8
				{
					bufferSpan[_position++] = (byte)0xCC;
					bufferSpan[_position++] = (byte)val;
				}
				else if (val <= 0xFFFF)  // uint 16
				{
					bufferSpan[_position++] = (byte)0xCD;
					CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes((UInt16)val), 2);
				}
				else if (val <= 0xFFFFFFFF)  // uint 32
				{
					bufferSpan[_position++] = (byte)0xCE;
					CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes((UInt32)val), 4);
				}
				else // signed int 64
				{
					bufferSpan[_position++] = (byte)0xD3;
					CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes(val), 8);
				}
			}
			else
			{
				if (val <= Int32.MinValue)  // int 64
				{
					bufferSpan[_position++] = (byte)0xD3;
					CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes(val), 8);
				}
				else if (val <= Int16.MinValue)  // int 32
				{
					bufferSpan[_position++] = (byte)0xD2;
					CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes((Int32)val), 4);
				}
				else if (val <= -128)  // int 16
				{
					bufferSpan[_position++] = (byte)0xD1;
					CopyBytesUnsafe(bufferSpan, _bigEndianConverter.GetBytes((Int16)val), 2);
				}
				else if (val <= -32)  // int 8
				{
					bufferSpan[_position++] = (byte)0xD0;
					bufferSpan[_position++] = (byte)val;
				}
				else  // negative fixint (5-bit)
				{
					bufferSpan[_position++] = (byte)val;
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void CopyBytes(Span<byte> bufferSpan, Span<byte> data, int length)
		{
			bufferSpan = EnsureBufferCapacity(bufferSpan, length);
			CopyBytesUnsafe(bufferSpan, data, length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void CopyBytesUnsafe(Span<byte> bufferSpan, Span<byte> data, int length)
		{
			data.CopyTo(bufferSpan.Slice(_position));
			_position += length;
		}

		private Span<byte> EnsureBufferCapacity(Span<byte> span, int additionalLenght)
		{
			int length = _position + additionalLenght;

			if (_buffer.Length >= length)
			{
				return span;
			}

			int capacity = Math.Max(length, _buffer.Length * 2);
			Array.Resize(ref _buffer, capacity);

			return _buffer.AsSpan();
		}
	}
}
