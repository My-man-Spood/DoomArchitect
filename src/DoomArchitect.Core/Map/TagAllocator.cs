namespace DoomArchitect.Core.Map;

/// <summary>
/// Finds a tag number not already in use - the shared logic behind the
/// Sector dialog's "New"/"Unused" buttons (see <see cref="MapDataTagQueries"/>
/// for what counts as "in use"), kept as its own pure, easily-tested
/// function rather than folded into the query methods themselves.
/// </summary>
public static class TagAllocator
{
    public static long FindFree(IEnumerable<long> used, long start = 1)
    {
        var usedSet = used as ISet<long> ?? new HashSet<long>(used);

        var candidate = start;
        while (usedSet.Contains(candidate)) candidate++;

        return candidate;
    }
}
