using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// UDB's own real 2D tag/action indicator family, ported directly
/// (verified against source, not guessed - see TODO.md's own writeup for
/// the exact real behaviors this mirrors and the deliberate simplifications
/// against them): every tagged sector shows a permanent "Tag N" label
/// (<c>LinedefsMode.SetupSectorLabels</c>'s own real always-visible
/// behavior, not hover-gated), and hovering a tagged linedef or sector
/// (<c>Association.cs</c>) draws an arrow to whatever it's tagged to, with
/// the target sector(s) flood-filled in the hover color. Doom-format plain
/// tag matching only (<c>MapDataTagQueries.ParseTags</c>'s own
/// <c>id</c>/<c>moreids</c> fields) - UDB's own Hexen/UDMF "generalized"
/// path additionally resolves tags out of a linedef's own action
/// arguments, which this project has no per-argument "this is a tag"
/// metadata to identify by yet (the same gap <c>MapDataTagQueries</c>'s
/// own doc comment already flags).
///
/// Deliberately its own handler, not folded into
/// <see cref="LinedefOverlayHandler"/>/<see cref="SectorOverlayHandler"/>:
/// this spans both element types and needs to read whichever one is
/// currently hovered from outside either handler's own state, exactly the
/// passthrough <see cref="LinedefOverlayHandler.Hovered"/>/
/// <see cref="SectorOverlayHandler.Hovered"/> exist for.
/// </summary>
public sealed class TagIndicatorOverlayHandler
{
	private const float LineWidth = 1.5f;
	private const float ArrowheadLengthPixels = 16f; // UDB's own real constant screen-pixel arrowhead size (it scales by 1/renderer.Scale to get here from map-space; this project already works in projected screen space, so no such scaling is needed).
	private const float ArrowheadHalfAngleRadians = 0.46f; // UDB's own real half-angle between the two arrowhead wings.

	private static readonly Color SectorFillColor = new(MapOverlayColors.Hover, 0.5f);

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;
	private readonly LinedefOverlayHandler _linedefHandler;
	private readonly SectorOverlayHandler _sectorHandler;

	/// <summary>UDB's own real <c>GZShowEventLines</c>/<c>ViewSelectionEffects</c>, combined into one toggle here rather than UDB's real two independent settings - narrower than UDB, flagged in TODO.md rather than silently matched.</summary>
	public bool Enabled { get; set; } = true;

	public TagIndicatorOverlayHandler(MapOverlay owner, MapOverlayCamera camera, LinedefOverlayHandler linedefHandler, SectorOverlayHandler sectorHandler)
	{
		_owner = owner;
		_camera = camera;
		_linedefHandler = linedefHandler;
		_sectorHandler = sectorHandler;
	}

	public void Draw(CanvasItem target)
	{
		if (!Enabled) return;

		DrawSectorTagLabels(target);
		DrawHoverArrows(target);
	}

	/// <summary>UDB's own real always-visible-once-tagged behavior (not hover-gated) - <see cref="SectorBounds"/>'s own bbox-center anchor stands in for UDB's real precomputed label point, see this class's own remarks.</summary>
	private void DrawSectorTagLabels(CanvasItem target)
	{
		foreach (var sector in _owner.Map.Sectors)
		{
			var tags = MapDataTagQueries.ParseTags(sector.Fields).ToList();
			if (tags.Count == 0) continue;

			var text = tags.Count == 1 ? $"Tag {tags[0]}" : $"Tags {string.Join(", ", tags)}";
			var screenCenter = _camera.Project(SectorBounds.Compute(sector).Center);
			var textSize = ScreenLabel.Measure(text);
			var baseline = screenCenter + new Vector2(-textSize.X / 2f, textSize.Y / 2f);
			ScreenLabel.Draw(target, baseline, text, MapOverlayColors.InfoLine);
		}
	}

	/// <summary>
	/// Mirrors UDB's own real <c>Association.GetAssociations</c>: hovering
	/// a linedef (only live while Linedefs mode is actually engaged - see
	/// <see cref="LinedefOverlayHandler.Hovered"/>'s own remarks) finds
	/// every sector its own tag(s) reach and draws an arrow to each,
	/// filling the target; hovering a sector finds every linedef whose own
	/// tag(s) reach it and draws an arrow from each - the reverse
	/// direction never fills anything, matching UDB's own real
	/// <c>Association.Render</c> (only its forward, linedef-to-sector
	/// branch ever adds to its own <c>sectors</c> highlight list).
	/// </summary>
	private void DrawHoverArrows(CanvasItem target)
	{
		if (_owner.Mode == EditMode.Linedefs && _linedefHandler.Hovered is { } hoveredLinedef)
		{
			var linedefCenter = (hoveredLinedef.Start.Position + hoveredLinedef.End.Position) / 2f;

			foreach (var tag in MapDataTagQueries.ParseTags(hoveredLinedef.Fields))
			{
				foreach (var sector in _owner.Map.GetSectorsWithTag(tag))
				{
					SectorOverlayHandler.Fill(target, _camera, sector, SectorFillColor);
					DrawArrow(target, linedefCenter, SectorBounds.Compute(sector).Center);
				}
			}
		}
		else if (_owner.Mode == EditMode.Sectors && _sectorHandler.Hovered is { } hoveredSector)
		{
			var sectorCenter = SectorBounds.Compute(hoveredSector).Center;

			foreach (var tag in MapDataTagQueries.ParseTags(hoveredSector.Fields))
			{
				foreach (var linedef in _owner.Map.GetLinedefsWithTag(tag))
				{
					var linedefCenter = (linedef.Start.Position + linedef.End.Position) / 2f;
					DrawArrow(target, linedefCenter, sectorCenter);
				}
			}
		}
	}

	/// <summary>
	/// A straight line with an open "V" arrowhead at <paramref name="to"/> -
	/// UDB's own real <c>IRenderer2D.RenderArrows</c>, same real constants
	/// (16px length, 0.46 rad half-angle). Computed directly in projected
	/// screen space (this project's own established convention, matching
	/// <c>DrawOverlayHandler.DrawDirectionTick</c>) via a standard rotation
	/// formula rather than mirroring UDB's own map-space-plus-inverse-scale
	/// one, which assumes a coordinate handedness this project's own
	/// screen space (Y down) doesn't share - the two wings are just the
	/// incoming direction rotated <see cref="ArrowheadHalfAngleRadians"/>
	/// each way and swept back from the tip, the same real angle/length
	/// UDB uses either way.
	/// </summary>
	private void DrawArrow(CanvasItem target, MapVector2 from, MapVector2 to)
	{
		var screenFrom = _camera.Project(from);
		var screenTo = _camera.Project(to);
		target.DrawLine(screenFrom, screenTo, MapOverlayColors.InfoLine, LineWidth);

		var screenDelta = screenTo - screenFrom;
		if (screenDelta.LengthSquared() < 1f) return;

		var direction = screenDelta.Normalized();
		var wing1 = screenTo - Rotate(direction, ArrowheadHalfAngleRadians) * ArrowheadLengthPixels;
		var wing2 = screenTo - Rotate(direction, -ArrowheadHalfAngleRadians) * ArrowheadLengthPixels;
		target.DrawLine(screenTo, wing1, MapOverlayColors.InfoLine, LineWidth);
		target.DrawLine(screenTo, wing2, MapOverlayColors.InfoLine, LineWidth);
	}

	private static Vector2 Rotate(Vector2 v, float radians)
	{
		var cos = Mathf.Cos(radians);
		var sin = Mathf.Sin(radians);
		return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
	}
}
