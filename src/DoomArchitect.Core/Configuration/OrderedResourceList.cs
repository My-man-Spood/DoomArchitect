namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Reads/writes an ordered list of strings as a <c>resources { resource0
/// = "..."; resource1 = "..."; }</c> block - the same numbered-key idiom
/// UDB's own <c>Configuration</c>-backed code uses for ordered lists
/// (<c>taglabel1</c>, <c>document0</c>, etc., confirmed in
/// <c>Source/Core/Map/MapOptions.cs</c>), needed because the underlying
/// storage is a plain dictionary with no guaranteed enumeration order on
/// either side of this parser. Shared by <see cref="AppSettings"/> and
/// <see cref="MapSettings"/>, the two things that persist an ordered
/// resource list.
/// </summary>
internal static class OrderedResourceList
{
    private const string KeyPrefix = "resource";

    public static IReadOnlyList<string> Read(CfgBlock? block)
    {
        if (block == null) return Array.Empty<string>();

        return block.Assignments
            .Select(a => (Order: ParseOrder(a.Key), a.Value))
            .Where(x => x.Order.HasValue)
            .OrderBy(x => x.Order!.Value)
            .Select(x => x.Value.AsString())
            .ToList();
    }

    public static CfgBlock Write(IReadOnlyList<string> paths)
    {
        var assignments = paths
            .Select((path, index) => new CfgAssignment($"{KeyPrefix}{index}", CfgValue.OfString(path)))
            .ToList();
        return new CfgBlock("resources", assignments, Array.Empty<CfgBlock>());
    }

    private static int? ParseOrder(string key) =>
        key.StartsWith(KeyPrefix, StringComparison.Ordinal) &&
        int.TryParse(key.AsSpan(KeyPrefix.Length), out var order)
            ? order
            : null;
}
