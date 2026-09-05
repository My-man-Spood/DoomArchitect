namespace DoomArchitect.Core.IO;

/// <summary>
/// A grammar/lexer error while parsing UDMF text. UDB's own parser halts
/// on the first error with no recovery (`InputStructure` simply stops
/// once its internal error flag is set) - this port does the same, one
/// exception per parse attempt rather than a multi-error report.
/// </summary>
public sealed class UdmfParseException : Exception
{
    public UdmfParseException(string message, int line)
        : base($"Line {line}: {message}")
    {
        Line = line;
    }

    /// <summary>1-based line number where the error was detected.</summary>
    public int Line { get; }
}
