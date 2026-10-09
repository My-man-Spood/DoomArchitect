namespace DoomArchitect.Core.Compilers;

/// <summary>
/// Parses <c>zt-bcc</c>'s own stderr output into structured errors -
/// confirmed directly from its source (<c>src/task.c</c>'s
/// <c>print_diag</c>): a line with a known source position prints as
/// <c>file:line:col: message</c>; everything else (an internal error
/// with no position) is just a bare message, no colons at all. Mirrors
/// Ultimate Doom Builder's own <c>ZtBccCompiler.OnCheckError</c> exactly -
/// same 4-way colon split (file, line, column - ignored, message), same
/// "if nothing at all parsed, treat the whole stream as one error"
/// fallback for output that isn't in the expected shape.
/// </summary>
public static class ZtBccErrorParser
{
    public static IReadOnlyList<ScriptCompileError> Parse(string stderr)
    {
        var errors = new List<ScriptCompileError>();
        var lines = stderr.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            var parts = line.Split(':', 4);
            if (parts.Length != 4 || !int.TryParse(parts[1], out var lineNumber)) continue;

            errors.Add(new ScriptCompileError(parts[0], lineNumber, parts[3].Trim()));
        }

        if (errors.Count == 0)
        {
            var whole = stderr.Trim();
            if (whole.Length > 0) errors.Add(new ScriptCompileError(null, 0, whole));
        }

        return errors;
    }
}
