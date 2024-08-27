// *********************************************************************************
// # Project: Server
// # Unity: 2022.3.5f1c1
// # Author: jinyijie
// # Version: 1.0.0
// # History: 2024-08-27  20:08
// # Copyright: 2024, jinyijie
// # Description: This is an automatically generated comment.
// *********************************************************************************

using System;
using System.Collections.Generic;

namespace JFramework.Net
{
    public static class PoolManager
    {
        private static readonly Dictionary<Type, IPool> streams = new();

        public static T Dequeue<T>() where T : new()
        {
            if (streams.TryGetValue(typeof(T), out var stream) && stream.count > 0)
            {
                return ((IPool<T>)stream).Pop();
            }

            return new T();
        }

        public static T Dequeue<T>(Type type) where T : new()
        {
            if (streams.TryGetValue(type, out var stream) && stream.count > 0)
            {
                return ((IPool<T>)stream).Pop();
            }

            return new T();
        }

        public static void Enqueue<T>(T obj) where T : new()
        {
            if (streams.TryGetValue(typeof(T), out var pool))
            {
                ((IPool<T>)pool).Push(obj);
                return;
            }

            streams.Add(typeof(T), new Pool<T>(obj));
        }

        public static void Enqueue<T>(T obj, Type type) where T : new()
        {
            if (streams.TryGetValue(type, out var pool))
            {
                ((IPool<T>)pool).Push(obj);
                return;
            }

            streams.Add(type, new Pool<T>(obj));
        }

        [Serializable]
        private class Pool<T> : IPool<T> where T : new()
        {
            private readonly Queue<T> objects = new Queue<T>();
            private readonly HashSet<T> unique = new HashSet<T>();
            public int count => objects.Count;

            public Pool(T obj)
            {
                unique.Add(obj);
                objects.Enqueue(obj);
            }

            public T Pop()
            {
                if (objects.Count > 0)
                {
                    var obj = objects.Dequeue();
                    unique.Remove(obj);
                    return obj;
                }

                return new T();
            }

            public bool Push(T obj)
            {
                if (unique.Add(obj))
                {
                    objects.Enqueue(obj);
                    return true;
                }

                return false;
            }

            public void Dispose()
            {
                unique.Clear();
                objects.Clear();
            }
        }

        private interface IPool : IDisposable
        {
            int count { get; }
        }

        private interface IPool<T> : IPool
        {
            T Pop();

            bool Push(T obj);
        }
    }
}