using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Geometry;

public class LinedefWallBuilderTests
{
    private static (MapData Map, Vertex A, Vertex B) TwoVertices()
    {
        var map = new MapData();
        var a = map.CreateVertex(new Vector2(0, 0));
        var b = map.CreateVertex(new Vector2(64, 0));
        return (map, a, b);
    }

    [Fact]
    public void Build_OneSidedLinedef_SpansFloorToCeiling()
    {
        var (map, a, b) = TwoVertices();
        var sector = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, sector, null);
        linedef.Front!.MiddleTexture = "STARTAN2";

        var segments = LinedefWallBuilder.Build(linedef);

        var segment = Assert.Single(segments);
        Assert.Equal(0, segment.Bottom);
        Assert.Equal(128, segment.Top);
        Assert.Equal("STARTAN2", segment.Texture);
    }

    [Fact]
    public void Build_OneSidedLinedef_BackSideOnly_StillBuildsAWall()
    {
        var (map, a, b) = TwoVertices();
        var sector = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, null, sector);

        var segments = LinedefWallBuilder.Build(linedef);

        var segment = Assert.Single(segments);
        Assert.Equal(0, segment.Bottom);
        Assert.Equal(128, segment.Top);
    }

    [Fact]
    public void Build_OneSidedLinedef_ZeroHeightSector_ProducesNoWall()
    {
        var (map, a, b) = TwoVertices();
        var sector = map.CreateSector(64, 64);
        var linedef = map.CreateLinedef(a, b, sector, null);

        Assert.Empty(LinedefWallBuilder.Build(linedef));
    }

    [Fact]
    public void Build_TwoSided_FrontCeilingHigher_AddsUpperWallOnFrontOnly()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 96);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.UpperTexture = "BROWN1";

        var segments = LinedefWallBuilder.Build(linedef);

        var upper = Assert.Single(segments);
        Assert.Equal(96, upper.Bottom);
        Assert.Equal(128, upper.Top);
        Assert.Equal("BROWN1", upper.Texture);
    }

    [Fact]
    public void Build_TwoSided_BackCeilingHigher_AddsUpperWallOnBackOnly()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 96);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Back!.UpperTexture = "BROWN1";

        var segments = LinedefWallBuilder.Build(linedef);

        var upper = Assert.Single(segments);
        Assert.Equal(96, upper.Bottom);
        Assert.Equal(128, upper.Top);
        Assert.Equal("BROWN1", upper.Texture);
    }

    [Fact]
    public void Build_TwoSided_EqualCeilings_AddsNoUpperWall()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);

        Assert.Empty(LinedefWallBuilder.Build(linedef));
    }

    [Fact]
    public void Build_TwoSided_FrontFloorLower_AddsLowerWallOnFrontOnly()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(32, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.LowerTexture = "SUPPORT2";

        var segments = LinedefWallBuilder.Build(linedef);

        var lower = Assert.Single(segments);
        Assert.Equal(0, lower.Bottom);
        Assert.Equal(32, lower.Top);
        Assert.Equal("SUPPORT2", lower.Texture);
    }

    [Fact]
    public void Build_TwoSided_BackFloorLower_AddsLowerWallOnBackOnly()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(32, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Back!.LowerTexture = "SUPPORT2";

        var segments = LinedefWallBuilder.Build(linedef);

        var lower = Assert.Single(segments);
        Assert.Equal(0, lower.Bottom);
        Assert.Equal(32, lower.Top);
        Assert.Equal("SUPPORT2", lower.Texture);
    }

    [Fact]
    public void Build_TwoSided_EqualFloors_AddsNoLowerWall()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);

        Assert.Empty(LinedefWallBuilder.Build(linedef));
    }

    [Fact]
    public void Build_TwoSided_StepInBothFloorAndCeiling_ProducesBothWallsOnCorrectSides()
    {
        // A classic Doom "step": front is the higher/outer platform,
        // back is the lower/inner room - front sees a lower wall (down
        // to the inner floor) and an upper wall (up from the inner
        // ceiling), simultaneously.
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(16, 200);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);

        var segments = LinedefWallBuilder.Build(linedef);

        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.Bottom == 128 && s.Top == 200); // front upper
        Assert.Contains(segments, s => s.Bottom == 0 && s.Top == 16); // front lower
    }

    [Fact]
    public void Build_TwoSided_InvertedOtherSector_ClampsUpperWallBottomToOwnFloor()
    {
        // Back sector is "closed" (floor above ceiling) - a real vanilla
        // Doom trick - and its ceiling sits below front's own floor.
        // Front's upper wall bottom must clamp to front's own floor
        // rather than extending down to that ceiling. Back's ceiling
        // staying below front's ceiling keeps this isolated from the
        // lower-wall condition (back's floor stays below front's floor
        // too), so exactly one segment should result.
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(-50, -100);
        var linedef = map.CreateLinedef(a, b, front, back);

        var segments = LinedefWallBuilder.Build(linedef);

        var upper = Assert.Single(segments);
        Assert.Equal(0, upper.Bottom);
        Assert.Equal(128, upper.Top);
    }

    [Fact]
    public void Build_TwoSided_InvertedOtherSector_ClampsLowerWallTopToOwnCeiling()
    {
        // Mirror of the above: back is closed with its floor above
        // front's ceiling, isolated from the upper-wall condition by
        // keeping back's ceiling at/above front's ceiling too.
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 64);
        var back = map.CreateSector(150, 100);
        var linedef = map.CreateLinedef(a, b, front, back);

        var segments = LinedefWallBuilder.Build(linedef);

        var lower = Assert.Single(segments);
        Assert.Equal(0, lower.Bottom);
        Assert.Equal(64, lower.Top);
    }

    [Fact]
    public void Build_TwoSided_MiddleTextureSet_NoHeightLookupProvided_SkipsMaskedMiddleEntirely()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128); // equal floors/ceilings - no upper/lower wall either
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";

        Assert.Empty(LinedefWallBuilder.Build(linedef));
    }

    [Fact]
    public void Build_TwoSided_MaskedMiddle_ShorterThanOpening_AnchorsToOpeningTop()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";

        var segments = LinedefWallBuilder.Build(linedef, _ => 40);

        var middle = Assert.Single(segments);
        Assert.Equal(88, middle.Bottom); // opening top (128) - texture height (40)
        Assert.Equal(128, middle.Top);
        Assert.Equal("MIDBARS1", middle.Texture);
    }

    [Fact]
    public void Build_TwoSided_MaskedMiddle_TallerThanOpening_ClipsToWholeOpening()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 64);
        var back = map.CreateSector(0, 64);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";

        var segments = LinedefWallBuilder.Build(linedef, _ => 500);

        var middle = Assert.Single(segments);
        Assert.Equal(0, middle.Bottom);
        Assert.Equal(64, middle.Top);
    }

    [Fact]
    public void Build_TwoSided_MaskedMiddle_OpeningBoundsUseBothSectors_LikeUpperAndLowerWalls()
    {
        // Same "step" shape as the upper/lower opening tests: the opening
        // is [max(floors), min(ceilings)], not either sector's own full
        // height range.
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(16, 200);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";

        var segments = LinedefWallBuilder.Build(linedef, _ => 1000);

        Assert.Contains(segments, s => s.Texture == "MIDBARS1" && s.Bottom == 16 && s.Top == 128);
    }

    [Fact]
    public void Build_TwoSided_NoMiddleTextureOnEitherSide_ProducesNoMaskedMiddleSegment()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        // Neither side's MiddleTexture is set - both default to "-".

        Assert.Empty(LinedefWallBuilder.Build(linedef, _ => 40));
    }

    [Fact]
    public void Build_TwoSided_BothSidesHaveMiddleTextures_ProducesOneSegmentPerSide()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "FRONTFENCE";
        linedef.Back!.MiddleTexture = "BACKFENCE";

        var segments = LinedefWallBuilder.Build(linedef, _ => 40);

        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.Texture == "FRONTFENCE");
        Assert.Contains(segments, s => s.Texture == "BACKFENCE");
    }

    [Fact]
    public void Build_TwoSided_MaskedMiddle_DefaultPegging_IsMaskedAndAnchoredToOpeningTop()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";

        var segments = LinedefWallBuilder.Build(linedef, _ => 40);

        var middle = Assert.Single(segments);
        Assert.True(middle.IsMasked);
        Assert.Equal(88, middle.Bottom);
        Assert.Equal(128, middle.Top);
        Assert.Equal(0, middle.VerticalTextureOffset); // unclipped - visible top edge is the texture's own natural top
    }

    [Fact]
    public void Build_TwoSided_UpperLowerAndSingleSegments_AreNeverMasked()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, null);
        linedef.Front!.MiddleTexture = "STARTAN2";

        var segment = Assert.Single(LinedefWallBuilder.Build(linedef));

        Assert.False(segment.IsMasked);
        Assert.Equal(0, segment.VerticalTextureOffset);
    }

    [Fact]
    public void Build_TwoSided_MaskedMiddle_ClassicLowerUnpeggedFlag_AnchorsToOpeningBottom()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";
        linedef.Fields.SetInteger("flags", 16); // ML_DONTPEGBOTTOM, classic-format storage

        var segments = LinedefWallBuilder.Build(linedef, _ => 40);

        var middle = Assert.Single(segments);
        Assert.Equal(0, middle.Bottom); // opening bottom (0)
        Assert.Equal(40, middle.Top); // opening bottom (0) + texture height (40)
        Assert.Equal(0, middle.VerticalTextureOffset);
    }

    [Fact]
    public void Build_TwoSided_MaskedMiddle_UdmfDontPegBottomField_AnchorsToOpeningBottom()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";
        linedef.Fields.SetBool("dontpegbottom", true);

        var segments = LinedefWallBuilder.Build(linedef, _ => 40);

        var middle = Assert.Single(segments);
        Assert.Equal(0, middle.Bottom);
        Assert.Equal(40, middle.Top);
    }

    [Fact]
    public void Build_TwoSided_MaskedMiddle_OffsetYShiftsTheAnchorPosition_NotJustTheUv()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";
        linedef.Front!.OffsetY = -10; // shift the texture's own top down by 10 from the opening's top

        var segments = LinedefWallBuilder.Build(linedef, _ => 40);

        var middle = Assert.Single(segments);
        Assert.Equal(78, middle.Bottom); // (128 - 10) - 40
        Assert.Equal(118, middle.Top); // 128 - 10
        Assert.Equal(0, middle.VerticalTextureOffset); // still unclipped - the whole shifted texture still fits inside the opening
    }

    [Fact]
    public void Build_TwoSided_MaskedMiddle_ClippedAtTop_ReportsTheVisibleOffsetIntoTheTexture()
    {
        var (map, a, b) = TwoVertices();
        var front = map.CreateSector(0, 128);
        var back = map.CreateSector(0, 128);
        var linedef = map.CreateLinedef(a, b, front, back);
        linedef.Front!.MiddleTexture = "MIDBARS1";
        linedef.Front!.OffsetY = 20; // push the texture's own top 20 units *above* the opening's own top

        var segments = LinedefWallBuilder.Build(linedef, _ => 40);

        var middle = Assert.Single(segments);
        Assert.Equal(128, middle.Top); // clipped to the opening's own top
        Assert.Equal(20, middle.VerticalTextureOffset); // the visible top edge is 20 units below the texture's own natural top
    }
}
