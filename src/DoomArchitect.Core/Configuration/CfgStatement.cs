namespace DoomArchitect.Core.Configuration;

/// <summary>
/// One statement inside a <c>.cfg</c> scope, in source order - order
/// matters here in a way it doesn't for <see cref="IO.UdmfBlock"/>: an
/// <c>include()</c> statement's merge result depends on what came before
/// it in the same scope (see <see cref="CfgLoader"/>'s remarks), so the
/// raw parse tree keeps statements as one ordered sequence rather than
/// bucketing them into separate assignment/block lists the way
/// <see cref="IO.UdmfTreeParser"/> does for UDMF.
/// </summary>
public abstract class CfgStatement
{
}

/// <summary>
/// <c>key = value;</c>, or the bare/null forms <c>key;</c> and
/// <c>key = null;</c> (both valid per UDB's own grammar - <see cref="Value"/>
/// is null for either).
/// </summary>
public sealed class CfgAssignStatement : CfgStatement
{
    public CfgAssignStatement(string key, CfgValue? value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    public CfgValue? Value { get; }
}

/// <summary>
/// <c>key { ... }</c>. <see cref="Key"/> may be an identifier or a bare
/// integer written as text (e.g. thing/linedef/sector type numbers) - the
/// grammar itself doesn't distinguish the two, a key is just "everything up
/// to the next delimiter, trimmed".
/// </summary>
public sealed class CfgBlockStatement : CfgStatement
{
    public CfgBlockStatement(string key, IReadOnlyList<CfgStatement> body)
    {
        Key = key;
        Body = body;
    }

    public string Key { get; }

    public IReadOnlyList<CfgStatement> Body { get; }
}

/// <summary>
/// <c>include("path");</c> or <c>include("path", "sub.path");</c> - the
/// only function call UDB's real grammar recognizes. Resolved by
/// <see cref="CfgLoader"/>, not by the parser itself (resolving needs a
/// file source and a cache the pure parser doesn't have).
/// </summary>
public sealed class CfgIncludeStatement : CfgStatement
{
    public CfgIncludeStatement(string path, string? subPath)
    {
        Path = path;
        SubPath = subPath;
    }

    public string Path { get; }

    public string? SubPath { get; }
}
