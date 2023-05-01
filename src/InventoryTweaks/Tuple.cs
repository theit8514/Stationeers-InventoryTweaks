namespace InventoryTweaks;

public readonly struct Tuple<T1, T2>
{
    public readonly T1 Item1;
    public readonly T2 Item2;

    public Tuple(T1 item1, T2 item2)
    {
        Item1 = item1;
        Item2 = item2;
    }

    public static Tuple<T1, T2> Create(T1 first, T2 second)
    {
        return new Tuple<T1, T2>(first, second);
    }
}