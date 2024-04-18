using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;

namespace JFramework.Net
{
    public static class Extensions
    {
        public static unsafe void Serialize<T>(this byte[] data, ref int position, T value) where T : unmanaged
        {
            fixed (byte* ptr = &data[position])
            {
#if UNITY_ANDROID
                var buffer = stackalloc T[1] { value };
                UnsafeUtility.MemCpy(ptr, buffer, sizeof(T));
#else
                *(T*)ptr = value;
#endif
            }

            position += sizeof(T);
        }

        public static unsafe T Deserialize<T>(this byte[] data, ref int position) where T : unmanaged
        {
            T value;
            fixed (byte* ptr = &data[position])
            {
#if UNITY_ANDROID
                var buffer = stackalloc T[1];
                UnsafeUtility.MemCpy(buffer, ptr, sizeof(T));
                value = buffer[0];
#else
                value = *(T*)ptr;
#endif
            }

            position += sizeof(T);
            return value;
        }

        public static void WriteByte(this byte[] data, ref int position, byte value)
        {
            Serialize(data, ref position, value);
        }

        public static byte ReadByte(this byte[] data, ref int position)
        {
            return data.Deserialize<byte>(ref position);
        }

        public static void WriteInt(this byte[] data, ref int position, int value)
        {
            Serialize(data, ref position, value);
        }

        public static int ReadInt(this byte[] data, ref int position)
        {
            return data.Deserialize<int>(ref position);
        }

        public static void WriteBool(this byte[] data, ref int position, bool value)
        {
            Serialize(data, ref position, (byte)(value ? 1 : 0));
        }

        public static bool ReadBool(this byte[] data, ref int position)
        {
            return data.Deserialize<byte>(ref position) != 0;
        }

        public static void WriteChar(this byte[] data, ref int position, char value)
        {
            Serialize(data, ref position, (ushort)value);
        }

        public static char ReadChar(this byte[] data, ref int position)
        {
            return (char)data.Deserialize<ushort>(ref position);
        }

        public static void WriteString(this byte[] data, ref int position, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                data.WriteInt(ref position, 0);
                return;
            }

            data.WriteInt(ref position, value.Length);
            foreach (var c in value)
            {
                data.WriteChar(ref position, c);
            }
        }

        public static string ReadString(this byte[] data, ref int position)
        {
            var length = data.ReadInt(ref position);
            string value = default;
            for (int i = 0; i < length; i++)
            {
                value += data.ReadChar(ref position);
            }

            return value;
        }

        public static void WriteBytes(this byte[] data, ref int position, byte[] value)
        {
            data.WriteInt(ref position, value.Length);
            foreach (var b in value)
            {
                data.WriteByte(ref position, b);
            }
        }

        public static byte[] ReadBytes(this byte[] data, ref int position)
        {
            var length = data.ReadInt(ref position);
            var value = new byte[length];
            for (int i = 0; i < length; i++)
            {
                value[i] = data.ReadByte(ref position);
            }

            return value;
        }

        public static string Decompress(this string compressedText)
        {
            byte[] gZipBuffer = Convert.FromBase64String(compressedText);
            using (var memoryStream = new MemoryStream())
            {
                int dataLength = BitConverter.ToInt32(gZipBuffer, 0);
                memoryStream.Write(gZipBuffer, 4, gZipBuffer.Length - 4);

                var buffer = new byte[dataLength];

                memoryStream.Position = 0;
                using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                {
                    gZipStream.Read(buffer, 0, buffer.Length);
                }

                return Encoding.UTF8.GetString(buffer);
            }
        }
    }
}