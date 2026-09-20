using System;
using System.Collections.Generic;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;
using MapVector2 = System.Numerics.Vector2;

namespace DoomArchitect.Rendering;

/// <summary>
/// Every highlighted surface in the 3D view: the current hover/crosshair
/// target, plus every selected Sector/Linedef/Thing - rebuilt as a pool of
/// child <see cref="MeshInstance3D"/>s each time <see cref="UpdateHighlights"/>
/// is called (see its own remarks), rather than as one single mesh the
/// way this class originally worked when it only ever showed one target
/// at a time. Reuses the exact same Core.Geometry triangulation already
/// used for the real geometry, offset slightly to avoid z-fighting - a
/// Thing highlight instead draws its own real pick-box dimensions (see
/// <see cref="AddThing"/>'s own remarks), which never coincides with any
/// opaque surface, so it needs no such offset.
/// </summary>
public partial class TargetHighlight : Node3D
{
    // Small enough to stay basically imperceptible face-on, but still
    // enough separation from the real geometry to avoid z-fighting at
    // typical view distances - 0.5 was clearing the depth test with a lot
    // to spare, which is exactly what made the highlight visibly float
    // off the surface at a shallow/grazing viewing angle (the same
    // absolute offset reads as a much bigger apparent gap once
    // foreshortened almost edge-on).
    private const float FloorCeilingOffset = 0.1f;
    private const float WallOffset = 0.1f;

    private static readonly Color HoverColor = new(1f, 0.5f, 0f, 0.03f);

    /// <summary>
    /// Same red hue as <c>MapOverlay.SelectedColor</c> (kept in sync by
    /// convention, not a shared constant - this is a translucent 3D
    /// surface fill covering large areas, tuned to a much lower alpha
    /// than the 2D view's opaque icon tint needs).
    /// </summary>
    private static readonly Color SelectedColor = new(0.9f, 0.15f, 0.15f, 0.05f);

    private readonly List<MeshInstance3D> _highlightInstances = new();
    private StandardMaterial3D _hoverMaterial;
    private StandardMaterial3D _selectedMaterial;

    /// <summary>Same lookup <c>MapView</c> already gives its <see cref="MapRaycaster"/> - needed to size a two-sided linedef's masked middle texture. Set once from <c>MapView._Ready</c>.</summary>
    public Func<string, double> MiddleTextureHeightLookup { get; set; }

    /// <summary>Same lookup <c>MapView</c> already gives its <see cref="MapRaycaster"/> - needed to draw a Thing's own real pick-box dimensions. Set once from <c>MapView._Ready</c>.</summary>
    public Func<Thing, ThingPickBounds> ThingPickBoundsLookup { get; set; }

    public override void _Ready()
    {
        _hoverMaterial = BuildMaterial(HoverColor);
        _selectedMaterial = BuildMaterial(SelectedColor);
    }

    private static StandardMaterial3D BuildMaterial(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        // The real wall/floor/ceiling meshes are double-sided via two
        // opposite-wound triangles each (see DoubleSidedMesh) so their
        // normals stay correct for lighting - this highlight has no
        // lighting to get right (Unshaded), so simply disabling
        // backface culling is enough to stay visible from both sides.
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        // A Thing's own box highlight (see AddThing) genuinely surrounds
        // its billboard sprite - two transparent objects at overlapping
        // depths, which Godot's default distance-from-camera sort has no
        // reliable way to order consistently (the box's own faces span
        // both nearer and farther than the sprite depending on viewing
        // angle, so its centroid-based sort position flips back and forth
        // as the camera moves). Explicitly lower than every other
        // transparent material's own default (0) forces this to always
        // draw first regardless of distance, so the sprite - drawn after -
        // always composites on top instead of flickering between the two,
        // matching what a "here's the box, and here's the thing inside
        // it, always visible" highlight should look like.
        RenderPriority = -1,
    };

    /// <summary>
    /// Rebuilds every highlighted surface from scratch on each call
    /// (rather than diffing against the previous call) - simplest correct
    /// approach, and cheap enough at the ~80ms pick cadence <c>MapView</c>
    /// already throttles this to for realistic selection sizes. Hover
    /// always wins visually over selection: an element that's both the
    /// hover target and selected is drawn once, in the hover material
    /// only - the same "hover always wins, no blended state" rule
    /// <c>MapOverlay</c>'s 2D view uses.
    /// </summary>
    public void UpdateHighlights(
        MapTarget? hoverTarget, IEnumerable<Sector> selectedSectors, IEnumerable<Linedef> selectedLinedefs,
        IEnumerable<Thing> selectedThings, MapVector2 viewerPosition)
    {
        foreach (var instance in _highlightInstances) instance.QueueFree();
        _highlightInstances.Clear();

        var hoverSector = hoverTarget is { Kind: TargetSurfaceKind.Floor or TargetSurfaceKind.Ceiling }
            ? hoverTarget.Value.Sector
            : null;
        var hoverLinedef = hoverTarget is { Kind: TargetSurfaceKind.Wall }
            ? hoverTarget.Value.WallSegment!.Value.Side.Linedef
            : null;
        var hoverThing = hoverTarget is { Kind: TargetSurfaceKind.Thing }
            ? hoverTarget.Value.Thing
            : null;

        foreach (var sector in selectedSectors)
        {
            if (sector == hoverSector) continue;
            AddFlat(sector, sector.FloorHeight, FloorCeilingOffset, _selectedMaterial);
            AddFlat(sector, sector.CeilingHeight, -FloorCeilingOffset, _selectedMaterial);
        }

        foreach (var linedef in selectedLinedefs)
        {
            if (linedef == hoverLinedef) continue;
            foreach (var segment in LinedefWallBuilder.Build(linedef, MiddleTextureHeightLookup))
            {
                AddWall(segment, viewerPosition, _selectedMaterial);
            }
        }

        foreach (var thing in selectedThings)
        {
            if (thing == hoverThing) continue;
            AddThing(thing, _selectedMaterial);
        }

        switch (hoverTarget)
        {
            case { Kind: TargetSurfaceKind.Floor } target:
                AddFlat(target.Sector!, target.Sector!.FloorHeight, FloorCeilingOffset, _hoverMaterial);
                break;
            case { Kind: TargetSurfaceKind.Ceiling } target:
                AddFlat(target.Sector!, target.Sector!.CeilingHeight, -FloorCeilingOffset, _hoverMaterial);
                break;
            case { Kind: TargetSurfaceKind.Wall } target:
                AddWall(target.WallSegment!.Value, viewerPosition, _hoverMaterial);
                break;
            case { Kind: TargetSurfaceKind.Thing } target:
                AddThing(target.Thing!, _hoverMaterial);
                break;
        }
    }

    private void AddFlat(Sector sector, double height, float offset, StandardMaterial3D material)
    {
        var polygons = PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(sector)));

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var polygon in polygons)
        {
            foreach (var (a, b, c) in EarClipper.Clip(polygon))
            {
                surfaceTool.AddVertex(a.ToWorld((float)height + offset));
                surfaceTool.AddVertex(b.ToWorld((float)height + offset));
                surfaceTool.AddVertex(c.ToWorld((float)height + offset));
            }
        }

        surfaceTool.GenerateNormals();
        AddInstance(surfaceTool.Commit(), material);
    }

    /// <summary>
    /// <paramref name="viewerPosition"/> (map-space XY, e.g. the camera's
    /// own position) decides which way to nudge the highlight off the
    /// wall's exact plane: always toward whichever side is actually being
    /// looked from. A single fixed perpendicular direction would push the
    /// highlight behind the wall's own opaque geometry - invisible, since
    /// a translucent quad loses the depth test against an opaque surface
    /// in front of it - for every wall whose Start-End winding happens to
    /// point away from the viewer, which in practice is most/all of them
    /// at once (real maps wind their sector boundaries consistently, so a
    /// fixed choice isn't a coin flip per wall, it's the same wrong answer
    /// every time). Caught after highlighting silently failed for every
    /// wall in a real map.
    /// </summary>
    private void AddWall(WallSegment segment, MapVector2 viewerPosition, StandardMaterial3D material)
    {
        var direction = segment.End.Position - segment.Start.Position;
        var length = direction.Length();
        var normal = length > 0 ? new MapVector2(-direction.Y, direction.X) / length : MapVector2.Zero;

        var midpoint = (segment.Start.Position + segment.End.Position) / 2f;
        if (MapVector2.Dot(normal, viewerPosition - midpoint) < 0) normal = -normal;

        var offsetStart = segment.Start.Position + normal * WallOffset;
        var offsetEnd = segment.End.Position + normal * WallOffset;

        var startBottom = offsetStart.ToWorld((float)segment.Bottom);
        var startTop = offsetStart.ToWorld((float)segment.Top);
        var endBottom = offsetEnd.ToWorld((float)segment.Bottom);
        var endTop = offsetEnd.ToWorld((float)segment.Top);

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        surfaceTool.AddVertex(startBottom);
        surfaceTool.AddVertex(startTop);
        surfaceTool.AddVertex(endTop);
        surfaceTool.AddVertex(startBottom);
        surfaceTool.AddVertex(endTop);
        surfaceTool.AddVertex(endBottom);
        surfaceTool.GenerateNormals();

        AddInstance(surfaceTool.Commit(), material);
    }

    /// <summary>
    /// A translucent axis-aligned box matching the Thing's own real pick
    /// bounds (<see cref="ThingPickBoundsLookup"/> - same dimensions
    /// <see cref="MapRaycaster"/> actually hit-tests against, so this is
    /// an honest "here's what you're clicking" affordance, not just a
    /// decorative marker) - the same idea as UDB's own real "thing cage"
    /// display, just filled rather than wireframe to match this class's
    /// existing flat/wall highlight look. Deliberately axis-aligned, not
    /// billboarded to the camera the way the Thing's own rendered sprite
    /// is - matching the pick box itself, which isn't billboarded either.
    /// A no-op if <see cref="ThingPickBoundsLookup"/> was never wired up.
    /// </summary>
    private void AddThing(Thing thing, StandardMaterial3D material)
    {
        if (ThingPickBoundsLookup == null) return;
        var bounds = ThingPickBoundsLookup(thing);

        var min = new MapVector2((float)(thing.Position.X - bounds.Radius), (float)(thing.Position.Y - bounds.Radius));
        var max = new MapVector2((float)(thing.Position.X + bounds.Radius), (float)(thing.Position.Y + bounds.Radius));
        var bottom = (float)bounds.WorldZ;
        var top = (float)(bounds.WorldZ + bounds.Height);

        var p000 = new MapVector2(min.X, min.Y).ToWorld(bottom);
        var p100 = new MapVector2(max.X, min.Y).ToWorld(bottom);
        var p010 = new MapVector2(min.X, max.Y).ToWorld(bottom);
        var p110 = new MapVector2(max.X, max.Y).ToWorld(bottom);
        var p001 = new MapVector2(min.X, min.Y).ToWorld(top);
        var p101 = new MapVector2(max.X, min.Y).ToWorld(top);
        var p011 = new MapVector2(min.X, max.Y).ToWorld(top);
        var p111 = new MapVector2(max.X, max.Y).ToWorld(top);

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        AddQuad(surfaceTool, p000, p100, p110, p010); // bottom
        AddQuad(surfaceTool, p001, p011, p111, p101); // top
        AddQuad(surfaceTool, p000, p010, p011, p001); // -X side
        AddQuad(surfaceTool, p100, p101, p111, p110); // +X side
        AddQuad(surfaceTool, p000, p001, p101, p100); // -Y side
        AddQuad(surfaceTool, p010, p110, p111, p011); // +Y side
        surfaceTool.GenerateNormals();

        AddInstance(surfaceTool.Commit(), material);
    }

    private static void AddQuad(SurfaceTool surfaceTool, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        surfaceTool.AddVertex(a);
        surfaceTool.AddVertex(b);
        surfaceTool.AddVertex(c);
        surfaceTool.AddVertex(a);
        surfaceTool.AddVertex(c);
        surfaceTool.AddVertex(d);
    }

    private void AddInstance(ArrayMesh mesh, StandardMaterial3D material)
    {
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        AddChild(instance);
        _highlightInstances.Add(instance);
    }
}
