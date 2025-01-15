using System;
using System.Runtime.CompilerServices;

namespace MiniIT.Snipe
{
	public static class BytesUtil
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void WriteInt(byte[] buffer, int offset, int value)
		{
			buffer[offset + 0] = (byte)(value);
			buffer[offset + 1] = (byte)(value >> 0x08);
			buffer[offset + 2] = (byte)(value >> 0x10);
			buffer[offset + 3] = (byte)(value >> 0x18);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void WriteInt(Span<byte> buffer, int offset, int value)
		{
			buffer[offset + 0] = (byte)(value);
			buffer[offset + 1] = (byte)(value >> 0x08);
			buffer[offset + 2] = (byte)(value >> 0x10);
			buffer[offset + 3] = (byte)(value >> 0x18);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void WriteInt3(byte[] buffer, int offset, int value)
		{
			buffer[offset + 0] = (byte)(value);
			buffer[offset + 1] = (byte)(value >> 0x08);
			buffer[offset + 2] = (byte)(value >> 0x10);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void WriteInt3(Span<byte> buffer, int offset, int value)
		{
			buffer[offset + 0] = (byte)(value);
			buffer[offset + 1] = (byte)(value >> 0x08);
			buffer[offset + 2] = (byte)(value >> 0x10);
		}
	}
}
