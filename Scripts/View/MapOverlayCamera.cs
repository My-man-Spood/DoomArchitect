using DoomArchitect.Interop;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Screen/map-space projection math for <see cref="MapOverlay"/> - split out
/// as its own plain (non-Node) class during the "MapOverlay.cs is a
/// growing god-object" cleanup flagged in TODO.md: everything here is pure
/// coordinate math with no drawing and no input handling, and every other
/// piece of <see cref="MapOverlay"/> (grid, marquee, all four per-element
/// handlers) needs it, so it's the one dependency every other split-out
/// piece takes. Holds the owning <see cref="Control"/> only for
/// <see cref="GetViewportRect"/>'s sake (<see cref="ViewportBounds"/> needs
/// the current viewport size) - <see cref="Camera"/> itself is a plain
/// settable property since <c>MapView</c> assigns it after construction
/// and can swap it.
/// </summary>
public sealed class MapOverlayCamera
{
	private const float MinCameraSize = 20f;

	// Was 2000 - too tight to zoom out far enough to see a whole real
	// map's floor plan at once (a real Doom level can easily span several
	// thousand map units per side), found in practice rather than ported
	// from any particular UDB limit (its own 2D view has no hard zoom-out
	// ceiling at all - this project keeps one purely so a stray huge
	// scroll can't zoom out to a degenerate near-infinite size).
	private const float MaxCameraSize = 20000f;

	private readonly Control _viewport;

	public MapOverlayCamera(Control viewport)
	{
		_viewport = viewport;
	}

	public Camera3D Camera { get; set; }

	/// <summary>Casts a ray from the camera through a screen point down to the map's ground plane (Y = 0).</summary>
	public MapVector2 Unproject(Vector2 screenPosition)
	{
		var origin = Camera.ProjectRayOrigin(screenPosition);
		var direction = Camera.ProjectRayNormal(screenPosition);
		var distanceToPlane = -origin.Y / direction.Y;
		return (origin + direction * distanceToPlane).ToDoom();
	}

	public Vector2 Project(MapVector2 doomPosition) => Camera.UnprojectPosition(doomPosition.ToWorld(0f));

	public float WorldSizeToScreenPixels(float size) =>
		Project(new MapVector2(size, 0)).DistanceTo(Project(MapVector2.Zero));

	/// <summary>
	/// The map-space rectangle the camera currently sees, found by
	/// unprojecting the viewport's own corners rather than reasoning about
	/// Godot's orthographic-projection math directly - works the same
	/// regardless of projection type or aspect ratio.
	/// </summary>
	public (MapVector2 Min, MapVector2 Max) ViewportBounds()
	{
		var size = _viewport.GetViewportRect().Size;
		var corners = new[]
		{
			Unproject(Vector2.Zero),
			Unproject(new Vector2(size.X, 0)),
			Unproject(new Vector2(0, size.Y)),
			Unproject(size),
		};

		var min = corners[0];
		var max = corners[0];
		foreach (var corner in corners)
		{
			min = MapVector2.Min(min, corner);
			max = MapVector2.Max(max, corner);
		}

		return (min, max);
	}

	/// <summary>
	/// Changes the ortho camera's <see cref="Camera3D.Size"/> (smaller =
	/// zoomed in) while keeping the map-space point under the cursor fixed
	/// on screen, the way UDB's own scroll-to-zoom does - otherwise
	/// zooming would recenter on the map origin instead of the cursor.
	/// </summary>
	public void ZoomAt(Vector2 screenPosition, float factor)
	{
		var before = Unproject(screenPosition);
		Camera.Size = Mathf.Clamp(Camera.Size * factor, MinCameraSize, MaxCameraSize);
		var after = Unproject(screenPosition);
		Camera.Position += (before - after).ToWorld(0f);
	}

	/// <summary>
	/// Grab-and-drag view panning while Space is held - a direct port of
	/// UDB's own real <c>ClassicMode.OnUpdateViewPanning</c>/
	/// <c>ScrollBy(lastmappos - mousemappos)</c>: the map point that was
	/// under the cursor before this motion event ends up under the cursor
	/// again after it, at whatever the current zoom's screen-to-map ratio
	/// is - no separate pan speed to tune. Reuses the exact same
	/// before/after-unproject-then-shift-camera trick <see cref="ZoomAt"/>
	/// already established for keeping a point fixed under the cursor,
	/// just for a translation instead of a zoom change.
	/// </summary>
	public void PanView(InputEventMouseMotion motion)
	{
		var before = Unproject(motion.Position - motion.Relative);
		var after = Unproject(motion.Position);
		Camera.Position += (before - after).ToWorld(0f);
	}
}
