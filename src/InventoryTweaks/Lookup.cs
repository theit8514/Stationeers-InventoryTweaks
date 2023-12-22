using System;
using System.Collections.Generic;
using System.Linq;

namespace InventoryTweaks;

public class Lookup<T1, T2, TOut>
{
    private readonly ILookup<Tuple<T1, T2>, TOut> _lookup;

    public Lookup(IEnumerable<TOut> source, Func<TOut, Tuple<T1, T2>> keySelector)
    {
        _lookup = source.ToLookup(keySelector);
    }

    public IEnumerable<TOut> this[T1 first, T2 second] => _lookup[Tuple<T1, T2>.Create(first, second)];
}