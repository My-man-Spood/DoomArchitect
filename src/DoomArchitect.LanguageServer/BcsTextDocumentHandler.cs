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
/// <c>.bcs</c> files: re-runs <see cref="BcsDocumentStore.GetProgram"/>
/// on open/change and publishes its collected <see cref="BcsDiagnostic"/>s
/// as real LSP <see cref="PublishDiagnosticsParams"/> - this now includes
/// an unresolvable <c>#include</c>/<c>#import</c> written directly in
/// this document (a warning, reported at the directive's own position;
/// see <c>BcsParser.ParseProgram</c>'s own remarks), not just this
/// file's own syntax errors. Also the sole writer of
/// <see cref="BcsDocumentStore"/> - <see cref="BcsHoverHandler"/>/
/// <see cref="BcsCompletionHandler"/> need the open document's current
/// text too, but requests like <c>textDocument/hover</c> only ever carry
/// a URI and a position, never the text itself.
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
        PublishDiagnosticsFor(notification.TextDocument.Uri);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams notification, CancellationToken token)
    {
        var text = notification.ContentChanges.LastOrDefault()?.Text;
        if (text != null)
        {
            _documentStore.Set(notification.TextDocument.Uri, text);
            PublishDiagnosticsFor(notification.TextDocument.Uri);
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

    /// <summary>
    /// Reads back via <see cref="BcsDocumentStore.GetProgram"/> rather
    /// than taking the text as its own parameter - by the time this
    /// runs, <c>Set</c> has already written it (both call sites above
    /// do that first), so <c>GetProgram</c> sees the current text either
    /// way, and this is the one path that also resolves includes.
    /// </summary>
    private void PublishDiagnosticsFor(DocumentUri uri)
    {
        var diagnostics = _documentStore.GetProgram(uri)?.Diagnostics ?? new List<BcsDiagnostic>();

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
