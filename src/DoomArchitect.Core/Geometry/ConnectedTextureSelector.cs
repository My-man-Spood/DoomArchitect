using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// "Select connected walls/flats sharing the same texture (and/or the
/// same height, for flats)" - UDB's own real
/// <c>BaseVisualGeometrySidedef.SelectNeighbours</c>/
/// <c>VisualFloor.SelectNeighbours</c>/<c>VisualCeiling.SelectNeighbours</c>.
/// UDB's own two modifiers (Shift=texture, Ctrl=height, independently
/// combinable - holding both requires both) are remapped here: Shift is
/// already this project's 3D fly-down camera control, and it's a real,
/// reported problem for *any* modifier use in 3D mode, not just a held-
/// key one - holding it to click can itself drift the camera down and
/// change what the click actually lands on before the click is even
/// processed. Texture-match is Ctrl+Click here instead; height-match is
/// Alt+Click (free, and UDB's own real use of Alt for this gesture -
/// <c>stopatselected</c>, a flood-fill termination tweak - isn't ported,
/// so there's no competing meaning to preserve). Height-matching is
/// flats-only for now - UDB supports it for walls too (comparing a wall
/// part's own visible rect height/position rather than a sector plane),
/// but it wasn't asked for and isn't ported here.
/// </summary>
public static class ConnectedTextureSelector
{
    private readonly record struct WalkJob(Sidedef Side, bool Forward);

    /// <summary>
    /// Every sidedef reachable from <paramref name="startSide"/> by
    /// walking outward along shared vertices to connected linedefs,
    /// whose own same part role (<paramref name="part"/>) carries the
    /// identical texture name - the exact same traversal
    /// <see cref="TextureAutoAligner"/> already uses (forward/backward
    /// job queue, resolving which of a two-sided linedef's Front/Back
    /// actually "continues" from a given vertex in a given walk
    /// direction), just without its own offset-accumulation payload,
    /// since a plain selection has nothing to accumulate. Scoped the
    /// same way <see cref="TextureAutoAligner"/> already is: same-role-
    /// only (upper-to-upper, lower-to-lower, middle-to-middle - no UDB-
    /// style cross-role chaining via its own <c>VisualSidedefParts</c>
    /// triangle-count machinery this project has no equivalent of), no
    /// 3D-floor (<c>middle3d</c>) participation.
    /// </summary>
    public static IReadOnlyList<Sidedef> FindConnectedWalls(Sidedef startSide, WallPartKind part, Func<string, double> textureHeightLookup)
    {
        var textureName = LinedefWallBuilder.GetPartTexture(startSide, part);
        if (textureName == "-") return Array.Empty<Sidedef>();

        var visited = new HashSet<Sidedef>();
        var stack = new Stack<WalkJob>();
        stack.Push(new WalkJob(startSide, Forward: true));

        while (stack.Count > 0)
        {
            var job = stack.Pop();
            if (visited.Contains(job.Side)) continue;
            visited.Add(job.Side);

            var linedef = job.Side.Linedef;
            var startVertex = job.Side.IsFront ? linedef.Start : linedef.End;
            var endVertex = job.Side.IsFront ? linedef.End : linedef.Start;

            PushNeighbors(stack, startVertex, forward: false, part, textureName, visited, textureHeightLookup);
            PushNeighbors(stack, endVertex, forward: true, part, textureName, visited, textureHeightLookup);
        }

        return visited.ToList();
    }

    private static void PushNeighbors(
        Stack<WalkJob> stack, Vertex v, bool forward, WallPartKind part, string textureName,
        HashSet<Sidedef> visited, Func<string, double> textureHeightLookup)
    {
        foreach (var linedef in v.Linedefs)
        {
            var side1 = forward ? linedef.Front : linedef.Back;
            var side2 = forward ? linedef.Back : linedef.Front;
            if ((side1 != null && visited.Contains(side1)) || (side2 != null && visited.Contains(side2))) continue;

            if (linedef.Start == v && side1 != null)
            {
                if (PartVisibleAndMatches(linedef, side1, part, textureName, textureHeightLookup)) stack.Push(new WalkJob(side1, forward));
            }
            else if (linedef.End == v && side2 != null)
            {
                if (PartVisibleAndMatches(linedef, side2, part, textureName, textureHeightLookup)) stack.Push(new WalkJob(side2, forward));
            }
        }
    }

    private static bool PartVisibleAndMatches(Linedef linedef, Sidedef side, WallPartKind part, string textureName, Func<string, double> textureHeightLookup)
    {
        foreach (var segment in LinedefWallBuilder.Build(linedef, textureHeightLookup))
        {
            if (segment.Side == side && segment.PartKind == part && segment.Texture == textureName) return true;
        }

        return false;
    }

    /// <summary>UDB's own real floating-point tolerance for "is this the same plane height" (<c>VisualFloor.ArePlanesSame</c>) - this project models no sector slopes, so the comparison reduces to just the plain Floor/CeilingHeight scalar, never a normal vector.</summary>
    private const double HeightEpsilon = 0.001;

    /// <summary>
    /// Every sector reachable from <paramref name="startSector"/> by
    /// walking across shared linedefs to each bordering sector, whose
    /// own floor (or ceiling, matching <paramref name="isFloor"/>)
    /// texture name and/or height matches the start's - UDB's own real
    /// sector-graph walk (<c>VisualFloor</c>/<c>VisualCeiling.SelectNeighbours</c>
    /// walks adjacent sectors via each shared sidedef's "other" side, not
    /// a vertex/linedef chain the way the wall walk above does, since a
    /// flat is naturally bounded by whichever sectors border it
    /// directly). <paramref name="matchTexture"/>/<paramref name="matchHeight"/>
    /// are independent, combinable criteria exactly like UDB's own real
    /// Shift(texture)/Ctrl(height) - both true means a neighbor must
    /// satisfy *both* to extend the walk, not either. No 3D-floor/vavoom
    /// participation, matching the wall walk's own same scope note.
    /// </summary>
    public static IReadOnlyList<Sector> FindConnectedSectors(Sector startSector, bool isFloor, bool matchTexture, bool matchHeight)
    {
        if (!matchTexture && !matchHeight) return Array.Empty<Sector>();

        var textureName = isFloor ? startSector.FloorTexture : startSector.CeilingTexture;
        var height = isFloor ? startSector.FloorHeight : startSector.CeilingHeight;

        var visited = new HashSet<Sector> { startSector };
        var stack = new Stack<Sector>();
        stack.Push(startSector);

        while (stack.Count > 0)
        {
            var sector = stack.Pop();
            foreach (var side in sector.Sidedefs)
            {
                var other = side.Linedef.Front == side ? side.Linedef.Back : side.Linedef.Front;
                var otherSector = other?.Sector;
                if (otherSector == null || visited.Contains(otherSector)) continue;

                if (matchTexture)
                {
                    var otherTexture = isFloor ? otherSector.FloorTexture : otherSector.CeilingTexture;
                    if (otherTexture != textureName) continue;
                }

                if (matchHeight)
                {
                    var otherHeight = isFloor ? otherSector.FloorHeight : otherSector.CeilingHeight;
                    if (Math.Abs(otherHeight - height) >= HeightEpsilon) continue;
                }

                visited.Add(otherSector);
                stack.Push(otherSector);
            }
        }

        return visited.ToList();
    }
}
