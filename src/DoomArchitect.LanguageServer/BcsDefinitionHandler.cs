using DoomArchitect.Core.ZDoom.Bcs;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range; // disambiguate from System.Range

namespace DoomArchitect.LanguageServer;

/// <summary>
/// <c>textDocument/definition</c> - finds the word under the request's
/// cursor position via <see cref="BcsWordScanner"/>, then resolves it the
/// same way the in-app side does, via <c>BcsProgram.FindDeclaration</c>
/// - which also reaches across whatever this document <c>#include</c>s/
/// <c>#import</c>s. When the match's own <see cref="BcsSymbol.SourcePath"/>
/// is non-empty, the returned <see cref="Location"/> points at *that*
/// file instead of the requesting one - a real editor already knows how
/// to open a <c>Location</c> in a different file, so that's the entire
/// cross-file story here. Re-parses <see cref="BcsDocumentStore"/>'s
/// tracked text on every request, same "small file, cheap to redo from
/// scratch" posture every other handler here already uses.
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

        var word = BcsWordScanner.WordAt(lines[requestedLine], request.Position.Character);
        if (word.Length == 0) return Task.FromResult<LocationOrLocationLinks?>(null);

        var program = _documentStore.GetProgram(request.TextDocument.Uri);
        var declaration = program?.FindDeclaration(word, requestedLine + 1);
        if (declaration is not { } found) return Task.FromResult<LocationOrLocationLinks?>(null);

        var targetUri = string.IsNullOrEmpty(found.SourcePath) ? request.TextDocument.Uri : DocumentUri.FromFileSystemPath(found.SourcePath);
        var position = new Position(found.Line - 1, found.Column - 1);
        var location = new Location { Uri = targetUri, Range = new Range(position, position) };
        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(new LocationOrLocationLink[] { location }));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = _selector };
}
