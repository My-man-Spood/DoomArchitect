using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

public class BcsBuiltinFunctionsTests
{
    /// <summary>
    /// Hand-verified against `builtin.c`'s own `setup_return_type`/
    /// `setup_param_list` directly - no base return type char, a ';'
    /// separator, then one required int param. The "tics" name isn't
    /// from that real source at all (the format string carries no
    /// names) - it's this project's own researched addition via
    /// <see cref="BcsFunctionDocs.ApplyParameterNames"/>; only the type
    /// and required/optional-ness are actually verified against `g_funcs[]`.
    /// </summary>
    [Fact]
    public void TryDescribe_Delay_OneRequiredIntParam()
    {
        Assert.Equal("void Delay(int tics)", BcsBuiltinFunctions.TryDescribe("delay"));
    }

    /// <summary>Hand-verified: int return, then "si;ii" -> str/int required, a second ';' flips the rest to optional. Parameter names are this project's own researched addition, not from the format string itself - see <see cref="TryDescribe_Delay_OneRequiredIntParam"/>'s own remarks.</summary>
    [Fact]
    public void TryDescribe_SpawnSpot_RequiredThenOptionalParams()
    {
        Assert.Equal("int Spawnspot(str classname, int spottid, [int tid], [int angle])", BcsBuiltinFunctions.TryDescribe("spawnspot"));
    }

    [Fact]
    public void TryDescribe_ThingCount_OneRequiredOneOptional()
    {
        Assert.Equal("int Thingcount(int type, [int tid])", BcsBuiltinFunctions.TryDescribe("thingcount"));
    }

    [Fact]
    public void TryDescribe_LineSide_NonVoidReturnNoParams()
    {
        Assert.Equal("int Lineside()", BcsBuiltinFunctions.TryDescribe("lineside"));
    }

    [Fact]
    public void TryDescribe_ClearInventory_VoidReturnNoParams()
    {
        Assert.Equal("void Clearinventory()", BcsBuiltinFunctions.TryDescribe("clearinventory"));
    }

    [Theory]
    [InlineData("delay")]
    [InlineData("DELAY")]
    [InlineData("Delay")]
    public void TryDescribe_IsCaseInsensitive(string name)
    {
        Assert.Equal("void Delay(int tics)", BcsBuiltinFunctions.TryDescribe(name));
    }

    [Fact]
    public void TryDescribe_NotABuiltin_ReturnsNull()
    {
        Assert.Null(BcsBuiltinFunctions.TryDescribe("notarealfunction"));
    }

    /// <summary>The real table's own entry for these three is an empty format string - decoding that naively would wrongly claim "void, zero params"; these get a manually written signature instead (see BcsBuiltinFunctions's own remarks).</summary>
    [Theory]
    [InlineData("print", "void Print(...)")]
    [InlineData("printbold", "void PrintBold(...)")]
    [InlineData("log", "void Log(...)")]
    public void TryDescribe_FormatFunctions_UseTheManualOverrideNotANaiveEmptyDecode(string name, string expected)
    {
        Assert.Equal(expected, BcsBuiltinFunctions.TryDescribe(name));
    }

    /// <summary>A real underscore in the raw name is a genuine, already-present word boundary (not invented) - both sides get capitalized.</summary>
    [Fact]
    public void TryDescribe_NameWithARealUnderscore_CapitalizesBothSides()
    {
        Assert.Equal("void Thing_Projectile2(int tid, int type, int angle, int speed, int vspeed, int gravity, int newtid)", BcsBuiltinFunctions.TryDescribe("thing_projectile2"));
    }

    [Fact]
    public void AllNames_ContainsWellKnownBuiltins()
    {
        Assert.Contains("Print", BcsBuiltinFunctions.AllNames);
        Assert.Contains("Delay", BcsBuiltinFunctions.AllNames);
        Assert.Contains("Spawnspot", BcsBuiltinFunctions.AllNames);
    }

    /// <summary>Confirmed from the real source: a STATIC_ASSERT there ties g_funcs[]'s own length to the sum of its three backing-implementation tables (131 + 6 + 2) - this is the complete, authoritative list, not a partial one.</summary>
    [Fact]
    public void AllNames_HasTheRealCompleteCount()
    {
        Assert.Equal(139, BcsBuiltinFunctions.AllNames.Count);
    }
}
