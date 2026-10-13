using System.Numerics;
using DoomArchitect.Core.Geometry;

namespace DoomArchitect.Core.Tests.Geometry;

public class FlatNudgeCorrectionTests
{
    // Every case below is independently sanity-checked two ways: (1)
    // cameraRight is cameraForward rotated -90 degrees (e.g. facing
    // north, right is east), and (2) a positive offset delta on an axis
    // moves the rendered pattern in that axis's *negative* direction
    // (confirmed by hand against FlatTextureTransform's own formula) -
    // so "press Right while facing north" should shift the pattern
    // toward +X (east), which this method achieves by *decreasing*
    // offsetx - a negative delta.

    [Fact]
    public void Resolve_FacingNorth_RightShiftsPatternEast_ViaNegativeXDelta()
    {
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(0, 1), sectorRotationDegrees: 0, horizontal: 1, vertical: 0);

        Assert.Equal(-1, dx, 4);
        Assert.Equal(0, dy, 4);
    }

    [Fact]
    public void Resolve_FacingEast_RightShiftsPatternSouth_ViaPositiveYDelta()
    {
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(1, 0), sectorRotationDegrees: 0, horizontal: 1, vertical: 0);

        Assert.Equal(0, dx, 4);
        Assert.Equal(1, dy, 4);
    }

    [Fact]
    public void Resolve_FacingSouth_RightShiftsPatternWest_ViaPositiveXDelta()
    {
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(0, -1), sectorRotationDegrees: 0, horizontal: 1, vertical: 0);

        Assert.Equal(1, dx, 4);
        Assert.Equal(0, dy, 4);
    }

    [Fact]
    public void Resolve_FacingWest_RightShiftsPatternNorth_ViaNegativeYDelta()
    {
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(-1, 0), sectorRotationDegrees: 0, horizontal: 1, vertical: 0);

        Assert.Equal(0, dx, 4);
        Assert.Equal(-1, dy, 4);
    }

    [Fact]
    public void Resolve_FacingNorth_UpShiftsPatternFurtherNorth_ViaNegativeYDelta()
    {
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(0, 1), sectorRotationDegrees: 0, horizontal: 0, vertical: 1);

        Assert.Equal(0, dx, 4);
        Assert.Equal(-1, dy, 4);
    }

    [Fact]
    public void Resolve_NinetyDegreeSectorRotation_RotatesTheDesiredShiftByNinetyBeforeNegating()
    {
        // Facing north, Right alone (no sector rotation) resolves to
        // desiredShift = (1,0) pre-negation (see the facing-north case
        // above). A 90-degree CCW sector rotation should turn that into
        // (0,1) before the same final negation applies - this checks
        // the rotation step in isolation, rather than trying to equate
        // it to some other camera-angle scenario (rotating the sector's
        // own sampling by theta is the *inverse* of rotating the camera
        // by theta, an easy mistake to make when testing this - the
        // first version of this test asserted the wrong equivalence).
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(0, 1), sectorRotationDegrees: 90, horizontal: 1, vertical: 0);

        Assert.Equal(0, dx, 4);
        Assert.Equal(-1, dy, 4);
    }

    [Fact]
    public void Resolve_CombinedHorizontalAndVertical_IsTheLinearSumOfBothAxesIndependently()
    {
        var combined = FlatNudgeCorrection.Resolve(new Vector2(0, 1), sectorRotationDegrees: 0, horizontal: 5, vertical: 3);
        var right = FlatNudgeCorrection.Resolve(new Vector2(0, 1), sectorRotationDegrees: 0, horizontal: 5, vertical: 0);
        var up = FlatNudgeCorrection.Resolve(new Vector2(0, 1), sectorRotationDegrees: 0, horizontal: 0, vertical: 3);

        Assert.Equal(right.OffsetXDelta + up.OffsetXDelta, combined.OffsetXDelta, 4);
        Assert.Equal(right.OffsetYDelta + up.OffsetYDelta, combined.OffsetYDelta, 4);
    }

    [Fact]
    public void Resolve_ZeroDeltaStaysZero()
    {
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(0, 1), sectorRotationDegrees: 0, horizontal: 0, vertical: 0);

        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
    }

    // A real camera is essentially never pointed exactly along a map
    // axis - a first version of this method used cameraRight/
    // cameraForward directly (no snapping), which meant pressing a
    // single arrow key moved *both* offset axes at once, by a fraction
    // proportional to how far off-cardinal the camera happened to be -
    // a permanent diagonal drift on every keypress, reported live. Both
    // cases below face mostly-but-not-exactly north/east and must match
    // their exactly-cardinal counterparts above exactly, with zero on
    // the other axis - proof the result is snapped, not blended.

    [Fact]
    public void Resolve_CameraSlightlyOffNorth_RightStillResolvesToPureEastShift_NotADiagonalBlend()
    {
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(0.34f, 0.94f), sectorRotationDegrees: 0, horizontal: 1, vertical: 0);

        Assert.Equal(-1, dx, 4);
        Assert.Equal(0, dy, 4);
    }

    [Fact]
    public void Resolve_CameraSlightlyOffEast_UpStillResolvesToPureXShift_NotADiagonalBlend()
    {
        // Facing (mostly) east, Up should push the pattern further east -
        // the same relationship "facing north, Up shifts pattern further
        // north" above has (a negative delta on the forward axis), just
        // on X instead of Y since the camera is turned 90 degrees.
        var (dx, dy) = FlatNudgeCorrection.Resolve(new Vector2(0.94f, 0.34f), sectorRotationDegrees: 0, horizontal: 0, vertical: 1);

        Assert.Equal(-1, dx, 4);
        Assert.Equal(0, dy, 4);
    }
}
