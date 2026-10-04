using DoomArchitect.Core.ZDoom.Bcs;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace DoomArchitect.LanguageServer;

/// <summary>
/// Keywords (the real 53-entry reserved-word list,
/// <see cref="BcsTokenizer.ReservedWordTexts"/>) plus every name visible
/// from the request's own cursor position - via
/// <see cref="BcsCompilationUnit.CollectSymbolsVisibleAt"/>, not the
/// older flat <c>CollectSymbols()</c>: a local declared inside one
/// script/function body is only offered while the cursor is inside that
/// same body - everything else (functions, script names, globals, enum
/// types/members, macros) is still offered everywhere. Needs
/// <see cref="BcsDocumentStore"/> since the declared-name half of the
/// list depends on the specific document (and now cursor position)
/// being completed in, not just a static table.
/// </summary>
internal sealed class BcsCompletionHandler : CompletionHandlerBase
{
    private static readonly IEnumerable<CompletionItem> KeywordItems = BcsTokenizer.ReservedWordTexts
        .Select(keyword => new CompletionItem
        {
            Label = keyword,
            // CompletionItemKind.Keyword (LSP) - an unrelated enum from Godot's own CodeEdit.CodeCompletionKind.Keyword used on the in-app side; they share a name, not a definition.
            Kind = CompletionItemKind.Keyword,
            InsertText = keyword,
        })
        .ToList();

    private readonly BcsDocumentStore _documentStore;

    private readonly TextDocumentSelector _selector = new(
        new TextDocumentFilter { Pattern = "**/*.bcs" }
    );

    public BcsCompletionHandler(BcsDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken token)
    {
        var text = _documentStore.Get(request.TextDocument.Uri);
        if (text == null) return Task.FromResult(new CompletionList(KeywordItems, isIncomplete: false));

        var (unit, _) = BcsParser.Parse(text);
        var line = request.Position.Line + 1; // LSP's 0-based line -> this parser's 1-based lines
        var symbolItems = unit.CollectSymbolsVisibleAt(line)
            // BcsSymbol now carries its own declaration position, so two distinct
            // declarations sharing a name (e.g. the same local reused across two
            // different scripts) no longer collapse for free - dedupe explicitly,
            // case-insensitively (BCS itself is case-insensitive).
            .DistinctBy(symbol => (symbol.Name.ToLowerInvariant(), symbol.Kind))
            .Select(symbol => new CompletionItem
            {
                Label = symbol.Name,
                Kind = ToCompletionItemKind(symbol.Kind),
                InsertText = symbol.Name,
            });

        return Task.FromResult(new CompletionList(KeywordItems.Concat(symbolItems), isIncomplete: false));
    }

    private static CompletionItemKind ToCompletionItemKind(BcsSymbolKind kind) => kind switch
    {
        BcsSymbolKind.Function => CompletionItemKind.Function,
        BcsSymbolKind.Variable => CompletionItemKind.Variable,
        BcsSymbolKind.EnumType => CompletionItemKind.Enum,
        BcsSymbolKind.EnumMember => CompletionItemKind.EnumMember,
        BcsSymbolKind.Macro => CompletionItemKind.Constant, // LSP has no dedicated "macro" kind - Constant is the closest honest fit
        _ => CompletionItemKind.Text,
    };

    /// <summary>The "resolve" request for a single already-shown item - never actually invoked, since <see cref="CreateRegistrationOptions"/> declares <c>ResolveProvider = false</c>. A plain pass-through covers the override regardless.</summary>
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken token) =>
        Task.FromResult(request);

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = _selector, ResolveProvider = false };
}
