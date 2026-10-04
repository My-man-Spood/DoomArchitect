using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.ZDoom.Bcs;
using Godot;

/// <summary>
/// A text editing tab - one <see cref="CodeEdit"/> (Godot's own built-in
/// code-editing control: line numbers, folding, basic editing all come
/// for free, no custom widget needed), a header showing the open file's
/// path, and load/save-to-disk. Language-aware only for <c>.bcs</c> files
/// so far (real syntax highlighting via <see cref="BcsSyntaxHighlighter"/>
/// and inline diagnostic line markers via <see cref="BcsParser"/>, both
/// driven in-process by the same library the standalone
/// <c>DoomArchitect.LanguageServer</c> project uses over LSP - see
/// TODO/bcs-lsp-foundation.md) - every other extension still opens as
/// plain text, same as before. "Script" just names the kind of file this
/// tab is for, the same way UDB's own script editor opens plain text
/// regardless of what's eventually compiled from it.
/// </summary>
public partial class ScriptDocument : VBoxContainer
{
	private Label _pathLabel;
	private CodeEdit _codeEdit;
	private string _filePath;
	private BcsSyntaxHighlighter _bcsHighlighter;
	private readonly HashSet<int> _diagnosticLines = new();
	private List<BcsDiagnostic> _diagnostics = new();
	private BcsProgram _bcsProgram;

	private static readonly Color ErrorLineColor = new(1, 0, 0, 0.15f);
	private static readonly Color WarningLineColor = new(1, 1, 0, 0.12f);

	/// <summary>Null until <see cref="LoadFile"/> is called - an unsaved, as-yet-nameless document.</summary>
	public string FilePath => _filePath;

	/// <summary>The tab title - just the file's own name, matching how every other editor names an open-file tab.</summary>
	public string DisplayName => _filePath == null ? "untitled" : System.IO.Path.GetFileName(_filePath);

	/// <summary>
	/// Raised when go-to-definition (<see cref="OnBcsSymbolLookup"/>)
	/// resolves to a declaration in a *different* file (reached via
	/// <c>#include</c>/<c>#import</c> - see <see cref="BcsProgram"/>) -
	/// this tab has no way to open another one itself (confirmed: no
	/// project/sibling-tab awareness at all), so <c>AppShell</c> - which
	/// owns the tab strip - handles it instead.
	/// </summary>
	public event Action<string, int, int> NavigateToFileRequested;

	/// <summary>Moves the caret to a declaration's own position and centers the viewport on it - the same thing <see cref="OnBcsSymbolLookup"/> already does for a same-file match, extracted so <c>AppShell</c> can also call it once it's switched to (or just opened) this tab for a cross-file jump.</summary>
	public void NavigateTo(int line, int column)
	{
		_codeEdit.SetCaretLine(line - 1);
		_codeEdit.SetCaretColumn(column - 1);
		_codeEdit.CenterViewportToCaret();
	}

	public override void _Ready()
	{
		_pathLabel = GetNode<Label>("Header/PathLabel");
		_codeEdit = GetNode<CodeEdit>("CodeEdit");
	}

	public void LoadFile(string path)
	{
		_filePath = path;
		_pathLabel.Text = path;
		_codeEdit.Text = Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsString(path) : "";

		var extension = System.IO.Path.GetExtension(path);
		// BCS is documented as "mostly compatible" with ACS (an extension of
		// it, not a separate language - github.com/zeta-group/zt-bcc's own
		// README/wiki) - the only two known incompatibilities are &&/||
		// short-circuiting (a behavior difference the parser can't see
		// either way, not a syntax error) and a handful of previously-legal
		// identifiers now being reserved words, so using the same BCS-
		// grounded tokenizer/highlighter for .acs files is a reasonable,
		// deliberate choice here, not an oversight that it's not actually
		// parsing "real" ACS grammar.
		if (extension.Equals(".bcs", System.StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".acs", System.StringComparison.OrdinalIgnoreCase))
		{
			_bcsHighlighter = new BcsSyntaxHighlighter();
			_codeEdit.SyntaxHighlighter = _bcsHighlighter;
			_codeEdit.TextChanged += RefreshBcsHighlightingAndDiagnostics;
			_codeEdit.SetTooltipRequestFunc(Callable.From<string, string>(GetBcsTooltip));

			_codeEdit.CodeCompletionEnabled = true; // false by default on a bare CodeEdit node
			_codeEdit.CodeCompletionRequested += OnBcsCodeCompletionRequested;
			_codeEdit.TextChanged += RequestBcsCodeCompletionIfWordLongEnough;

			// Godot's own Ctrl/Cmd+Click-to-navigate mechanism (confirmed from
			// scene/gui/code_edit.cpp): SymbolValidate fires on Ctrl/Cmd-held
			// mouse motion over a word (no position - same mouse-position
			// derivation GetBcsTooltip already needs), answered via
			// SetSymbolLookupWordAsValid; SymbolLookup then fires only on an
			// actual Ctrl/Cmd+Click on a word already marked valid, handing
			// over the click's own position directly - no custom word/hit
			// detection needed on this side at all.
			_codeEdit.SymbolLookupOnClick = true;
			_codeEdit.SymbolValidate += OnBcsSymbolValidate;
			_codeEdit.SymbolLookup += OnBcsSymbolLookup;

			RefreshBcsHighlightingAndDiagnostics();
		}
	}

	/// <summary>
	/// Confirmed from Godot's own text_edit.cpp: this callback only fires
	/// when the mouse is over a recognized "word" (TextEdit's own
	/// select_word) and is handed that word's text directly - the real
	/// line still has to be derived independently via
	/// <see cref="TextEdit.GetLineColumnAtPos"/> (the callback gives no
	/// position), but the word itself is exactly what
	/// <see cref="BcsCompilationUnit.FindDeclaration"/> needs to resolve
	/// "what is this identifier." A diagnostic on the hovered line always
	/// wins (same priority as before this existed); if there isn't one,
	/// falls back to resolving the word to a declaration. Known, accepted
	/// limitation: hovering blank columns (trailing whitespace, a line
	/// that's just an unmatched brace) won't show anything, since
	/// <c>select_word</c> never calls this callback for a position with no
	/// word at all - not worth a custom popup/mouse-motion workaround for
	/// what this feature needs today.
	///
	/// The returned text is already BBCode-ready for
	/// <see cref="BcsCodeEdit"/>'s custom tooltip to display as-is - which
	/// of <see cref="BcsBbcodeFormatter"/>'s two paths applies depends on
	/// which of these two cases produced it (a diagnostic's own English
	/// message vs. a real declaration's description), so the choice is
	/// made here, not inside the formatter or the tooltip control.
	/// </summary>
	private string GetBcsTooltip(string word)
	{
		var mousePos = (Vector2I)_codeEdit.GetLocalMousePosition();
		var line = _codeEdit.GetLineColumnAtPos(mousePos).Y;

		var messages = _diagnostics.Where(d => d.Line - 1 == line).Select(d => d.Message).ToList();
		if (messages.Count > 0) return BcsBbcodeFormatter.EscapePlainText(string.Join("\n", messages));

		var declaration = _bcsProgram?.FindDeclaration(word, line + 1);
		if (declaration is not { } found) return "";

		var signature = BcsBbcodeFormatter.ColorizeCode(found.Describe());
		if (string.IsNullOrEmpty(found.DocComment)) return signature;

		// DocComment is plain prose (a leading // or /* */ block, see
		// BcsParser.ExtractDocComment) - escaped, not colorized, same
		// reasoning as a diagnostic message: it isn't code, and
		// re-tokenizing arbitrary prose with the BCS lexer is what
		// silently dropped quote characters around quoted punctuation
		// before (see BcsBbcodeFormatter's own remarks).
		return $"{BcsBbcodeFormatter.EscapePlainText(found.DocComment)}\n\n{signature}";
	}

	private const int MinCompletionPrefixLength = 2;

	/// <summary>
	/// <see cref="CodeEdit.CodeCompletionRequested"/> is manual-only by
	/// default (Ctrl+Space) - typing alone never requests completion
	/// unless something asks for it on every edit, confirmed real Godot
	/// behavior, not a bug. Calling <see cref="CodeEdit.RequestCodeCompletion"/>
	/// unconditionally on every edit was tried first and was too
	/// aggressive in practice (popping up after a single letter, or right
	/// after a space) - Godot's own internal "is the caret in a word"
	/// check it does before firing doesn't enforce a minimum length or
	/// care that a space was *just* typed, only whether one currently
	/// sits there. This re-implements that check with an actual minimum
	/// prefix length instead, by looking at the word-character run
	/// immediately before the caret on the current line.
	/// </summary>
	private void RequestBcsCodeCompletionIfWordLongEnough()
	{
		var line = _codeEdit.GetLine(_codeEdit.GetCaretLine());
		var caretColumn = _codeEdit.GetCaretColumn();

		var start = caretColumn;
		while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_')) start--;

		if (caretColumn - start >= MinCompletionPrefixLength) _codeEdit.RequestCodeCompletion();
	}

	/// <summary>
	/// Keywords (the real 53-entry reserved-word list) plus every name
	/// visible from the caret's current line - via
	/// <see cref="BcsCompilationUnit.CollectSymbolsVisibleAt"/>, not the
	/// older flat <c>CollectSymbols()</c>: a local declared inside one
	/// script/function body is only offered while editing that same body
	/// - everything else (functions, script names, globals, enum
	/// types/members, macros) is still offered everywhere, same as
	/// before. Deliberately not word-based: a plain identifier that only
	/// ever appears as a *use*, never a declaration, is not offered.
	/// Dedupes by name+kind, case-insensitively (BCS itself is
	/// case-insensitive) - <see cref="BcsSymbol"/> now carries its own
	/// declaration position, so two distinct declarations of the same
	/// name (e.g. the same local name reused across two different
	/// scripts) no longer collapse for free the way they used to.
	/// </summary>
	private void OnBcsCodeCompletionRequested()
	{
		foreach (var keyword in BcsTokenizer.ReservedWordTexts)
		{
			_codeEdit.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Keyword, keyword, keyword);
		}

		var line = _codeEdit.GetCaretLine() + 1; // 0-based caret line -> this parser's 1-based lines
		var symbols = _bcsProgram?.CollectSymbolsVisibleAt(line) ?? Array.Empty<BcsSymbol>();
		foreach (var symbol in symbols.DistinctBy(s => (s.Name.ToLowerInvariant(), s.Kind)))
		{
			_codeEdit.AddCodeCompletionOption(ToCodeCompletionKind(symbol.Kind), symbol.Name, symbol.Name);
		}

		_codeEdit.UpdateCodeCompletionOptions(true);
	}

	/// <summary>
	/// Fired on Ctrl/Cmd-held mouse motion over a word (confirmed from
	/// Godot's own source - no position is handed over, only the word
	/// text, same limitation already documented for <see cref="GetBcsTooltip"/>'s
	/// own tooltip callback) - derives the hovered line the same way, then
	/// answers whether <paramref name="symbol"/> resolves to a real
	/// declaration from there.
	/// </summary>
	private void OnBcsSymbolValidate(string symbol)
	{
		var mousePos = (Vector2I)_codeEdit.GetLocalMousePosition();
		var line = _codeEdit.GetLineColumnAtPos(mousePos).Y;
		_codeEdit.SetSymbolLookupWordAsValid(_bcsProgram?.FindDeclaration(symbol, line + 1) != null);
	}

	/// <summary>
	/// Fired only on an actual Ctrl/Cmd+Click on a word already marked
	/// valid by <see cref="OnBcsSymbolValidate"/> - <paramref name="line"/>/
	/// <paramref name="column"/> here are the click's own 0-based position
	/// (confirmed from source), used only to re-resolve the symbol at the
	/// moment of the click rather than trusting whatever was last
	/// validated during hover. If the match lives in a *different* file
	/// (reached via <c>#include</c>/<c>#import</c>), this tab can't jump
	/// there itself - raises <see cref="NavigateToFileRequested"/> for
	/// <c>AppShell</c> to handle instead; otherwise jumps directly via
	/// <see cref="NavigateTo"/> - this project's own equivalent of a real
	/// editor's "go to definition."
	/// </summary>
	private void OnBcsSymbolLookup(string symbol, long line, long column)
	{
		var declaration = _bcsProgram?.FindDeclaration(symbol, (int)line + 1);
		if (declaration is not { } found) return;

		if (!string.IsNullOrEmpty(found.SourcePath) && found.SourcePath != _filePath)
		{
			NavigateToFileRequested?.Invoke(found.SourcePath, found.Line, found.Column);
			return;
		}

		NavigateTo(found.Line, found.Column);
	}

	private static CodeEdit.CodeCompletionKind ToCodeCompletionKind(BcsSymbolKind kind) => kind switch
	{
		BcsSymbolKind.Function => CodeEdit.CodeCompletionKind.Function,
		BcsSymbolKind.Variable => CodeEdit.CodeCompletionKind.Variable,
		BcsSymbolKind.EnumType => CodeEdit.CodeCompletionKind.Enum,
		BcsSymbolKind.EnumMember => CodeEdit.CodeCompletionKind.Constant, // Godot has no dedicated "enum member" kind - Constant is the closest honest fit
		BcsSymbolKind.Macro => CodeEdit.CodeCompletionKind.Constant, // same reasoning - a #define'd name is conceptually a compile-time constant
		_ => CodeEdit.CodeCompletionKind.PlainText,
	};

	/// <summary>
	/// Re-tokenizes/re-parses the whole buffer on every edit - the same
	/// "small file, cheap to redo from scratch" approach
	/// <c>DoomArchitect.LanguageServer</c> uses, not debounced, since a
	/// real BCS script is small enough that this is still effectively
	/// instant. Clears every previously-marked diagnostic line first
	/// (back to fully transparent, i.e. no tint) rather than only the
	/// ones still relevant, since which lines are relevant can only be
	/// known after re-parsing anyway.
	/// </summary>
	private void RefreshBcsHighlightingAndDiagnostics()
	{
		var text = _codeEdit.Text;
		_bcsHighlighter.Rebuild(text);

		// Belt-and-suspenders alongside BcsSyntaxHighlighter.Rebuild's own
		// UpdateCache() call: that clears the highlighter *resource's*
		// cache, but adding/removing a line (confirmed live, still wrong
		// with only UpdateCache()) shifts every line below it to a new
		// line number, and CodeEdit itself may keep separate per-line
		// highlighting state that only UpdateCache() doesn't reach.
		// Unassigning and reassigning the highlighter forces CodeEdit's
		// own "a highlighter was just set" full-reinitialization path
		// instead of relying on a narrower cache-clear API to cover it.
		_codeEdit.SyntaxHighlighter = null;
		_codeEdit.SyntaxHighlighter = _bcsHighlighter;

		foreach (var line in _diagnosticLines) _codeEdit.SetLineBackgroundColor(line, Colors.Transparent);
		_diagnosticLines.Clear();

		_bcsProgram = BcsParser.ParseProgram(text, _filePath, ReadBcsFileFromDisk);
		_diagnostics = _bcsProgram.Diagnostics;
		foreach (var diagnostic in _diagnostics)
		{
			var line = Mathf.Clamp(diagnostic.Line - 1, 0, _codeEdit.GetLineCount() - 1);
			_codeEdit.SetLineBackgroundColor(line, diagnostic.Severity == BcsDiagnosticSeverity.Warning ? WarningLineColor : ErrorLineColor);
			_diagnosticLines.Add(line);
		}

		_codeEdit.QueueRedraw();
	}

	/// <summary>The <c>readFile</c> delegate <see cref="BcsParser.ParseProgram"/> needs to resolve an <c>#include</c>/<c>#import</c> - mirrors <see cref="LoadFile"/>'s own existence-check-then-read shape.</summary>
	private static string ReadBcsFileFromDisk(string path) =>
		Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsString(path) : null;

	public void Save()
	{
		if (_filePath == null) return;

		using var file = Godot.FileAccess.Open(_filePath, Godot.FileAccess.ModeFlags.Write);
		file.StoreString(_codeEdit.Text);
	}

	/// <summary>
	/// <c>_Input</c>, not <c>_UnhandledInput</c> - a focused <see cref="CodeEdit"/>
	/// consumes every keyboard event during its own GUI processing (the
	/// normal "am I being typed into" behavior), which runs *before*
	/// <c>_UnhandledInput</c> ever sees anything. Ctrl+S has no built-in
	/// meaning to <c>CodeEdit</c>, so with this in <c>_UnhandledInput</c>
	/// it never fired at all - the key event fell through to plain
	/// character insertion instead, which is why it was typing a literal
	/// "s" rather than saving (confirmed live). <c>_Input</c> runs before
	/// GUI dispatch, so catching it here and calling
	/// <see cref="Viewport.SetInputAsHandled"/> stops it from reaching
	/// <see cref="CodeEdit"/> at all.
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (!Visible) return;
		if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

		if (key.IsActionPressed("save_document"))
		{
			Save();
			GetViewport().SetInputAsHandled();
		}
	}
}
