namespace DoomArchitect.Core.IO;

/// <summary>
/// A parsed UDMF block (<c>name { ... }</c>) or, with an empty
/// <see cref="Name"/>, the document root itself - the root is just a
/// block whose contents happen to sit outside any braces.
/// </summary>
public sealed class UdmfBlock
{
    public UdmfBlock(string name, IReadOnlyList<UdmfAssignment> assignments, IReadOnlyList<UdmfBlock> blocks)
    {
        Name = name;
        Assignments = assignments;
        Blocks = blocks;
    }

    public string Name { get; }

    public IReadOnlyList<UdmfAssignment> Assignments { get; }

    public IReadOnlyList<UdmfBlock> Blocks { get; }

    /// <summary>
    /// The value of <paramref name="key"/>, or null if absent. A duplicate
    /// key is not an error - the <em>last</em> matching assignment silently
    /// wins, matching UDB's own field lookup.
    /// </summary>
    public UdmfValue? Find(string key)
    {
        UdmfValue? result = null;
        foreach (var assignment in Assignments)
        {
            if (assignment.Key == key) result = assignment.Value;
        }

        return result;
    }
}
