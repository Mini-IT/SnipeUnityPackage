// from https://github.com/caesay/CS.Util/blob/master/CS.Util/EndianBitConverter.cs
using System;
using System.Runtime.CompilerServices;

namespace MiniIT
{
    internal interface IBitConverter
    {
        byte[] GetBytes(bool value);
        byte[] GetBytes(char value);
        byte[] GetBytes(short value);
        byte[] GetBytes(int value);
        byte[] GetBytes(long value);
        byte[] GetBytes(ushort value);
        byte[] GetBytes(uint value);
        byte[] GetBytes(ulong value);
        byte[] GetBytes(float value);
        byte[] GetBytes(double value);

        char ToChar(ReadOnlySpan<byte> value);
        short ToInt16(ReadOnlySpan<byte> value);
        int ToInt32(ReadOnlySpan<byte> value);
        long ToInt64(ReadOnlySpan<byte> value);
        ushort ToUInt16(ReadOnlySpan<byte> value);
        uint ToUInt32(ReadOnlySpan<byte> value);
        ulong ToUInt64(ReadOnlySpan<byte> value);
        float ToSingle(ReadOnlySpan<byte> value);
        double ToDouble(ReadOnlySpan<byte> value);
        bool ToBoolean(ReadOnlySpan<byte> value);
    }

    internal sealed class BigEndianBitConverter : EndianBitConverter
    {
        public override bool IsLittleEndian => false;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override long FromBytes(ReadOnlySpan<byte> buffer, int len)
        {
            long ret = 0;
            for (int i = 0; i < len; i++)
            {
                ret = unchecked((ret << 8) | buffer[i]);
            }
            return ret;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override byte[] ToBytesImpl(long value, int len)
        {
            byte[] buffer = new byte[len];
            int endOffset = len - 1;
            for (int i = 0; i < len; i++)
            {
                buffer[endOffset - i] = unchecked((byte)(value & 0xff));
                value >>= 8;
            }
            return buffer;
        }
    }

    internal sealed class LittleEndianBitConverter : EndianBitConverter
    {
        public override bool IsLittleEndian => true;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override long FromBytes(ReadOnlySpan<byte> buffer, int len)
        {
            long ret = 0;
            for (int i = 0; i < len; i++)
            {
                ret = unchecked((ret << 8) | buffer[len - 1 - i]);
            }
            return ret;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override byte[] ToBytesImpl(long value, int len)
        {
            byte[] buffer = new byte[len];
            for (int i = 0; i < len; i++)
            {
                buffer[i] = unchecked((byte)(value & 0xff));
                value >>= 8;
            }
            return buffer;
        }
    }

    internal abstract class EndianBitConverter : IBitConverter
    {
        public abstract bool IsLittleEndian { get; }

        public static IBitConverter Big => _big ??= new BigEndianBitConverter();
        public static IBitConverter Little => _little ??= new LittleEndianBitConverter();

        private static BigEndianBitConverter _big;
        private static LittleEndianBitConverter _little;

        protected EndianBitConverter() { }

        protected abstract long FromBytes(ReadOnlySpan<byte> buffer, int bytes);
        
        protected byte[] ToBytes(long value, int bytes)
        {
            return ToBytesImpl(value, bytes);
        }
        protected abstract byte[] ToBytesImpl(long value, int len);

        public byte[] GetBytes(bool value)
        {
            return BitConverter.GetBytes(value);
        }
        public byte[] GetBytes(char value)
        {
            return ToBytes(value, 2);
        }
        public byte[] GetBytes(short value)
        {
            return ToBytes(value, 2);
        }
        public byte[] GetBytes(int value)
        {
            return ToBytes(value, 4);
        }
        public byte[] GetBytes(long value)
        {
            return ToBytes(value, 8);
        }
        public byte[] GetBytes(ushort value)
        {
            return ToBytes(value, 2);
        }
        public byte[] GetBytes(uint value)
        {
            return ToBytes(value, 4);
        }
        public byte[] GetBytes(ulong value)
        {
            return ToBytes(unchecked((long)value), 8);
        }
        public unsafe byte[] GetBytes(float value)
        {
            return GetBytes(*(int*)&value);
        }
        public unsafe byte[] GetBytes(double value)
        {
            return GetBytes(*(long*)&value);
        }

        public char ToChar(ReadOnlySpan<byte> value)
        {
            return unchecked((char)(FromBytes(value, 2)));
        }
        public short ToInt16(ReadOnlySpan<byte> value)
        {
            return unchecked((short)(FromBytes(value, 2)));
        }
        public int ToInt32(ReadOnlySpan<byte> value)
        {
            return unchecked((int)(FromBytes(value, 4)));
        }
        public long ToInt64(ReadOnlySpan<byte> value)
        {
            return FromBytes(value, 8);
        }
        public ushort ToUInt16(ReadOnlySpan<byte> value)
        {
            return unchecked((ushort)(FromBytes(value, 2)));
        }
        public uint ToUInt32(ReadOnlySpan<byte> value)
        {
            return unchecked((uint)(FromBytes(value, 4)));
        }
        public ulong ToUInt64(ReadOnlySpan<byte> value)
        {
            return unchecked((ulong)(FromBytes(value, 8)));
        }
        public unsafe float ToSingle(ReadOnlySpan<byte> value)
        {
            int val = ToInt32(value);
            return *(float*)&val;
        }
        public unsafe double ToDouble(ReadOnlySpan<byte> value)
        {
            long val = ToInt64(value);
            return *(double*)&val;
        }
        public bool ToBoolean(ReadOnlySpan<byte> value)
        {
            return BitConverter.ToBoolean(value);
        }
    }
}
