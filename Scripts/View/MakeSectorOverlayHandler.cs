using System.Collections.Generic;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Make Sector mode - UDB's real <c>MakeSectorMode</c>
/// (<c>ClassicModes/MakeSectorMode.cs</c>): hovering traces a potential
/// sector boundary from the nearest linedef side
/// (<see cref="BoundaryTracer.FindPotentialSectorAt(MapData,Vector2)"/>),
/// and a click commits it (<see cref="MakeSectorCommand"/>, UDB's own
/// <c>Tools.MakeSector</c>) - which steals any sidedef already along the
/// traced boundary away from whatever sector currently (even wrongly)
/// owns it. The real reason this exists: a sector-construction bug
/// (now fixed - see <see cref="BoundaryTracer"/>'s own remarks on nested
/// holes) had already corrupted some existing sectors before the fix
/// landed, and redrawing them by hand wasn't an option - clicking here
/// re-derives the correct boundary from the map's real current geometry
/// and reassigns ownership correctly, without touching a single vertex.
///
/// Unlike Draw mode, every hover is independent - there's no
/// accumulated in-progress state to cancel when leaving this mode.
/// </summary>
public sealed class MakeSectorOverlayHandler
{
	private const float LineWidth = 3f;
	private const float FlashDurationMsec = 300f;

	// Deliberately its own color, distinct from MapOverlayColors.Hover -
	// a real, reported bug: the associate-sector fill originally reused
	// Hover at the same alpha SectorOverlayHandler's own plain hover
	// highlight already uses, so for a corrupted sector (exactly this
	// mode's main use case) the preview was pixel-identical to - and
	// so indistinguishable from - ordinary Sectors-mode hover, giving
	// no useful signal about what this mode would actually do.
	private static readonly Color PreviewColor = new(0.3f, 0.9f, 0.4f);
	private static readonly Color AssociateFillColor = PreviewColor with { A = 0.25f };

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;

	private IReadOnlyList<LinedefSide> _trace;

	private IReadOnlyList<(MapVector2 A, MapVector2 B, MapVector2 C)> _flashTriangles;
	private ulong _flashStartMsec;

	public MakeSectorOverlayHandler(MapOverlay owner, MapOverlayCamera camera)
	{
		_owner = owner;
		_camera = camera;
	}

	public void HandleInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
				Commit();
				break;
			case InputEventMouseMotion motion:
				_trace = BoundaryTracer.FindPotentialSectorAt(_owner.Map, _camera.Unproject(motion.Position));
				break;
		}
	}

	private void Commit()
	{
		if (_trace == null) return;

		var command = new MakeSectorCommand(_owner.Map, _trace);
		_owner.UndoStack.Execute(command);
		StartFlash(command.CreatedSector);
	}

	/// <summary>
	/// UDB's own real post-commit feedback (<c>MakeSectorMode</c>'s own
	/// <c>flashpolygon</c>/<c>flashintensity</c>/<c>flashstarttime</c>) -
	/// the just-created sector's own real floor triangles (the identical
	/// trace -&gt; nest -&gt; cut -&gt; ear-clip pipeline
	/// <see cref="SectorOverlayHandler.Fill"/> itself uses, computed once
	/// here rather than through that method since this needs the raw
	/// triangles cached, not drawn immediately), rendered solid white
	/// and linearly faded to transparent over <see cref="FlashDurationMsec"/> -
	/// confirms *which* sector just got (re)built, not just that a click
	/// registered.
	/// </summary>
	private void StartFlash(Sector sector)
	{
		if (sector == null)
		{
			_flashTriangles = null;
			return;
		}

		var triangles = new List<(MapVector2, MapVector2, MapVector2)>();
		foreach (var polygon in PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(sector))))
		{
			triangles.AddRange(EarClipper.Clip(polygon));
		}

		_flashTriangles = triangles;
		_flashStartMsec = Time.GetTicksMsec();
	}

	public void Draw(CanvasItem target)
	{
		DrawFlash(target);

		if (_owner.Mode != EditMode.MakeSector || _trace == null) return;

		// Every existing sector that already owns one of the traced
		// sides - UDB's own "associates": the ones a click here would
		// actually consume/affect, shown filled so a mapper can confirm
		// that before committing (a boundary that's all still void shows
		// no fill at all - a brand-new sector, nothing to steal from).
		var associates = new HashSet<Sector>();
		foreach (var side in _trace)
		{
			var existing = side.Front ? side.Linedef.Front : side.Linedef.Back;
			if (existing != null) associates.Add(existing.Sector);
		}

		foreach (var sector in associates) SectorOverlayHandler.Fill(target, _camera, sector, AssociateFillColor);

		foreach (var side in _trace)
		{
			target.DrawLine(
				_camera.Project(side.Linedef.Start.Position), _camera.Project(side.Linedef.End.Position),
				PreviewColor, LineWidth);
		}
	}

	/// <summary>Independent of the current mode/trace - the flash plays out over the following ~300ms even after a click has already switched away or the hover has moved on, matching UDB's own real behavior (the flash is a property of the sector just made, not of still being in this mode).</summary>
	private void DrawFlash(CanvasItem target)
	{
		if (_flashTriangles == null) return;

		var intensity = 1f - (Time.GetTicksMsec() - _flashStartMsec) / FlashDurationMsec;
		if (intensity <= 0f)
		{
			_flashTriangles = null;
			return;
		}

		var color = new Color(1f, 1f, 1f, intensity);
		foreach (var (a, b, c) in _flashTriangles)
		{
			target.DrawColoredPolygon(new[] { _camera.Project(a), _camera.Project(b), _camera.Project(c) }, color);
		}
	}
}
