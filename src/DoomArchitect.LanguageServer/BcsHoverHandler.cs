using DoomArchitect.Core.ZDoom.Bcs;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace DoomArchitect.LanguageServer;

/// <summary>
/// A diagnostic's own message always wins when there is one on the
/// hovered line (unchanged from before); otherwise falls back to
/// resolving the hovered word to a real declaration via
/// <c>BcsProgram.FindDeclaration</c> (finding the word itself via
/// <see cref="BcsWordScanner"/>, since LSP only ever hands over a
/// position, never the word under it) - which also reaches across
/// whatever this document <c>#include</c>s/<c>#import</c>s, not just
/// itself - describing what it is (<see cref="BcsSymbol.Describe"/>),
/// and prepending its leading doc comment (<see cref="BcsSymbol.DocComment"/>)
/// when it has one. Re-parses
/// <see cref="BcsDocumentStore"/>'s tracked text on every request rather
/// than caching the result - the same "small file, cheap to redo from
/// scratch" posture <see cref="BcsTextDocumentHandler"/> already uses,
/// which avoids a second piece of state that would need to stay in sync
/// with the store.
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
        if (messages.Count > 0)
        {
            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.PlainText, Value = string.Join("\n", messages) }),
            });
        }

        var lines = text.Split('\n');
        if (request.Position.Line < 0 || request.Position.Line >= lines.Length) return Task.FromResult<Hover?>(null);

        var word = BcsWordScanner.WordAt(lines[request.Position.Line], request.Position.Character);
        if (word.Length == 0) return Task.FromResult<Hover?>(null);

        var program = _documentStore.GetProgram(request.TextDocument.Uri);
        var declaration = program?.FindDeclaration(word, request.Position.Line + 1);
        if (declaration is not { } found)
        {
            // Not a real declared symbol - a true compiler intrinsic like
            // Print/Delay/SpawnSpot (see BcsBuiltinFunctions's own remarks)
            // has no declaration to find at all, but is still worth a
            // signature on hover.
            var builtin = BcsBuiltinFunctions.TryDescribe(word);
            if (builtin == null) return Task.FromResult<Hover?>(null);

            var builtinSignature = $"```c\n{builtin}\n```";
            var builtinDoc = BcsFunctionDocs.Format(word);
            var builtinBody = builtinDoc == null ? builtinSignature : $"{builtinDoc}\n\n{builtinSignature}";

            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = builtinBody }),
            });
        }

        // Markdown + a fenced code block (unlike the plain-text diagnostic
        // path above - that's English prose, not code) so a real editor
        // applies its own syntax highlighting to a signature like
        // "function int Add(int a, int b)" instead of showing it as flat
        // text. No "bcs" grammar is commonly recognized, so "c" is used as
        // the closest reasonable approximation. A leading doc comment
        // (see BcsParser.ExtractDocComment), if there is one, goes above
        // the fence as plain Markdown prose - not inside it, since it
        // isn't code.
        var signature = $"```c\n{found.Describe()}\n```";
        var body = string.IsNullOrEmpty(found.DocComment) ? signature : $"{found.DocComment}\n\n{signature}";

        return Task.FromResult<Hover?>(new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent { Kind = MarkupKind.Markdown, Value = body }),
        });
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = _selector };
}
