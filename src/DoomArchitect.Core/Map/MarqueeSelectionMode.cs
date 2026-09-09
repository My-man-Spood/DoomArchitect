namespace DoomArchitect.Core.Map;

/// <summary>
/// How a marquee/box-select rectangle's hit-set combines with the
/// current selection - a close port of UDB's real
/// <c>MarqueSelectionMode</c> (<c>BaseClassicMode.GetMultiSelectionMode</c>):
/// no modifier keys means <see cref="Select"/> (replace); Shift means
/// <see cref="Add"/>; Ctrl means <see cref="Subtract"/>; Ctrl+Shift means
/// <see cref="Intersect"/>.
/// </summary>
public enum MarqueeSelectionMode
{
    Select,
    Add,
    Subtract,
    Intersect,
}
