using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Tests.Map;

public class UniFieldsTests
{
    [Fact]
    public void GetValue_MissingKey_ReturnsDefault()
    {
        var fields = new UniFields();

        Assert.Equal(42, fields.GetValue("missing", 42));
    }

    [Fact]
    public void GetValue_WrongType_ReturnsDefault()
    {
        var fields = new UniFields { ["key"] = new UniValue(UniversalType.String, "text") };

        Assert.Equal(42, fields.GetValue("key", 42));
    }

    [Fact]
    public void GetValue_CorrectTypeAndKey_ReturnsStoredValue()
    {
        var fields = new UniFields { ["key"] = new UniValue(UniversalType.Integer, 7L) };

        Assert.Equal(7L, fields.GetValue("key", 0L));
    }

    [Fact]
    public void SetInteger_DefaultValue_RemovesTheKey()
    {
        var fields = new UniFields { ["key"] = new UniValue(UniversalType.Integer, 5L) };

        fields.SetInteger("key", 0);

        Assert.False(fields.ContainsKey("key"));
    }

    [Fact]
    public void SetInteger_NonDefaultValue_StoresItWithIntegerType()
    {
        var fields = new UniFields();

        fields.SetInteger("key", 5);

        Assert.Equal(5L, fields["key"].Value);
        Assert.Equal(UniversalType.Integer, fields["key"].Type);
    }

    [Fact]
    public void GetInteger_AbsentKey_ReturnsSuppliedDefault()
    {
        var fields = new UniFields();

        Assert.Equal(9L, fields.GetInteger("key", 9));
    }

    [Fact]
    public void SetFloat_DefaultValue_RemovesTheKey()
    {
        var fields = new UniFields { ["key"] = new UniValue(UniversalType.Float, 1.5) };

        fields.SetFloat("key", 0.0);

        Assert.False(fields.ContainsKey("key"));
    }

    [Fact]
    public void SetFloat_NonDefaultValue_StoresItWithFloatType()
    {
        var fields = new UniFields();

        fields.SetFloat("key", 1.5);

        Assert.Equal(1.5, fields["key"].Value);
        Assert.Equal(UniversalType.Float, fields["key"].Type);
    }

    [Fact]
    public void GetFloat_AbsentKey_ReturnsSuppliedDefault()
    {
        var fields = new UniFields();

        Assert.Equal(2.5, fields.GetFloat("key", 2.5));
    }

    [Fact]
    public void SetBool_DefaultValue_RemovesTheKey()
    {
        var fields = new UniFields { ["key"] = new UniValue(UniversalType.Boolean, true) };

        fields.SetBool("key", false);

        Assert.False(fields.ContainsKey("key"));
    }

    [Fact]
    public void SetBool_NonDefaultValue_StoresItWithBooleanType()
    {
        var fields = new UniFields();

        fields.SetBool("key", true);

        Assert.Equal(true, fields["key"].Value);
        Assert.Equal(UniversalType.Boolean, fields["key"].Type);
    }

    [Fact]
    public void GetBool_AbsentKey_ReturnsSuppliedDefault()
    {
        var fields = new UniFields();

        Assert.True(fields.GetBool("key", true));
    }

    [Fact]
    public void SetString_DefaultValue_RemovesTheKey()
    {
        var fields = new UniFields { ["key"] = new UniValue(UniversalType.String, "custom") };

        fields.SetString("key", "-", "-");

        Assert.False(fields.ContainsKey("key"));
    }

    [Fact]
    public void SetString_NonDefaultValue_StoresItWithStringType()
    {
        var fields = new UniFields();

        fields.SetString("key", "custom", "-");

        Assert.Equal("custom", fields["key"].Value);
        Assert.Equal(UniversalType.String, fields["key"].Type);
    }

    [Fact]
    public void GetString_AbsentKey_ReturnsSuppliedDefault()
    {
        var fields = new UniFields();

        Assert.Equal("fallback", fields.GetString("key", "fallback"));
    }

    [Fact]
    public void RemoveField_RemovesAnExistingKey()
    {
        var fields = new UniFields { ["key"] = new UniValue(UniversalType.Integer, 1L) };

        fields.RemoveField("key");

        Assert.False(fields.ContainsKey("key"));
    }

    [Fact]
    public void RemoveField_AbsentKey_DoesNotThrow()
    {
        var fields = new UniFields();

        fields.RemoveField("missing");
    }

    [Fact]
    public void RemoveFields_RemovesEveryGivenKeyAndLeavesOthers()
    {
        var fields = new UniFields
        {
            ["a"] = new UniValue(UniversalType.Integer, 1L),
            ["b"] = new UniValue(UniversalType.Integer, 2L),
            ["c"] = new UniValue(UniversalType.Integer, 3L),
        };

        fields.RemoveFields(new[] { "a", "b" });

        Assert.False(fields.ContainsKey("a"));
        Assert.False(fields.ContainsKey("b"));
        Assert.True(fields.ContainsKey("c"));
    }
}
