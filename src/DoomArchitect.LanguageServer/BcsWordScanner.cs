namespace DoomArchitect.LanguageServer;

/// <summary>
/// Unlike Godot's own <c>CodeEdit</c> (whose <c>symbol_lookup</c>/tooltip
/// callbacks hand the in-app editor the hovered word directly via its own
/// internal <c>select_word</c>), every LSP request only ever carries a
/// cursor *position* - the server has to work out which word sits under
/// it itself. Shared by <see cref="BcsDefinitionHandler"/> and
/// <see cref="BcsHoverHandler"/>, both of which need exactly this before
/// they can call <c>BcsCompilationUnit.FindDeclaration</c>.
/// </summary>
internal static class BcsWordScanner
{
    /// <summary>
    /// The identifier/keyword touching <paramref name="character"/> on
    /// <paramref name="lineText"/>, or <see cref="string.Empty"/> if
    /// <paramref name="character"/> isn't inside a word at all - scans
    /// both directions from that column over the same word-character
    /// class <c>ScriptDocument.RequestBcsCodeCompletionIfWordLongEnough</c>
    /// already uses on the in-app side.
    /// </summary>
    public static string WordAt(string lineText, int character)
    {
        if (character < 0 || character > lineText.Length) return string.Empty;

        bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        var start = character;
        while (start > 0 && IsWordChar(lineText[start - 1])) start--;

        var end = character;
        while (end < lineText.Length && IsWordChar(lineText[end])) end++;

        return start == end ? string.Empty : lineText[start..end];
    }
}
