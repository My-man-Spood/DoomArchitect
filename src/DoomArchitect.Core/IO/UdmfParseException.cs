namespace DoomArchitect.Core.IO;

/// <summary>
/// A grammar/lexer error while parsing UDMF text. Parsing halts on the
/// first error with no recovery, matching UDB's own parser - one exception
/// per parse attempt rather than a multi-error report.
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
