namespace DoomArchitect.Core.IO;

public enum UdmfValueKind
{
    Int,
    Double,
    Bool,
    String,
}

/// <summary>
/// One assignment's value, exactly as UDB's own parser resolves it: an
/// integer, a double, a boolean, or a string - there is no separate
/// "float" case (UDB's writer distinguishes float/double only for text
/// formatting; the reader never produces a float).
/// </summary>
public readonly struct UdmfValue
{
    private readonly long _intValue;
    private readonly double _doubleValue;
    private readonly bool _boolValue;
    private readonly string? _stringValue;

    private UdmfValue(UdmfValueKind kind, long intValue, double doubleValue, bool boolValue, string? stringValue)
    {
        Kind = kind;
        _intValue = intValue;
        _doubleValue = doubleValue;
        _boolValue = boolValue;
        _stringValue = stringValue;
    }

    public UdmfValueKind Kind { get; }

    public static UdmfValue OfInt(long value) => new(UdmfValueKind.Int, value, 0, false, null);

    public static UdmfValue OfDouble(double value) => new(UdmfValueKind.Double, 0, value, false, null);

    public static UdmfValue OfBool(bool value) => new(UdmfValueKind.Bool, 0, 0, value, null);

    public static UdmfValue OfString(string value) => new(UdmfValueKind.String, 0, 0, false, value);

    /// <summary>
    /// UDMF's own coercion rule: a field declared as a double accepts an
    /// int-typed value transparently (e.g. a vertex coordinate written as
    /// the bare integer <c>0</c> rather than <c>0.0</c>). This is
    /// one-directional - <see cref="AsLong"/>/<see cref="AsInt"/> do NOT
    /// accept a double-kinded value in return, matching UDB's own
    /// <c>UniversalEntry.ValidateType</c> exact-type-match check.
    /// </summary>
    public double AsDouble() => Kind switch
    {
        UdmfValueKind.Int => _intValue,
        UdmfValueKind.Double => _doubleValue,
        _ => throw new UdmfTypeException(Kind, UdmfValueKind.Double),
    };

    public long AsLong() => Kind == UdmfValueKind.Int
        ? _intValue
        : throw new UdmfTypeException(Kind, UdmfValueKind.Int);

    public int AsInt() => (int)AsLong();

    public bool AsBool() => Kind == UdmfValueKind.Bool
        ? _boolValue
        : throw new UdmfTypeException(Kind, UdmfValueKind.Bool);

    public string AsString() => Kind == UdmfValueKind.String
        ? _stringValue!
        : throw new UdmfTypeException(Kind, UdmfValueKind.String);

    /// <summary>
    /// The boxed CLR value this holds - a <see cref="long"/>,
    /// <see cref="double"/>, <see cref="bool"/>, or <see cref="string"/> -
    /// for moving a value into a map element's <c>CustomFields</c> bag
    /// without needing <c>Core.Map</c> to know about <see cref="UdmfValue"/>.
    /// </summary>
    public object ToObject() => Kind switch
    {
        UdmfValueKind.Int => _intValue,
        UdmfValueKind.Double => _doubleValue,
        UdmfValueKind.Bool => _boolValue,
        UdmfValueKind.String => _stringValue!,
        _ => throw new InvalidOperationException($"Unhandled {nameof(UdmfValueKind)}: {Kind}"),
    };
}
