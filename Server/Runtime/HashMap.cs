using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace JFramework.Net
{
    internal class HashMap<TFirst, TSecond>
    {
        private readonly IDictionary<TFirst, TSecond> firstToSecond = new ConcurrentDictionary<TFirst, TSecond>();
        private readonly IDictionary<TSecond, TFirst> secondToFirst = new ConcurrentDictionary<TSecond, TFirst>();
        public ICollection<TFirst> Keys => secondToFirst.Values;
        public ICollection<TSecond> Values => firstToSecond.Values;

        public void Add(TFirst first, TSecond second)
        {
            if (firstToSecond.ContainsKey(first) || secondToFirst.ContainsKey(second))
            {
                throw new ArgumentException("双向字典包含了重复的键和值！");
            }

            firstToSecond.Add(first, second);
            secondToFirst.Add(second, first);
        }

        public bool TryGetFirst(TFirst first, out TSecond second)
        {
            return firstToSecond.TryGetValue(first, out second);
        }

        public bool TryGetSecond(TSecond second, out TFirst first)
        {
            return secondToFirst.TryGetValue(second, out first);
        }

        public TSecond GetFirst(TFirst first)
        {
            return firstToSecond[first];
        }

        public TFirst GetSecond(TSecond second)
        {
            return secondToFirst[second];
        }

        public void Remove(TFirst first)
        {
            secondToFirst.Remove(firstToSecond[first]);
            firstToSecond.Remove(first);
        }

        public void Clear()
        {
            firstToSecond.Clear();
            secondToFirst.Clear();
        }
    }
}