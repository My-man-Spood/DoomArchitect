namespace DoomArchitect.Core.Configuration;

public enum CfgValueKind
{
    Int,
    Double,
    Bool,
    String,
}

/// <summary>
/// One assignment's value in a <c>.cfg</c> file - an integer, a double, a
/// boolean, or a string. Deliberately its own type rather than reusing
/// <see cref="IO.UdmfValue"/>: the two grammars are similar but not the
/// same format (see <see cref="CfgParser"/>'s remarks), and this project's
/// own architecture notes call for keeping format parsers independent
/// until sharing them stops being speculative.
/// </summary>
public readonly struct CfgValue
{
    private readonly long _intValue;
    private readonly double _doubleValue;
    private readonly bool _boolValue;
    private readonly string? _stringValue;

    private CfgValue(CfgValueKind kind, long intValue, double doubleValue, bool boolValue, string? stringValue)
    {
        Kind = kind;
        _intValue = intValue;
        _doubleValue = doubleValue;
        _boolValue = boolValue;
        _stringValue = stringValue;
    }

    public CfgValueKind Kind { get; }

    public static CfgValue OfInt(long value) => new(CfgValueKind.Int, value, 0, false, null);

    public static CfgValue OfDouble(double value) => new(CfgValueKind.Double, 0, value, false, null);

    public static CfgValue OfBool(bool value) => new(CfgValueKind.Bool, 0, 0, value, null);

    public static CfgValue OfString(string value) => new(CfgValueKind.String, 0, 0, false, value);

    /// <summary>
    /// Accepts an int-kinded value transparently, matching the same
    /// coercion <see cref="IO.UdmfValue.AsDouble"/> applies - and, unlike
    /// that UDMF counterpart, also a numeric-looking quoted string (real
    /// .cfg data has a handful of <c>width = "16";</c>-style entries
    /// alongside the far more common unquoted <c>width = 16;</c>, an
    /// authoring inconsistency across UDB's own decades-old data rather
    /// than a distinct format).
    /// </summary>
    public double AsDouble() => Kind switch
    {
        CfgValueKind.Int => _intValue,
        CfgValueKind.Double => _doubleValue,
        CfgValueKind.String when double.TryParse(_stringValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw new CfgTypeException(Kind, CfgValueKind.Double),
    };

    public float AsFloat() => (float)AsDouble();

    public long AsLong() => Kind == CfgValueKind.Int
        ? _intValue
        : throw new CfgTypeException(Kind, CfgValueKind.Int);

    public int AsInt() => (int)AsLong();

    /// <summary>
    /// Accepts an int-kinded value transparently too (same coercion
    /// <see cref="AsDouble"/> applies for numeric kinds) - real .cfg data
    /// writes plenty of boolean-semantic fields (e.g. <c>hangs = 0;</c>)
    /// as a plain 0/1 rather than the <c>true</c>/<c>false</c> keyword,
    /// and both forms need to read the same way.
    /// </summary>
    public bool AsBool() => Kind switch
    {
        CfgValueKind.Bool => _boolValue,
        CfgValueKind.Int => _intValue != 0,
        _ => throw new CfgTypeException(Kind, CfgValueKind.Bool),
    };

    public string AsString() => Kind == CfgValueKind.String
        ? _stringValue!
        : throw new CfgTypeException(Kind, CfgValueKind.String);
}

public sealed class CfgTypeException : Exception
{
    public CfgTypeException(CfgValueKind actual, CfgValueKind expected)
        : base($"Expected a {expected} value but found {actual}.")
    {
    }
}

public sealed class CfgParseException : Exception
{
    public CfgParseException(string message, int line) : base($"{message} (line {line})")
    {
        Line = line;
    }

    public int Line { get; }
}
