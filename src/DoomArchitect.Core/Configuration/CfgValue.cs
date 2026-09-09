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

    /// <summary>Accepts an int-kinded value transparently, matching the same coercion <see cref="IO.UdmfValue.AsDouble"/> applies.</summary>
    public double AsDouble() => Kind switch
    {
        CfgValueKind.Int => _intValue,
        CfgValueKind.Double => _doubleValue,
        _ => throw new CfgTypeException(Kind, CfgValueKind.Double),
    };

    public float AsFloat() => (float)AsDouble();

    public long AsLong() => Kind == CfgValueKind.Int
        ? _intValue
        : throw new CfgTypeException(Kind, CfgValueKind.Int);

    public int AsInt() => (int)AsLong();

    public bool AsBool() => Kind == CfgValueKind.Bool
        ? _boolValue
        : throw new CfgTypeException(Kind, CfgValueKind.Bool);

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
