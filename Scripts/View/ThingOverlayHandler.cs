using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using DoomArchitect.Rendering;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Everything about how a Thing behaves in the 2D view - hit-testing,
/// select/marquee/drag input (a Thing drags itself, like Vertex, via
/// <c>MapData.MoveThing</c>/<see cref="MoveThingCommand"/> rather than the
/// vertex-based commands every other mode uses), and drawing (the largest
/// single piece of this project's whole 2D-view code, ported from UDB's
/// real <c>Renderer2D.RenderThingsBatch</c> - see <see cref="Draw"/>'s own
/// remarks).
/// </summary>
public sealed class ThingOverlayHandler
{
	/// <summary>Inset between the square's own edge and the real sprite drawn inside it - matches UDB's own real small fixed inset (<c>THING_SPRITE_SHRINK</c>), not zero, so the sprite never visually merges into the square's own border.</summary>
	private const float SpriteInsetPixels = 2f;

	/// <summary>The Thing square's own corner radius - a DoomArchitect-specific softening UDB's own real square icon doesn't have, kept small (and scaled down further for a genuinely tiny square via the <c>screenRadius * 0.3f</c> cap at its own call site) so it still reads as "a square," not a rounded pill.</summary>
	private const float SquareCornerRadiusPixels = 3f;

	/// <summary>The shaft's own visible length, as a multiple of the square's own half-size - a fixed reach beyond wherever <see cref="DrawArrow"/> determines the square's own true edge to be along the facing direction, so the shaft reads the same length whether the thing faces a side or a corner.</summary>
	private const float ArrowShaftLengthMultiplier = 0.35f;

	/// <summary>Small gap between the square's own true edge (see <see cref="DrawArrow"/>'s own <c>edgeDistance</c> calculation) and where the shaft actually starts, as a multiple of the square's own half-size - keeps the shaft from visually touching the square's own border.</summary>
	private const float ArrowGapMultiplier = 0.08f;

	/// <summary>The V-shaped arrowhead wings' own length, as a multiple of the square's own half-size - independent of the (now short) shaft length so the wings stay a readable size regardless.</summary>
	private const float ArrowHeadLengthMultiplier = 0.3f;

	/// <summary>The V-shaped arrowhead's own half-angle - wider than a typical arrowhead so the two wings read clearly even at this small a size.</summary>
	private const float ArrowHeadAngleDegrees = 35f;

	/// <summary>Stroke width for <see cref="DrawArrow"/>'s own stick-figure lines - thin on purpose, not a filled/outlined shape at all.</summary>
	private const float ArrowLineWidth = 1f;

	private const float ArrowOutlineWidth = 3f;

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;
	private readonly ElementOverlayHandler<Thing, Thing> _input;

	public ThingOverlayHandler(MapOverlay owner, MapOverlayCamera camera, MarqueeSelector marquee)
	{
		_owner = owner;
		_camera = camera;
		_input = new ElementOverlayHandler<Thing, Thing>(
			camera, marquee, () => _owner.UndoStack, _owner.SnapIfEnabled,
			FindNear, t => t.IsSelected,
			t => _owner.Map.SelectOnly(t), t => _owner.Map.ToggleSelect(t), () => _owner.Map.ClearSelectedThings(),
			() => _owner.Map.GetSelectedThings(), t => t.Position, (t, p) => _owner.Map.MoveThing(t, p),
			(t, oldPos, newPos) => new MoveThingCommand(_owner.Map, t, oldPos, newPos),
			(min, max, mode) => _owner.Map.MarqueeSelectThings(min, max, mode),
			onDoubleClick: t => _owner.RaiseEditThingsRequested(_owner.Map.GetSelectedThings().ToList()));
	}

	public void HandleInput(InputEvent @event) => _input.HandleInput(@event);

	/// <summary>
	/// Picks against each Thing's own real on-screen radius rather than a
	/// small fixed pick radius like <see cref="VertexOverlayHandler"/>'s
	/// own - a Thing's footprint varies hugely by type (a Spider
	/// Mastermind vs. a key), so "click what you see" is truer here than a
	/// uniform hit circle would be for the big ones.
	/// </summary>
	private Thing FindNear(Vector2 screenPosition)
	{
		Thing closest = null;
		var closestDistance = float.MaxValue;
		foreach (var thing in _owner.Map.Things)
		{
			var radius = _owner.GameConfiguration?.GetThingType(thing.Type)?.Radius ?? ThingMeshBuilder.FallbackRadius;
			var screenRadius = _camera.WorldSizeToScreenPixels(radius);
			var distance = _camera.Project(thing.Position).DistanceTo(screenPosition);
			if (distance <= screenRadius && distance < closestDistance)
			{
				closest = thing;
				closestDistance = distance;
			}
		}

		return closest;
	}

	/// <summary>
	/// Ported from UDB's own real <c>Renderer2D.RenderThingsBatch</c>
	/// (verified directly, not guessed): a square (not circle - "things
	/// are square in Doom"), sized to the type's own real radius; the
	/// actual decoded sprite drawn on top at its own native colors and
	/// aspect ratio (never rotated to <see cref="Thing.Angle"/> - a Doom
	/// sprite's own facing is baked into *which rotation frame* is shown,
	/// resolved live per-thing via
	/// <see cref="SpriteIconCache.GetOrDecodeRotationFrame"/>/
	/// <see cref="Core.Textures.TextureSet.ResolveSpriteRotations"/>, not
	/// by rotating a fixed image); a small separate arrow only for a type
	/// that actually has a meaningful facing
	/// (<see cref="ThingTypeInfo.ShowsDirection"/>), rotated to
	/// <see cref="Thing.Angle"/> since it's the one element that genuinely
	/// needs to point somewhere. Unlike UDB's own bundled
	/// <c>ThingTexture2D.png</c> atlas art, the square here is a plain
	/// flat fill - this project's own original choice of exactly how to
	/// draw "a square", not a claim about matching UDB's own bundled
	/// pixels.
	///
	/// The hovered thing is drawn in its own separate pass, after every
	/// other thing, so it's always on top regardless of where it happens
	/// to sit in <see cref="MapData.Things"/>'s own list order (which
	/// otherwise decides paint order outright - later in the list draws
	/// over earlier, no other sorting at all) - matches UDB's own real
	/// <c>Renderer2D.RenderThingsBatch</c>, confirmed directly: its own
	/// main pass explicitly skips <c>t.Highlighted</c>
	/// (<c>if(!fixedcolor &amp;&amp; t.Highlighted) continue;</c>) and
	/// renders it separately afterward for the exact same reason - two
	/// overlapping things at similar screen positions should never let
	/// list order hide the one actually being pointed at.
	/// </summary>
	public void Draw(CanvasItem target)
	{
		var alpha = _owner.Mode == EditMode.Things ? 1f : MapOverlayColors.InactiveModeAlpha;
		var hovered = _owner.Mode == EditMode.Things ? _input.Hovered : null;

		foreach (var thing in _owner.Map.Things)
		{
			if (thing == hovered) continue;
			DrawOne(target, thing, alpha, isHighlighted: false);
		}

		if (hovered != null) DrawOne(target, hovered, alpha, isHighlighted: true);
	}

	private void DrawOne(CanvasItem target, Thing thing, float alpha, bool isHighlighted)
	{
		var info = _owner.GameConfiguration?.GetThingType(thing.Type);
		var radius = info?.Radius ?? ThingMeshBuilder.FallbackRadius;
		var screenRadius = _camera.WorldSizeToScreenPixels(radius);
		var diameter = screenRadius * 2f;

		// Hovered/dragged swaps the category tint for HoverColor
		// outright, the same treatment vertices/linedefs already use,
		// rather than a new visual language just for Things.
		var tint = isHighlighted ? MapOverlayColors.Hover
			: thing.IsSelected ? MapOverlayColors.Selected
			: ThingCategoryColors.Get(info?.ColorIndex ?? 0);

		var center = _camera.Project(thing.Position);
		var square = new Rect2(center - new Vector2(screenRadius, screenRadius), new Vector2(diameter, diameter));

		DrawRoundedRect(target, square, Mathf.Min(SquareCornerRadiusPixels, screenRadius * 0.3f), new Color(tint, alpha));
		DrawSprite(target, thing, info, square, alpha);

		// A type that doesn't actually rotate in gameplay (most
		// pickups/decorations) gets no arrow at all - showing a facing
		// indicator for something with no meaningful facing is
		// actively misleading, not just unnecessary detail. Unrecognized
		// types keep the arrow, matching this project's existing "assume
		// nothing" default from before per-type data existed.
		if (info is not { ShowsDirection: false }) DrawArrow(target, thing, center, screenRadius, alpha);
	}

	/// <summary>
	/// UDB's own real per-angle rotation-frame selection
	/// (<c>General.ClampAngle(-t.AngleDoom + 270) / 45</c>, verified
	/// directly against <c>Renderer2D.RenderThingsBatch</c>) - two things
	/// of the identical type facing different directions genuinely show
	/// different decoded sprite frames, not the same image rotated. Drawn
	/// at its own real aspect ratio (never stretched to fill the square)
	/// and native colors (never tinted by the category color, matching
	/// UDB's own real behavior) - a missing/undecoded sprite simply
	/// leaves the plain colored square with no overlay, same "still
	/// renders something, just less detail" fallback this project's own
	/// texture previews already use elsewhere.
	/// </summary>
	private void DrawSprite(CanvasItem target, Thing thing, ThingTypeInfo info, Rect2 square, float alpha)
	{
		if (_owner.SpriteIconCache == null || string.IsNullOrEmpty(info?.SpriteName)) return;

		var angleIndex = (((-thing.Angle + 270) % 360) + 360) % 360 / 45;
		var (icon, mirror) = _owner.SpriteIconCache.GetOrDecodeRotationFrame(info.SpriteName, angleIndex);
		if (icon == null) return;

		var bounds = square.Grow(-SpriteInsetPixels);
		if (bounds.Size.X <= 0f || bounds.Size.Y <= 0f) return;

		var iconSize = icon.GetSize();
		var scale = bounds.Size.X / Mathf.Max(iconSize.X, iconSize.Y);
		var drawSize = iconSize * scale;

		target.DrawSetTransform(bounds.GetCenter(), 0f, new Vector2(mirror ? -1f : 1f, 1f));
		target.DrawTextureRect(icon, new Rect2(-drawSize / 2f, drawSize), false, new Color(1f, 1f, 1f, alpha));
		target.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
	}

	/// <summary>
	/// A thin "stick" arrow (a shaft plus a two-line V-shaped head, no fill
	/// and no closed outline shape at all) pointing in
	/// <see cref="Thing.Angle"/>'s own facing direction - UDB's own real
	/// arrow is a separate element drawn in addition to (not instead of)
	/// the rotation-aware sprite, confirmed directly against
	/// <c>Renderer2D.CreateThingArrowVerts</c>. The stick shape itself,
	/// and its exact geometry, are DoomArchitect-specific choices, not
	/// UDB's own real ones: a filled triangle sat on top of the sprite and
	/// hid whatever was underneath instead of just pointing at it, and a
	/// shaft starting at the thing's own center ran back across the
	/// sprite too - the shaft now starts just past the square's own true
	/// edge along the facing direction (see this method's own
	/// <c>edgeDistance</c>/<see cref="ArrowGapMultiplier"/>) and reaches
	/// only a short, fixed way further out regardless of that direction
	/// (<see cref="ArrowShaftLengthMultiplier"/>), with wider wings
	/// (<see cref="ArrowHeadAngleDegrees"/>) than a typical arrowhead so
	/// they stay readable at this small a size.
	///
	/// Colored white-on-black (a wider black pass first, a thinner white
	/// pass on top of the exact same lines) rather than one flat color -
	/// verified directly against UDB's own real
	/// <c>CreateThingArrowVerts</c>, whose own vertex color is packed
	/// opaque white (<c>verts[offset].c = -1</c>), drawn from an icon
	/// atlas whose own art already bakes in a black outline for contrast
	/// against any background; reproduced here as an actual two-pass
	/// outlined stroke instead, since this project draws the arrow as
	/// plain geometry rather than a textured atlas sprite. Plain white
	/// alone (or plain black alone, tried first) reads poorly against
	/// whichever half of the map view happens to share that same tone -
	/// the outline keeps it visible against both. Never the category/
	/// hover/selection tint the square uses - it only ever needs to read
	/// as "a facing indicator," not carry any of that state itself.
	/// </summary>
	private void DrawArrow(CanvasItem target, Thing thing, Vector2 center, float screenRadius, float alpha)
	{
		var facingAngle = -Mathf.DegToRad(thing.Angle);

		// The square isn't a circle, so its own true edge distance from
		// center varies with direction - farther out at a corner (up to
		// screenRadius * sqrt(2)) than at a side (exactly screenRadius).
		// Using a fixed radial distance here (an earlier version of this
		// method did) put the shaft's own start point outside the square
		// when facing a side but inside it when facing a corner - this is
		// the standard "distance from center to a square's own boundary
		// along a given direction" formula instead, so the shaft starts
		// just past the real edge regardless of which way the thing faces.
		var edgeDistance = screenRadius / Mathf.Max(Mathf.Abs(Mathf.Cos(facingAngle)), Mathf.Abs(Mathf.Sin(facingAngle)));
		var startDistance = edgeDistance + screenRadius * ArrowGapMultiplier;
		var tipDistance = startDistance + screenRadius * ArrowShaftLengthMultiplier;

		var shaftStart = new Vector2(startDistance, 0f);
		var tip = new Vector2(tipDistance, 0f);
		var headLength = screenRadius * ArrowHeadLengthMultiplier;
		var headAngle = Mathf.DegToRad(ArrowHeadAngleDegrees);

		var headLeft = tip - new Vector2(headLength * Mathf.Cos(headAngle), headLength * Mathf.Sin(headAngle));
		var headRight = tip - new Vector2(headLength * Mathf.Cos(headAngle), -headLength * Mathf.Sin(headAngle));

		target.DrawSetTransform(center, facingAngle, Vector2.One);

		foreach (var (color, width) in new[] { (new Color(Colors.Black, alpha), ArrowOutlineWidth), (new Color(Colors.White, alpha), ArrowLineWidth) })
		{
			target.DrawLine(shaftStart, tip, color, width, true);
			target.DrawLine(tip, headLeft, color, width, true);
			target.DrawLine(tip, headRight, color, width, true);
		}

		target.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
	}

	/// <summary>
	/// Godot's own <see cref="CanvasItem.DrawRect"/> has no corner-radius
	/// parameter at all (that only exists on <see cref="StyleBoxFlat"/>,
	/// a <c>Control</c> theming resource, not an immediate-mode draw call)
	/// - built by hand instead, as a filled polygon: each corner's own
	/// quarter-circle arc (<paramref name="segmentsPerCorner"/> straight
	/// segments each - a handful is already smooth at this small an icon
	/// size, no need for a high segment count) traced in order around the
	/// rect, connected corner-to-corner by the straight edges implicitly
	/// (a polygon closes on its own between its last and first point, so
	/// the edges themselves need no separate points beyond each arc's own
	/// start/end).
	/// </summary>
	private static void DrawRoundedRect(CanvasItem target, Rect2 rect, float radius, Color color, int segmentsPerCorner = 4)
	{
		radius = Mathf.Max(0f, Mathf.Min(radius, Mathf.Min(rect.Size.X, rect.Size.Y) / 2f));
		if (radius <= 0.01f)
		{
			target.DrawRect(rect, color);
			return;
		}

		var corners = new (Vector2 Center, float StartAngle)[]
		{
			(rect.Position + new Vector2(radius, radius), Mathf.Pi),
			(rect.Position + new Vector2(rect.Size.X - radius, radius), -Mathf.Pi / 2f),
			(rect.Position + rect.Size - new Vector2(radius, radius), 0f),
			(rect.Position + new Vector2(radius, rect.Size.Y - radius), Mathf.Pi / 2f),
		};

		var points = new List<Vector2>((segmentsPerCorner + 1) * corners.Length);
		foreach (var (cornerCenter, startAngle) in corners)
		{
			for (var i = 0; i <= segmentsPerCorner; i++)
			{
				var angle = startAngle + Mathf.Pi / 2f * (i / (float)segmentsPerCorner);
				points.Add(cornerCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
			}
		}

		target.DrawColoredPolygon(points.ToArray(), color);
	}
}
