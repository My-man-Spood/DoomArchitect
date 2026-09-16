using DoomArchitect.Core.Geometry;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// The 2D view's background grid - state (<see cref="GridSize"/>,
/// <see cref="DynamicGridSizeEnabled"/>) plus drawing, split out of
/// <see cref="MapOverlay"/> during its "growing god-object" cleanup
/// (flagged in TODO.md). Genuinely self-contained: needs only
/// <see cref="MapOverlayCamera"/> (for screen-pixel sizing and the
/// current viewport's map-space bounds) and a <see cref="CanvasItem"/> to
/// draw into, passed to <see cref="Draw"/> rather than held - this class
/// isn't itself a Node, matching every other piece split out of
/// <see cref="MapOverlay"/> this same pass.
/// </summary>
public sealed class MapOverlayGrid
{
	public const float DefaultGridSize = 32f;
	public const float MinGridSize = 1f;
	public const float MaxGridSize = 1024f;

	/// <summary>UDB's own threshold in <c>Renderer2D.RenderGrid</c> for when a grid tier is too dense to read.</summary>
	private const float MinGridCellPixels = 6f;
	private const int MaxGridDoublings = 20;
	private const float Grid64Size = 64f;

	private static readonly Color GridColor = new(0.5f, 0.5f, 0.55f, 0.22f);
	private static readonly Color Grid64Color = new(0.65f, 0.65f, 0.85f, 0.32f);

	private readonly MapOverlayCamera _camera;

	public MapOverlayGrid(MapOverlayCamera camera)
	{
		_camera = camera;
	}

	public float GridSize { get; set; } = DefaultGridSize;

	/// <summary>
	/// Mirrors UDB's own "DynamicGridSize" setting (default on there too):
	/// while enabled, zooming recomputes <see cref="GridSize"/> via
	/// <see cref="DynamicGridSize"/> instead of leaving it
	/// fixed. Manually changing grid size (<c>[</c>/<c>]</c>) turns this
	/// off, matching UDB's <c>DisableDynamicGridResize</c>.
	/// </summary>
	public bool DynamicGridSizeEnabled { get; set; } = true;

	/// <summary>
	/// Matches UDB's own <c>[</c>/<c>]</c> grid-size keys: doubles within
	/// the 1..1024 bound, and turns off <see cref="DynamicGridSizeEnabled"/>
	/// first, matching UDB's <c>DisableDynamicGridResize</c> - manual and
	/// automatic sizing shouldn't fight each other. Shared by the keybind
	/// and the grid toolbar's +/- buttons so both go through one policy.
	/// </summary>
	public void IncreaseGridSize()
	{
		DynamicGridSizeEnabled = false;
		if (GridSize <= MaxGridSize / 2) GridSize *= 2f;
	}

	public void DecreaseGridSize()
	{
		DynamicGridSizeEnabled = false;
		if (GridSize >= MinGridSize * 2) GridSize /= 2f;
	}

	/// <summary>Ported from UDB's <c>ClassicMode.MatchGridSizeToDisplayScale</c>, called on every zoom change.</summary>
	public void ApplyDynamicGridSize()
	{
		var (min, max) = _camera.ViewportBounds();
		var minVisibleExtent = Mathf.Min(max.X - min.X, max.Y - min.Y);
		var target = DynamicGridSize.ForVisibleExtent(minVisibleExtent);
		GridSize = Mathf.Clamp(target, MinGridSize, MaxGridSize);
	}

	/// <summary>
	/// Ported from UDB's <c>RenderBackgroundGrid</c>/<c>RenderGrid</c>:
	/// the configured grid draws in the normal color, plus - whenever
	/// that configured size is 64 or finer - a second tier always fixed
	/// at exactly 64 units (Doom's standard alignment unit) in a distinct
	/// color, so that reference stays visible however fine you've zoomed
	/// the working grid. Not ported: UDB's separate "DynamicGridSize"
	/// setting that auto-adjusts the persisted grid size itself as you
	/// zoom - this only adapts what's drawn, never the configured/snap size.
	/// </summary>
	public void Draw(CanvasItem target)
	{
		DrawTier(target, GridSize, GridColor);
		if (GridSize <= Grid64Size) DrawTier(target, Grid64Size, Grid64Color);
	}

	/// <summary>
	/// Doubles <paramref name="baseSize"/> until each cell is at least
	/// <see cref="MinGridCellPixels"/> wide on screen, exactly like UDB's
	/// own "increase rendered grid size if needed" fallback in
	/// <c>RenderGrid</c> - otherwise a fine grid zoomed far out renders as
	/// a dense, illegible mesh of lines.
	/// </summary>
	private void DrawTier(CanvasItem target, float baseSize, Color color)
	{
		var size = baseSize;
		for (var i = 0; i < MaxGridDoublings && _camera.WorldSizeToScreenPixels(size) <= MinGridCellPixels; i++)
		{
			size *= 2f;
		}

		var (min, max) = _camera.ViewportBounds();
		var startX = SnapDown(min.X, size);
		var endX = SnapUp(max.X, size);
		var startY = SnapDown(min.Y, size);
		var endY = SnapUp(max.Y, size);

		for (var x = startX; x <= endX; x += size)
		{
			DrawWorldLine(target, new MapVector2(x, startY), new MapVector2(x, endY), color);
		}

		for (var y = startY; y <= endY; y += size)
		{
			DrawWorldLine(target, new MapVector2(startX, y), new MapVector2(endX, y), color);
		}
	}

	private void DrawWorldLine(CanvasItem target, MapVector2 from, MapVector2 to, Color color, float width = 1f) =>
		target.DrawLine(_camera.Project(from), _camera.Project(to), color, width);

	private static float SnapDown(float value, float step) => Mathf.Floor(value / step) * step;

	private static float SnapUp(float value, float step) => Mathf.Ceil(value / step) * step;
}
