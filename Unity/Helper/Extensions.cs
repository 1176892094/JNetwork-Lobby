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

        public static unsafe void Deserialize<T>(this byte[] data, ref int position, out T value) where T : unmanaged
        {
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
        }

        public static void Write(this byte[] data, ref int position, byte value)
        {
            Serialize(data, ref position, value);
        }

        public static void Read(this byte[] data, ref int position, out byte value)
        {
            Deserialize(data, ref position, out value);
        }

        public static void Write(this byte[] data, ref int position, int value)
        {
            Serialize(data, ref position, value);
        }

        public static void Read(this byte[] data, ref int position, out int value)
        {
            Deserialize(data, ref position, out value);
        }

        public static void Write(this byte[] data, ref int position, bool value)
        {
            Serialize(data, ref position, (byte)(value ? 1 : 0));
        }

        public static void Read(this byte[] data, ref int position, out bool value)
        {
            Deserialize(data, ref position, out byte param);
            value = param != 0;
        }

        public static void Write(this byte[] data, ref int position, char value)
        {
            Serialize(data, ref position, (ushort)value);
        }

        public static void Read(this byte[] data, ref int position, out char value)
        {
            Deserialize(data, ref position, out ushort param);
            value = (char)param;
        }

        public static void Write(this byte[] data, ref int position, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                data.Write(ref position, 0);
                return;
            }

            data.Write(ref position, value.Length);
            foreach (var c in value)
            {
                data.Write(ref position, c);
            }
        }

        public static void Read(this byte[] data, ref int position, out string value)
        {
            data.Read(ref position, out int size);
            value = default;
            for (int i = 0; i < size; i++)
            {
                data.Read(ref position, out char c);
                value += c;
            }
        }

        public static void Write(this byte[] data, ref int position, byte[] value)
        {
            data.Write(ref position, value.Length);
            foreach (var b in value)
            {
                data.Write(ref position, b);
            }
        }

        public static void Read(this byte[] data, ref int position, out byte[] value)
        {
            data.Read(ref position, out int size);
            value = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data.Read(ref position, out byte b);
                value[i] = b;
            }
        }
    }
}