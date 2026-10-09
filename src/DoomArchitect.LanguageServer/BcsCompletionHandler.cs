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
/// <see cref="BcsDocumentStore.GetProgram"/>'s
/// <c>BcsProgram.CollectSymbolsVisibleAt</c>, which also reaches across
/// whatever this document <c>#include</c>s/<c>#import</c>s: a local
/// declared inside one script/function body is only offered while the
/// cursor is inside that same body - everything else (functions, script
/// names, globals, enum types/members, macros), from this file *and*
/// anything it pulls in, is offered everywhere.
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

    /// <summary>True compiler intrinsics (Print, Delay, SpawnSpot, ...) - never declared anywhere, so <c>CollectSymbolsVisibleAt</c> never sees them (see <see cref="BcsBuiltinFunctions"/>'s own remarks). Same shape as <see cref="KeywordItems"/> - a fixed list, computed once.</summary>
    private static readonly IEnumerable<CompletionItem> BuiltinItems = BcsBuiltinFunctions.AllNames
        .Select(name => new CompletionItem
        {
            Label = name,
            Kind = CompletionItemKind.Function,
            InsertText = name,
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
        var program = _documentStore.GetProgram(request.TextDocument.Uri);
        if (program == null) return Task.FromResult(new CompletionList(KeywordItems.Concat(BuiltinItems), isIncomplete: false));

        var line = request.Position.Line + 1; // LSP's 0-based line -> this parser's 1-based lines
        var symbolItems = program.CollectSymbolsVisibleAt(line)
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

        return Task.FromResult(new CompletionList(KeywordItems.Concat(BuiltinItems).Concat(symbolItems), isIncomplete: false));
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
