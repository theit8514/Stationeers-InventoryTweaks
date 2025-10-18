using System.Collections.Generic;

namespace InventoryTweaks.Extensions;

public static class DictionaryExtensions
{
    public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value = default)
    {
        if (dict.ContainsKey(key))
            return false;
        dict.Add(key, value);
        return true;
    }
}