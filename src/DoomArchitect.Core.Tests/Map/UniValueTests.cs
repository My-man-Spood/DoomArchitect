using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Map;

public class UniValueTests
{
    [Theory]
    [InlineData(UniversalType.Integer, 7L)]
    [InlineData(UniversalType.Float, 1.5)]
    [InlineData(UniversalType.Boolean, true)]
    [InlineData(UniversalType.String, "ambush")]
    public void Constructor_ValidClrType_RoundTripsTypeAndValue(UniversalType type, object value)
    {
        var uniValue = new UniValue(type, value);

        Assert.Equal(type, uniValue.Type);
        Assert.Equal(value, uniValue.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1f)]
    public void Constructor_UnsupportedClrType_ThrowsArgumentException(object value)
    {
        Assert.Throws<ArgumentException>(() => new UniValue(UniversalType.Integer, value));
    }

    [Fact]
    public void Constructor_NullValue_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new UniValue(UniversalType.Integer, null!));
    }

    [Fact]
    public void ValueSetter_UnsupportedClrType_ThrowsArgumentException()
    {
        var uniValue = new UniValue(UniversalType.Integer, 1L);

        Assert.Throws<ArgumentException>(() => uniValue.Value = 1);
    }

    [Fact]
    public void CopyConstructor_CopiesTypeAndValueWithoutRevalidating()
    {
        var original = new UniValue(UniversalType.String, "special");

        var copy = new UniValue(original);

        Assert.Equal(original.Type, copy.Type);
        Assert.Equal(original.Value, copy.Value);
    }
}
