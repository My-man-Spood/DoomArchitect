namespace DoomArchitect.Core.ZDoom;

/// <summary>A parsed `goto ClassName::StateName+Offset` directive inside a state block - ported verbatim from UDB's real <c>StateGoto</c> (trivial, no dependencies).</summary>
public class StateGoto
{
    public string ClassName { get; internal set; } = string.Empty;
    public string StateName { get; internal set; } = string.Empty;
    public int SpriteOffset { get; internal set; }
}
