using System.Threading;

namespace SinmaiAlpha.Notes.Libs;

// A native callback can re-enter for another controller (or a stock note).
// Restore the previous route even when Harmony runs the finalizer after an exception.
internal sealed class OwnerCallScope<T> where T : class
{
    private sealed class Lease
    {
        internal object Owner;
        internal T Value;
        internal Lease Previous;
        internal int Generation;
    }

    private readonly ThreadLocal<Lease> current = new();
    private volatile int generation;

    internal T For(object owner)
    {
        for (var entry = current.Value; entry != null && entry.Generation == generation; entry = entry.Previous)
            if (ReferenceEquals(entry.Owner, owner)) return entry.Value;
        return null;
    }

    internal object Enter(object owner, T value)
    {
        var previous = current.Value;
        var lease = new Lease { Owner = owner, Value = value,
            Previous = previous?.Generation == generation ? previous : null, Generation = generation };
        current.Value = lease;
        return lease;
    }

    internal void Exit(object state)
    {
        if (state is Lease lease && lease.Generation == generation && ReferenceEquals(current.Value, lease))
            current.Value = lease.Previous;
    }

    internal void Clear() { generation++; current.Value = null; }
}
