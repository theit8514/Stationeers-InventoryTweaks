using System;
using System.Collections.Generic;
using System.Linq;

namespace InventoryTweaks.Utilities;

public class Lookup<T1, T2, TOut>
{
    private readonly ILookup<(T1, T2), TOut> _lookup;

    public Lookup(IEnumerable<TOut> source, Func<TOut, (T1, T2)> keySelector)
    {
        _lookup = source.ToLookup(keySelector);
    }

    public IEnumerable<TOut> this[T1 first, T2 second] => _lookup[(first, second)];
}