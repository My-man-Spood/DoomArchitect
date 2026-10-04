using DoomArchitect.Core.ZDoom.Bcs;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range; // disambiguate from System.Range

namespace DoomArchitect.LanguageServer;

/// <summary>
/// <c>textDocument/definition</c> - unlike Godot's own <c>CodeEdit</c>
/// (whose <c>symbol_lookup</c> signal hands the in-app editor the hovered
/// word directly), LSP's request only ever carries a cursor *position* -
/// the server has to work out which word sits under it itself
/// (<see cref="WordAt"/>), then resolve that word the same way the
/// in-app side does, via <see cref="BcsCompilationUnit.FindDeclaration"/>.
/// Re-parses <see cref="BcsDocumentStore"/>'s tracked text on every
/// request, same "small file, cheap to redo from scratch" posture every
/// other handler here already uses.
/// </summary>
internal sealed class BcsDefinitionHandler : DefinitionHandlerBase
{
    private readonly BcsDocumentStore _documentStore;

    private readonly TextDocumentSelector _selector = new(
        new TextDocumentFilter { Pattern = "**/*.bcs" }
    );

    public BcsDefinitionHandler(BcsDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken token)
    {
        var text = _documentStore.Get(request.TextDocument.Uri);
        if (text == null) return Task.FromResult<LocationOrLocationLinks?>(null);

        // request.Position is 0-based (LSP); everything BcsParser produces is
        // 1-based (the real compiler's own convention) - same conversion
        // every other handler here already does.
        var lines = text.Split('\n');
        var requestedLine = request.Position.Line;
        if (requestedLine < 0 || requestedLine >= lines.Length) return Task.FromResult<LocationOrLocationLinks?>(null);

        var word = WordAt(lines[requestedLine], request.Position.Character);
        if (word.Length == 0) return Task.FromResult<LocationOrLocationLinks?>(null);

        var (unit, _) = BcsParser.Parse(text);
        var declaration = unit.FindDeclaration(word, requestedLine + 1);
        if (declaration is not { } found) return Task.FromResult<LocationOrLocationLinks?>(null);

        var position = new Position(found.Line - 1, found.Column - 1);
        var location = new Location { Uri = request.TextDocument.Uri, Range = new Range(position, position) };
        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(new LocationOrLocationLink[] { location }));
    }

    /// <summary>
    /// The identifier/keyword touching <paramref name="character"/> on
    /// <paramref name="lineText"/>, or <see cref="string.Empty"/> if
    /// <paramref name="character"/> isn't inside a word at all - scans
    /// both directions from that column over the same word-character
    /// class <c>ScriptDocument.RequestBcsCodeCompletionIfWordLongEnough</c>
    /// already uses on the in-app side. Only needed here: Godot's own
    /// <c>CodeEdit</c> already does the equivalent of this internally
    /// (<c>select_word</c>) before ever emitting <c>symbol_lookup</c>.
    /// </summary>
    private static string WordAt(string lineText, int character)
    {
        if (character < 0 || character > lineText.Length) return string.Empty;

        bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        var start = character;
        while (start > 0 && IsWordChar(lineText[start - 1])) start--;

        var end = character;
        while (end < lineText.Length && IsWordChar(lineText[end])) end++;

        return start == end ? string.Empty : lineText[start..end];
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = _selector };
}
