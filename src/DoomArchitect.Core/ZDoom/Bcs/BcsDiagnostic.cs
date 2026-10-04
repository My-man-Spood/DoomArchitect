namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// Matches the real <c>bcc</c> compiler's own diagnostic levels - an empty
/// hex/fixed-point-fraction/radix-digits literal is only ever a warning
/// (the compiler substitutes <c>0</c> and keeps going), while an empty
/// binary/octal/decimal literal, an unterminated string/comment, and every
/// real syntax error are fatal. <see cref="BcsTokenizer"/>/<see cref="BcsParser"/>
/// both report into one shared list rather than two separate channels.
/// </summary>
public enum BcsDiagnosticSeverity
{
    Error,
    Warning,
}

/// <summary>One real compiler diagnostic, carrying its own 1-based source position (converted to LSP's 0-based <c>Position</c> only at the language-server boundary, not here).</summary>
public sealed class BcsDiagnostic
{
    public string Message { get; }
    public int Line { get; }
    public int Column { get; }
    public BcsDiagnosticSeverity Severity { get; }

    /// <summary>
    /// Which file this diagnostic belongs to - empty for the file
    /// originally handed to <c>BcsParser.Parse</c>/<c>ParseProgram</c>
    /// (same "empty means the main file" convention <see cref="BcsSymbol.SourcePath"/>
    /// already uses), a real resolved path for one raised while reading
    /// a file spliced in via <c>#include</c>/<c>#import</c>. Optional,
    /// defaulting to <c>""</c>, so every pre-existing 4-arg call site
    /// across this codebase compiles unchanged.
    /// </summary>
    public string SourcePath { get; }

    public BcsDiagnostic(string message, int line, int column, BcsDiagnosticSeverity severity = BcsDiagnosticSeverity.Error, string sourcePath = "")
    {
        Message = message;
        Line = line;
        Column = column;
        Severity = severity;
        SourcePath = sourcePath;
    }

    public override string ToString() => $"{Severity} ({Line}:{Column}): {Message}";
}
