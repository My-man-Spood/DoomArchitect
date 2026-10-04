using DoomArchitect.Core.ZDoom.Bcs;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace DoomArchitect.LanguageServer;

/// <summary>
/// The only thing worth hovering for today is a diagnostic's own message
/// text - there's no symbol table, so hovering an identifier/keyword to
/// explain what it is isn't built yet (see TODO/bcs-lsp-foundation.md).
/// Re-parses <see cref="BcsDocumentStore"/>'s tracked text on every
/// request rather than caching the result - the same "small file, cheap
/// to redo from scratch" posture <see cref="BcsTextDocumentHandler"/>
/// already uses, which avoids a second piece of state that would need
/// to stay in sync with the store.
/// </summary>
internal sealed class BcsHoverHandler : HoverHandlerBase
{
    private readonly BcsDocumentStore _documentStore;

    private readonly TextDocumentSelector _selector = new(
        new TextDocumentFilter { Pattern = "**/*.bcs" }
    );

    public BcsHoverHandler(BcsDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken token)
    {
        var text = _documentStore.Get(request.TextDocument.Uri);
        if (text == null) return Task.FromResult<Hover?>(null);

        var (_, diagnostics) = BcsParser.Parse(text);

        // request.Position is 0-based (LSP); BcsDiagnostic.Line is 1-based
        // (the real compiler's own convention) - the same conversion
        // BcsTextDocumentHandler.ToLspDiagnostic already does, just the
        // other direction. Column-independent - a diagnostic anywhere on
        // the hovered line matches, since BcsDiagnostic has no end
        // position/length to narrow against yet either.
        var messages = diagnostics.Where(d => d.Line - 1 == request.Position.Line).Select(d => d.Message).ToList();
        if (messages.Count == 0) return Task.FromResult<Hover?>(null);

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.PlainText, Value = string.Join("\n", messages) }),
        });
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = _selector };
}
