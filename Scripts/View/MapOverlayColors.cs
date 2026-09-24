using Godot;

/// <summary>
/// The 2D view's shared visual language, used identically by all four
/// per-element overlay handlers (<see cref="VertexOverlayHandler"/>/
/// <see cref="LinedefOverlayHandler"/>/<see cref="SectorOverlayHandler"/>/
/// <see cref="ThingOverlayHandler"/>) - "hovered/dragged swaps the
/// category/base tint for <see cref="Hover"/> outright... rather than a
/// new visual language just for [any one element type]," the same
/// reasoning already established when this was one shared set of
/// constants inside <see cref="MapOverlay"/> itself, kept as one shared
/// definition here rather than duplicated per handler so the four can't
/// independently drift.
/// </summary>
public static class MapOverlayColors
{
	public const float InactiveModeAlpha = 0.35f;

	public static readonly Color Hover = new(1f, 0.55f, 0.1f);

	/// <summary>Persistent multi-selection tint - always drawn in place of the base color, but hover always wins over it.</summary>
	public static readonly Color Selected = new(0.9f, 0.15f, 0.15f);

	/// <summary>UDB's own real <c>General.Colors.InfoLine</c> default (<c>#C6C6FF</c>) - a sector's own tag-number label and the tag-arrow indicator both use this, so it lives here rather than duplicated in each.</summary>
	public static readonly Color InfoLine = new(0.776f, 0.776f, 1f);
}
