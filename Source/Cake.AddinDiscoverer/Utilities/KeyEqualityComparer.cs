using System;
using System.Collections.Generic;

namespace Cake.AddinDiscoverer.Utilities
{
	internal class KeyEqualityComparer<T, TKey> : IEqualityComparer<T>
	{
		private Func<T, TKey> GetKey { get; init; }

		public KeyEqualityComparer(Func<T, TKey> getKey)
		{
			GetKey = getKey;
		}

		public bool Equals(T x, T y)
		{
			return GetKey(x).Equals(GetKey(y));
		}

		public int GetHashCode(T obj)
		{
			return GetKey(obj).GetHashCode();
		}
	}
}
