// *********************************************************************************
// # Project: Test
// # Unity: 2022.3.5f1c1
// # Author: Charlotte
// # Version: 1.0.0
// # History: 2024-06-05  04:06
// # Copyright: 2024, Charlotte
// # Description: This is an automatically generated comment.
// *********************************************************************************

using System;
using System.IO;

namespace JFramework.Net
{
    public static partial class StreamExtensions
    {
        public static void WriteByte(this NetworkWriter writer, byte value)
        {
            writer.Write(value);
        }

        public static void WriteBool(this NetworkWriter writer, bool value)
        {
            writer.Write((byte)(value ? 1 : 0));
        }

        public static void WriteUShort(this NetworkWriter writer, ushort value)
        {
            writer.Write(value);
        }

        public static void WriteInt(this NetworkWriter writer, int value)
        {
            writer.Write(value);
        }

        public static void WriteUInt(this NetworkWriter writer, uint value)
        {
            writer.Write(value);
        }

        public static void WriteString(this NetworkWriter writer, string value)
        {
            if (value == null)
            {
                writer.WriteUShort(0);
                return;
            }

            writer.AddCapacity(writer.position + 2 + writer.encoding.GetMaxByteCount(value.Length));
            var count = writer.encoding.GetBytes(value, 0, value.Length, writer.buffer, writer.position + 2);
            if (count > ushort.MaxValue - 1)
            {
                throw new EndOfStreamException("写入字符串过长!");
            }

            writer.WriteUShort(checked((ushort)(count + 1))); // writer.position + 2
            writer.position += count;
        }

        public static void WriteArraySegment(this NetworkWriter writer, ArraySegment<byte> value)
        {
            if (value == null)
            {
                writer.WriteUInt(0);
                return;
            }

            writer.WriteUInt(checked((uint)value.Count) + 1);
            writer.WriteBytes(value.Array, value.Offset, value.Count);
        }
    }
}