namespace DoomArchitect.Core.Compilers;

/// <summary>
/// One compiler-reported problem, resolved from <c>zt-bcc</c>'s own
/// stderr output (see <see cref="ZtBccErrorParser"/>). <see cref="Line"/>
/// is 1-based, matching <c>BcsDiagnostic.Line</c>'s own existing
/// convention - both get converted to a 0-based editor line the same way,
/// at the Godot call site.
/// </summary>
public sealed record ScriptCompileError(string? FilePath, int Line, string Message);
