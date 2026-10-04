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
}
