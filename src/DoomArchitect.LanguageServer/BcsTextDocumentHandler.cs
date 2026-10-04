using DoomArchitect.Core.ZDoom.Bcs;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace DoomArchitect.LanguageServer;

/// <summary>
/// Full-document sync (no incremental ranges - a real BCS script is small
/// enough that re-tokenizing/re-parsing the whole buffer on every
/// keystroke is cheap, and it keeps this first pass simple) for
/// <c>.bcs</c> files: re-runs <see cref="BcsParser"/> on open/change and
/// publishes its collected <see cref="BcsDiagnostic"/>s as real LSP
/// <see cref="PublishDiagnosticsParams"/>. Also the sole writer of
/// <see cref="BcsDocumentStore"/> - <see cref="BcsHoverHandler"/>/
/// <see cref="BcsCompletionHandler"/> need the open document's current
/// text too, but requests like <c>textDocument/hover</c> only ever carry
/// a URI and a position, never the text itself. Go-to-definition,
/// multi-file <c>#include</c> resolution, and any semantic analysis
/// beyond raw syntax diagnostics are still explicitly out of scope for
/// this pass - see TODO/TODO.md.
/// </summary>
internal sealed class BcsTextDocumentHandler : TextDocumentSyncHandlerBase
{
    private readonly ILanguageServerFacade _server;
    private readonly BcsDocumentStore _documentStore;

    private readonly TextDocumentSelector _selector = new(
        new TextDocumentFilter { Pattern = "**/*.bcs" }
    );

    public BcsTextDocumentHandler(ILanguageServerFacade server, BcsDocumentStore documentStore)
    {
        _server = server;
        _documentStore = documentStore;
    }

    public TextDocumentSyncKind Change { get; } = TextDocumentSyncKind.Full;

    public override Task<Unit> Handle(DidOpenTextDocumentParams notification, CancellationToken token)
    {
        _documentStore.Set(notification.TextDocument.Uri, notification.TextDocument.Text);
        PublishDiagnosticsFor(notification.TextDocument.Uri, notification.TextDocument.Text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams notification, CancellationToken token)
    {
        var text = notification.ContentChanges.LastOrDefault()?.Text;
        if (text != null)
        {
            _documentStore.Set(notification.TextDocument.Uri, text);
            PublishDiagnosticsFor(notification.TextDocument.Uri, text);
        }

        return Unit.Task;
    }

    /// <summary>Closing off an otherwise-real, if slow, leak - without this, <see cref="BcsDocumentStore"/> would grow forever for an editor session that opens/closes many files.</summary>
    public override Task<Unit> Handle(DidCloseTextDocumentParams notification, CancellationToken token)
    {
        _documentStore.Remove(notification.TextDocument.Uri);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams notification, CancellationToken token) => Unit.Task;

    private void PublishDiagnosticsFor(DocumentUri uri, string text)
    {
        var (_, diagnostics) = BcsParser.Parse(text);

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(diagnostics.Select(ToLspDiagnostic)),
        });
    }

    /// <summary>
    /// <see cref="BcsDiagnostic"/>'s line/column are 1-based (matching the
    /// real compiler's own positions); LSP's <see cref="Position"/> is
    /// 0-based for both - converted only here, at the boundary, never
    /// inside <see cref="BcsTokenizer"/>/<see cref="BcsParser"/> itself.
    /// <see cref="BcsDiagnostic"/> doesn't carry an end position/length
    /// yet, so this reports a single-character range - a reasonable
    /// approximation for this pass, not a precise span.
    /// </summary>
    private static Diagnostic ToLspDiagnostic(BcsDiagnostic diagnostic)
    {
        var start = new Position(Math.Max(diagnostic.Line - 1, 0), Math.Max(diagnostic.Column - 1, 0));
        return new Diagnostic
        {
            Range = new Range(start, new Position(start.Line, start.Character + 1)),
            Severity = diagnostic.Severity == BcsDiagnosticSeverity.Warning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            Message = diagnostic.Message,
            Source = "bcs",
        };
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = _selector, Change = Change };

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, "bcs");
}
