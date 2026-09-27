namespace DoomArchitect.Core.IO;

/// <summary>
/// A known UDMF field's value has the wrong type (e.g. a string where a
/// number was expected). This throws rather than warns - it's the one
/// hard-failure path in the reader besides a grammar error, since a type
/// mismatch here means the file's field simply cannot be interpreted as
/// the thing it's named for.
/// </summary>
public sealed class UdmfTypeException : Exception
{
    public UdmfTypeException(UdmfValueKind actual, UdmfValueKind expected)
        : base($"Expected a {expected} value but found {actual}.")
    {
        Actual = actual;
        Expected = expected;
    }

    public UdmfValueKind Actual { get; }

    public UdmfValueKind Expected { get; }
}
