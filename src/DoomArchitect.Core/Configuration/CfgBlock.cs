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
}
