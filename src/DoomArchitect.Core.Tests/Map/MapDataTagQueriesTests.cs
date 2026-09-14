using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Map;

public class MapDataTagQueriesTests
{
    [Fact]
    public void FindFree_SkipsUsedNumbersStartingFromOne()
    {
        var used = new HashSet<long> { 1, 2, 4 };

        Assert.Equal(3, TagAllocator.FindFree(used));
    }

    [Fact]
    public void FindFree_HonorsACustomStart()
    {
        var used = new HashSet<long> { 5, 6 };

        Assert.Equal(7, TagAllocator.FindFree(used, start: 5));
    }

    [Fact]
    public void FindFree_EmptySet_ReturnsStart()
    {
        Assert.Equal(1, TagAllocator.FindFree(Array.Empty<long>()));
    }

    [Fact]
    public void GetUsedSectorTags_CollectsPrimaryAndExtraTagsFromEverySector()
    {
        var map = new MapData();
        var a = map.CreateSector(0, 128);
        a.Fields.SetInteger("id", 5);
        var b = map.CreateSector(0, 128);
        b.Fields.SetInteger("id", 9);
        b.Fields.SetString("moreids", "10 11", "");

        var used = map.GetUsedSectorTags();

        Assert.Equal(new HashSet<long> { 5, 9, 10, 11 }, used);
    }

    [Fact]
    public void GetUsedSectorTags_ZeroTagIsNeverCountedAsUsed()
    {
        var map = new MapData();
        map.CreateSector(0, 128); // id defaults to 0 (absent)

        Assert.Empty(map.GetUsedSectorTags());
    }

    [Fact]
    public void GetUsedTags_AlsoIncludesLinedefPrimaryTags()
    {
        var map = new MapData();
        var sector = map.CreateSector(0, 128);
        sector.Fields.SetInteger("id", 5);
        var v1 = map.CreateVertex(new System.Numerics.Vector2(0, 0));
        var v2 = map.CreateVertex(new System.Numerics.Vector2(64, 0));
        var linedef = map.CreateLinedef(v1, v2, front: sector, back: null);
        linedef.Fields.SetInteger("id", 20);

        var used = map.GetUsedTags();

        Assert.Equal(new HashSet<long> { 5, 20 }, used);
        Assert.DoesNotContain(20L, map.GetUsedSectorTags());
    }
}
