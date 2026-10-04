using DoomArchitect.Core.ZDoom.Bcs;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace DoomArchitect.LanguageServer;

/// <summary>
/// Tracks each open document's current full text by URI - the one piece
/// of state <c>textDocument/hover</c> and <c>textDocument/completion</c>
/// genuinely need that didn't exist before: those requests carry only a
/// URI and a position, not the document's text, and the server is
/// expected to already know it from prior <c>didOpen</c>/<c>didChange</c>
/// notifications. <see cref="BcsTextDocumentHandler"/> updates this on
/// open/change/close; <see cref="BcsHoverHandler"/>/<see cref="BcsCompletionHandler"/>
/// only ever read it. Deliberately dumb - no per-document parse caching,
/// no incremental anything - matching this project's own established
/// "re-parse the whole buffer, don't be clever" posture.
/// </summary>
internal sealed class BcsDocumentStore
{
    private readonly Dictionary<DocumentUri, string> _textByUri = new();

    public void Set(DocumentUri uri, string text) => _textByUri[uri] = text;
    public void Remove(DocumentUri uri) => _textByUri.Remove(uri);
    public string? Get(DocumentUri uri) => _textByUri.TryGetValue(uri, out var text) ? text : null;

    /// <summary>
    /// <see cref="BcsParser.ParseProgram"/> for this document - resolves
    /// its own <c>#include</c>/<c>#import</c>s against its real
    /// filesystem path (via <see cref="DocumentUri.GetFileSystemPath"/>;
    /// a non-<c>file</c>-scheme URI, e.g. an untitled buffer, has none,
    /// so relative includes simply can't resolve for it - the same
    /// "nowhere to resolve against" case <c>ParseProgram</c> already
    /// handles for a null path). Included files are always read fresh
    /// from real disk, never from this store - an included file that
    /// isn't itself an open document (the common case - most included
    /// headers are never directly opened) has no entry here at all.
    /// Returns <c>null</c> under the same condition <see cref="Get"/>
    /// does: this URI was never opened at all.
    /// </summary>
    public BcsProgram? GetProgram(DocumentUri uri)
    {
        var text = Get(uri);
        if (text == null) return null;

        var path = uri.Scheme == "file" ? uri.GetFileSystemPath() : null;
        return BcsParser.ParseProgram(text, path, p => File.Exists(p) ? File.ReadAllText(p) : null);
    }
}
