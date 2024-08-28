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
        public static byte ReadByte(this NetworkReader reader)
        {
            return reader.Read<byte>();
        }

        public static bool ReadBool(this NetworkReader reader)
        {
            return reader.Read<byte>() != 0;
        }

        public static ushort ReadUShort(this NetworkReader reader)
        {
            return reader.Read<ushort>();
        }

        public static int ReadInt(this NetworkReader reader)
        {
            return reader.Read<int>();
        }

        public static uint ReadUInt(this NetworkReader reader)
        {
            return reader.Read<uint>();
        }

        public static string ReadString(this NetworkReader reader)
        {
            var count = reader.ReadUShort();
            if (count == 0)
            {
                return null;
            }

            count = (ushort)(count - 1);
            if (count > ushort.MaxValue - 1)
            {
                throw new EndOfStreamException("读取字符串过长!");
            }

            var segment = reader.ReadArraySegment(count);
            return reader.encoding.GetString(segment.Array, segment.Offset, segment.Count);
        }

        public static ArraySegment<byte> ReadArraySegment(this NetworkReader reader)
        {
            var count = reader.ReadUInt();
            return count == 0 ? null : reader.ReadArraySegment(checked((int)(count - 1)));
        }
    }
}