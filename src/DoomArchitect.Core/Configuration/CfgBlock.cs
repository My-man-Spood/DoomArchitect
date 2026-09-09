namespace DoomArchitect.Core.Configuration;

public sealed record CfgAssignment(string Key, CfgValue Value);

/// <summary>
/// A fully <c>include()</c>-resolved <c>.cfg</c> scope (<c>key { ... }</c>,
/// or the document root with an empty <see cref="Key"/>) - the merged
/// result <see cref="CfgLoader"/> produces, mirroring <see cref="IO.UdmfBlock"/>'s
/// shape. Unlike the raw <see cref="CfgStatement"/> tree, there's exactly
/// one entry per key here (duplicates/includes have already been merged),
/// so enumeration order isn't meaningful the way statement order is in the
/// raw tree.
/// </summary>
public sealed class CfgBlock
{
    public CfgBlock(string key, IReadOnlyList<CfgAssignment> assignments, IReadOnlyList<CfgBlock> blocks)
    {
        Key = key;
        Assignments = assignments;
        Blocks = blocks;
    }

    public string Key { get; }

    public IReadOnlyList<CfgAssignment> Assignments { get; }

    public IReadOnlyList<CfgBlock> Blocks { get; }

    public CfgValue? Find(string key)
    {
        foreach (var assignment in Assignments)
        {
            if (assignment.Key == key) return assignment.Value;
        }

        return null;
    }

    public CfgBlock? FindBlock(string key)
    {
        foreach (var block in Blocks)
        {
            if (block.Key == key) return block;
        }

        return null;
    }

    /// <summary>An empty scope, for building a fresh document from scratch (e.g. a brand new settings file).</summary>
    public static CfgBlock Empty(string key = "") => new(key, Array.Empty<CfgAssignment>(), Array.Empty<CfgBlock>());

    /// <summary>
    /// A new block with <paramref name="key"/>'s assignment replaced (or
    /// added) and every other assignment/block left exactly as-is - the
    /// non-destructive "change one field" a settings file needs when it
    /// must preserve fields this codebase doesn't model (see
    /// <c>MapSettings</c>'s own remarks).
    /// </summary>
    public CfgBlock WithAssignment(string key, CfgValue value)
    {
        var assignments = Assignments.Where(a => a.Key != key).Append(new CfgAssignment(key, value)).ToList();
        return new CfgBlock(Key, assignments, Blocks);
    }

    /// <summary>Same idea as <see cref="WithAssignment"/>, for a nested block instead of a scalar value.</summary>
    public CfgBlock WithBlock(string key, CfgBlock child)
    {
        var retargeted = child.Key == key ? child : new CfgBlock(key, child.Assignments, child.Blocks);
        var blocks = Blocks.Where(b => b.Key != key).Append(retargeted).ToList();
        return new CfgBlock(Key, Assignments, blocks);
    }
}
