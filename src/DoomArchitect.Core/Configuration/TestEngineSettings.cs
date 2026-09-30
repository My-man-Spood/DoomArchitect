namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Reads/writes a <c>testengines { active = N; engine0 { ... } engine1
/// { ... } }</c> block - a numbered-key idiom for the engine list itself
/// (same reason as <see cref="OrderedResourceList"/>: no guaranteed
/// enumeration order on either side of this parser) plus one plain
/// <c>active</c> field naming which engine Test Map actually uses.
/// </summary>
internal static class TestEngineSettings
{
    private const string KeyPrefix = "engine";
    private const string ActiveKey = "active";
    private const string NameKey = "name";
    private const string ExecutablePathKey = "exe";
    private const string UseCustomParametersKey = "usecustomparams";
    private const string CustomParametersKey = "params";

    public static IReadOnlyList<TestEngine> Read(CfgBlock? block)
    {
        if (block == null) return Array.Empty<TestEngine>();

        return block.Blocks
            .Select(b => (Order: ParseOrder(b.Key), Block: b))
            .Where(x => x.Order.HasValue)
            .OrderBy(x => x.Order!.Value)
            .Select(x => ReadOne(x.Block))
            .ToList();
    }

    /// <summary>Clamped to a valid index if the stored value is out of range (e.g. the active engine was since removed) - -1 when there are no engines at all.</summary>
    public static int ReadActiveIndex(CfgBlock? block, int engineCount)
    {
        if (engineCount == 0) return -1;

        var stored = block?.Find(ActiveKey)?.AsInt() ?? 0;
        return Math.Clamp(stored, 0, engineCount - 1);
    }

    public static CfgBlock Write(IReadOnlyList<TestEngine> engines, int activeIndex)
    {
        var blocks = engines
            .Select((engine, index) => WriteOne($"{KeyPrefix}{index}", engine))
            .ToList();
        var assignments = new List<CfgAssignment> { new(ActiveKey, CfgValue.OfInt(Math.Clamp(activeIndex, 0, Math.Max(engines.Count - 1, 0)))) };
        return new CfgBlock("testengines", assignments, blocks);
    }

    private static TestEngine ReadOne(CfgBlock block) => new(
        Name: block.Find(NameKey)?.AsString() ?? block.Key,
        ExecutablePath: block.Find(ExecutablePathKey)?.AsString() ?? string.Empty,
        UseCustomParameters: block.Find(UseCustomParametersKey)?.AsBool() ?? false,
        CustomParameters: block.Find(CustomParametersKey)?.AsString() ?? string.Empty);

    private static CfgBlock WriteOne(string key, TestEngine engine)
    {
        var assignments = new List<CfgAssignment>
        {
            new(NameKey, CfgValue.OfString(engine.Name)),
            new(ExecutablePathKey, CfgValue.OfString(engine.ExecutablePath)),
        };

        if (engine.UseCustomParameters)
        {
            assignments.Add(new CfgAssignment(UseCustomParametersKey, CfgValue.OfBool(true)));
            assignments.Add(new CfgAssignment(CustomParametersKey, CfgValue.OfString(engine.CustomParameters)));
        }

        return new CfgBlock(key, assignments, Array.Empty<CfgBlock>());
    }

    private static int? ParseOrder(string key) =>
        key.StartsWith(KeyPrefix, StringComparison.Ordinal) &&
        int.TryParse(key.AsSpan(KeyPrefix.Length), out var order)
            ? order
            : null;
}
