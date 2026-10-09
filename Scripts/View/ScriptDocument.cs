using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Compilers;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.ZDoom.Bcs;
using DoomArchitect.Settings;
using Godot;

/// <summary>
/// A text editing tab - one <see cref="CodeEdit"/> (Godot's own built-in
/// code-editing control: line numbers, folding, basic editing all come
/// for free, no custom widget needed), a header showing the open file's
/// path, and load/save-back. File-backed (<see cref="LoadFile"/>, a real
/// path on disk), lump-backed (<see cref="LoadLump"/>, a byte array
/// already read from a specific lump inside a specific WAD - the
/// resource browser's own "Open" on a <c>SCRIPTS</c>/<c>ZSCRIPT</c> lump),
/// or PK3-entry-backed (<see cref="LoadPk3Entry"/>, a byte array already
/// read from a specific entry inside a real <c>.pk3</c> zip archive) -
/// exactly one of <see cref="FilePath"/>/<see cref="_wadSourcePath"/>/
/// <see cref="_pk3SourcePath"/> is ever set for a given tab. Language-aware only for BCS/ACS source so
/// far (real syntax highlighting via <see cref="BcsSyntaxHighlighter"/>
/// and inline diagnostic line markers via <see cref="BcsParser"/>, both
/// driven in-process by the same library the standalone
/// <c>DoomArchitect.LanguageServer</c> project uses over LSP - see
/// TODO/bcs-lsp-foundation.md) - triggered by a <c>.bcs</c>/<c>.acs</c>
/// extension for a file-backed tab, or a <c>SCRIPTS</c> lump name for a
/// lump-backed one (its content *is* genuine ACS source, same grammar) -
/// every other extension/lump name still opens as plain text, same as
/// before (a <c>ZSCRIPT</c> lump included - no ZScript highlighting
/// exists in this project yet). "Script" just names the kind of file
/// this tab is for, the same way UDB's own script editor opens plain
/// text regardless of what's eventually compiled from it.
/// </summary>
public partial class ScriptDocument : VBoxContainer
{
	private Label _pathLabel;
	private CodeEdit _codeEdit;
	private string _filePath;
	private string _wadSourcePath;
	private int _lumpIndex;
	private string _lumpName;
	private string _pk3SourcePath;
	private string _pk3EntryPath;
	private BcsSyntaxHighlighter _bcsHighlighter;
	private readonly HashSet<int> _diagnosticLines = new();
	private List<BcsDiagnostic> _diagnostics = new();
	private BcsProgram _bcsProgram;
	private ResourceSet _includeResources;

	private static readonly Color ErrorLineColor = new(1, 0, 0, 0.15f);
	private static readonly Color WarningLineColor = new(1, 1, 0, 0.12f);

	/// <summary>Null for a lump-backed tab (see <see cref="LoadLump"/>) or an unsaved, as-yet-nameless document.</summary>
	public string FilePath => _filePath;

	/// <summary>The tab title - the file's own name for a file-backed tab, "LUMPNAME (wad.wad)" for a lump-backed one, "entry/path (mod.pk3)" for a PK3-entry-backed one, matching how every other editor names an open-file tab.</summary>
	public string DisplayName => _filePath != null
		? System.IO.Path.GetFileName(_filePath)
		: _lumpName != null ? $"{_lumpName} ({System.IO.Path.GetFileName(_wadSourcePath)})"
		: _pk3EntryPath != null ? $"{_pk3EntryPath} ({System.IO.Path.GetFileName(_pk3SourcePath)})"
		: "untitled";

	/// <summary>Whether this tab is already the lump at <paramref name="lumpIndex"/> inside the WAD at <paramref name="wadSourcePath"/> - the lump-backed counterpart of comparing <see cref="FilePath"/> directly, used the same way to focus an already-open tab instead of duplicating it.</summary>
	public bool IsLumpFrom(string wadSourcePath, int lumpIndex) =>
		_wadSourcePath != null && _lumpIndex == lumpIndex
		&& string.Equals(System.IO.Path.GetFullPath(_wadSourcePath), System.IO.Path.GetFullPath(wadSourcePath), StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Which resources this tab's own <c>#include</c>/<c>#import</c>
	/// directives can resolve against beyond a real on-disk sibling file -
	/// the same ones the currently active map tab is using (see
	/// <c>AppShell.CurrentMapResourcePaths</c>), so a bare name like
	/// <c>"zcommon.acs"</c> can resolve the same way real compilation
	/// does (<see cref="ResourceSet.FindIncludeText"/>). An empty list
	/// (no map active when this tab was opened) just means nothing extra
	/// resolves - the exact same disk-only behavior this had before.
	/// </summary>
	public void SetIncludeResourcePaths(IReadOnlyList<string> resourcePaths) =>
		_includeResources = new ResourceSet(resourcePaths.Select(ResourceContainerCache.Open).ToList());

	/// <summary>The PK3-entry-backed counterpart of <see cref="IsLumpFrom"/> - whether this tab is already the entry at <paramref name="entryPath"/> inside the PK3 at <paramref name="pk3SourcePath"/>.</summary>
	public bool IsPk3EntryFrom(string pk3SourcePath, string entryPath) =>
		_pk3SourcePath != null && string.Equals(_pk3EntryPath, entryPath, StringComparison.OrdinalIgnoreCase)
		&& string.Equals(System.IO.Path.GetFullPath(_pk3SourcePath), System.IO.Path.GetFullPath(pk3SourcePath), StringComparison.OrdinalIgnoreCase);

	/// <summary>Tints this tab's own lines for a just-finished save's compile errors, reusing the exact <see cref="CodeEdit.SetLineBackgroundColor"/>/<see cref="_diagnosticLines"/> mechanism the live BCS-parser diagnostics above already use - cleared the same way, by the next edit's own <see cref="RefreshBcsHighlightingAndDiagnostics"/>. An empty list is a deliberate no-op (a successful compile doesn't need to clear anything itself - the next edit already will).</summary>
	public void ShowCompileErrors(IReadOnlyList<ScriptCompileError> errors)
	{
		foreach (var error in errors)
		{
			var line = Mathf.Clamp(error.Line - 1, 0, _codeEdit.GetLineCount() - 1);
			_codeEdit.SetLineBackgroundColor(line, ErrorLineColor);
			_diagnosticLines.Add(line);
		}
	}

	/// <summary>
	/// Raised when go-to-definition (<see cref="OnBcsSymbolLookup"/>)
	/// resolves to a declaration in a *different* file (reached via
	/// <c>#include</c>/<c>#import</c> - see <see cref="BcsProgram"/>) -
	/// this tab has no way to open another one itself (confirmed: no
	/// project/sibling-tab awareness at all), so <c>AppShell</c> - which
	/// owns the tab strip - handles it instead.
	/// </summary>
	public event Action<string, int, int> NavigateToFileRequested;

	/// <summary>Whether this tab has unsaved changes - a plain bool, unlike <c>MapView</c>'s own undo-stack-version-based <c>IsDirty</c> (<see cref="CodeEdit"/> has its own native undo/redo this project doesn't drive, so there's no version to track against); set on every edit, cleared on load or a successful save.</summary>
	public bool IsDirty => _dirty;

	/// <summary>Fired whenever <see cref="IsDirty"/>'s result changes - <c>AppShell</c>'s own reason to care: refreshing this tab's own dirty-dot indicator the instant it happens, not poll for it.</summary>
	public event Action DirtyChanged;

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
		// Unconditional (unlike InitializeBcsLanguageSupport's own
		// TextChanged subscription, which only fires for BCS-highlighted
		// tabs) - every tab's dirty state needs tracking, not just those.
		_codeEdit.TextChanged += () => SetDirty(true);
	}

	private bool _dirty;

	private void SetDirty(bool dirty)
	{
		if (_dirty == dirty) return;
		_dirty = dirty;
		DirtyChanged?.Invoke();
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
			InitializeBcsLanguageSupport();
		}

		// Last, not first: setting _codeEdit.Text above may itself fire
		// TextChanged (Godot's own CodeEdit, not this project's concern to
		// predict precisely) - this is what corrects that back to clean
		// regardless, rather than relying on it not happening.
		SetDirty(false);
	}

	/// <summary>
	/// Lump-backed counterpart of <see cref="LoadFile"/> - populates the
	/// editor directly from already-read bytes (no disk read at all), for
	/// a lump the resource browser's own "Open" action resolved (see
	/// <c>AppShell.OnResourceOpenRequested</c>). <paramref name="lumpIndex"/>
	/// is the lump's own real position in <paramref name="wadPath"/>'s own
	/// lump list (<c>ResourceTreeNode.LumpIndex</c>) - needed, not just
	/// <paramref name="lumpName"/>, since a WAD can have more than one lump
	/// sharing a name (a Hexen-format WAD's own per-map <c>SCRIPTS</c>
	/// lump, one per map) - <see cref="Save"/> has to write back to this
	/// exact lump, not just "the first/any lump with this name".
	/// </summary>
	public void LoadLump(string wadPath, int lumpIndex, string lumpName, byte[] data)
	{
		_wadSourcePath = wadPath;
		_lumpIndex = lumpIndex;
		_lumpName = lumpName;
		_pathLabel.Text = $"{lumpName} ({System.IO.Path.GetFileName(wadPath)})";
		_codeEdit.Text = System.Text.Encoding.UTF8.GetString(data);

		// SCRIPTS lump content is genuine ACS source, same grammar .acs/
		// .bcs already use - ZSCRIPT (or anything else) falls through to
		// plain text, same as every other currently-unrecognized
		// extension/lump name already does (no ZScript highlighting
		// exists in this project yet).
		if (lumpName.Equals("SCRIPTS", System.StringComparison.OrdinalIgnoreCase))
		{
			InitializeBcsLanguageSupport();
		}

		SetDirty(false);
	}

	/// <summary>
	/// PK3-entry-backed counterpart of <see cref="LoadFile"/>/<see cref="LoadLump"/> -
	/// populates the editor directly from already-read bytes, for an entry
	/// the resource browser's own "Open" action resolved from inside a real
	/// <c>.pk3</c> zip archive. <paramref name="entryPath"/> is the entry's
	/// own full in-archive path (e.g. <c>"scripts/mylib.acs"</c> or the
	/// bare root-level <c>"SCRIPTS"</c>) - a PK3's own entries are already
	/// uniquely identified by path (unlike a WAD lump), so no separate
	/// index is needed the way <see cref="LoadLump"/> needs one.
	/// </summary>
	public void LoadPk3Entry(string pk3Path, string entryPath, byte[] data)
	{
		_pk3SourcePath = pk3Path;
		_pk3EntryPath = entryPath;
		_pathLabel.Text = $"{entryPath} ({System.IO.Path.GetFileName(pk3Path)})";
		_codeEdit.Text = System.Text.Encoding.UTF8.GetString(data);

		// Combines LoadFile's extension check and LoadLump's bare-name
		// check, since a PK3 entry can legitimately be shaped either way -
		// a real .acs/.bcs file, or a bare SCRIPTS entry mirroring the WAD
		// lump name convention. ZSCRIPT (bare or .zs) falls through to
		// plain text, same as everywhere else.
		var extension = System.IO.Path.GetExtension(entryPath);
		var baseName = System.IO.Path.GetFileNameWithoutExtension(entryPath);
		if (extension.Equals(".bcs", System.StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".acs", System.StringComparison.OrdinalIgnoreCase) ||
			baseName.Equals("SCRIPTS", System.StringComparison.OrdinalIgnoreCase))
		{
			InitializeBcsLanguageSupport();
		}

		SetDirty(false);
	}

	private void InitializeBcsLanguageSupport()
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
		if (declaration is not { } found)
		{
			// Not a real declared symbol - a true compiler intrinsic like
			// Print/Delay/SpawnSpot (see BcsBuiltinFunctions's own remarks)
			// has no declaration to find at all, but is still worth a
			// signature on hover.
			var builtin = BcsBuiltinFunctions.TryDescribe(word);
			if (builtin == null) return "";

			var builtinSignature = BcsBbcodeFormatter.ColorizeCode(builtin);
			var builtinDoc = BcsFunctionDocs.Format(word);
			return builtinDoc == null ? builtinSignature : $"{BcsBbcodeFormatter.EscapePlainText(builtinDoc)}\n\n{builtinSignature}";
		}

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

		// True compiler intrinsics (Print, Delay, SpawnSpot, ...) - never
		// declared anywhere, so CollectSymbolsVisibleAt above never sees
		// them (see BcsBuiltinFunctions's own remarks).
		foreach (var name in BcsBuiltinFunctions.AllNames)
		{
			_codeEdit.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Function, name, name);
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

		_bcsProgram = BcsParser.ParseProgram(text, _filePath, ReadBcsFile);
		// Only this tab's own file - an empty SourcePath is BcsDiagnostic's
		// own "the main file" convention (BcsDiagnostic.cs). Without this
		// filter, a diagnostic raised while reading an #include'd file
		// (e.g. a real external library this project's own BCS parser
		// doesn't yet fully understand - confirmed directly: the real
		// zcommon.bcs alone produces 1102 of them) gets clamped onto and
		// painted over this tab's own, completely unrelated lines, once
		// per diagnostic - wrong regardless of how many there are, and
		// with enough of them, slow enough to look like a hang.
		_diagnostics = _bcsProgram.Diagnostics.Where(d => d.SourcePath.Length == 0).ToList();
		foreach (var diagnostic in _diagnostics)
		{
			var line = Mathf.Clamp(diagnostic.Line - 1, 0, _codeEdit.GetLineCount() - 1);
			_codeEdit.SetLineBackgroundColor(line, diagnostic.Severity == BcsDiagnosticSeverity.Warning ? WarningLineColor : ErrorLineColor);
			_diagnosticLines.Add(line);
		}

		_codeEdit.QueueRedraw();
	}

	/// <summary>
	/// The <c>readFile</c> delegate <see cref="BcsParser.ParseProgram"/>
	/// needs to resolve an <c>#include</c>/<c>#import</c> - a real
	/// on-disk sibling file first (mirrors <see cref="LoadFile"/>'s own
	/// existence-check-then-read shape), then <see cref="_includeResources"/>
	/// for a bare name that isn't one (e.g. a mapper's own shared library,
	/// which has no meaningful directory for a lump/PK3-entry-backed tab -
	/// see <see cref="SetIncludeResourcePaths"/>), then
	/// <see cref="BundledScriptCompiler.ResolveLibDirectory"/>'s own
	/// <c>zcommon.acs</c>/<c>zcommon.bcs</c> and friends last - confirmed
	/// NOT something the real engine ships (a real <c>gzdoom.pk3</c> has
	/// no such entries at all), so a mapper's own resource, if they
	/// happen to have one, still wins over this fallback.
	/// </summary>
	private string ReadBcsFile(string path)
	{
		if (Godot.FileAccess.FileExists(path)) return Godot.FileAccess.GetFileAsString(path);

		var fromResources = _includeResources?.FindIncludeText(path);
		if (fromResources != null) return fromResources;

		var libDirectory = BundledScriptCompiler.ResolveLibDirectory();
		if (libDirectory == null) return null;

		var bundledPath = System.IO.Path.Combine(libDirectory, System.IO.Path.GetFileName(path));
		return System.IO.File.Exists(bundledPath) ? System.IO.File.ReadAllText(bundledPath) : null;
	}

	public void Save()
	{
		if (_filePath != null)
		{
			using var file = Godot.FileAccess.Open(_filePath, Godot.FileAccess.ModeFlags.Write);
			file.StoreString(_codeEdit.Text);
			SetDirty(false);
			return;
		}

		if (_wadSourcePath != null)
		{
			SaveLump();
			SetDirty(false);
			return;
		}

		if (_pk3SourcePath != null)
		{
			SavePk3Entry();
			SetDirty(false);
		}
	}

	/// <summary>
	/// Re-reads the WAD fresh (not holding some stale in-memory copy from
	/// whenever this tab was opened), splices in this lump's new bytes via
	/// the generic <see cref="WadFile.WithReplacedLumpData"/>, rebuilds via
	/// <see cref="WadWriter.Write"/>, backs up (<c>.bak</c>) then
	/// overwrites - the exact same pattern <c>OpenMapMenu.WriteMapToFile</c>
	/// already uses for every map save, not a new one; never an in-place
	/// binary patch.
	/// </summary>
	private void SaveLump()
	{
		var wad = WadFile.Read(_wadSourcePath);
		var newLumps = WadFile.WithReplacedLumpData(wad.Lumps, _lumpIndex, System.Text.Encoding.UTF8.GetBytes(_codeEdit.Text));
		var bytes = WadWriter.Write(newLumps);

		if (System.IO.File.Exists(_wadSourcePath)) System.IO.File.Move(_wadSourcePath, _wadSourcePath + ".bak", overwrite: true);
		System.IO.File.WriteAllBytes(_wadSourcePath, bytes);
		// Real, reported bug: without this, re-opening this exact lump
		// (a fresh tab, or the resource browser's own tree) kept serving
		// the pre-save content from the still-cached WadFile instance -
		// SavePk3Entry already did this, this path just missed it.
		ResourceContainerCache.Invalidate(_wadSourcePath);
	}

	/// <summary>
	/// The PK3 counterpart of <see cref="SaveLump"/>: opens a fresh,
	/// independent read of the archive, splices in this entry's new bytes
	/// via <see cref="Pk3File.WithReplacedEntry"/>, rebuilds the whole
	/// archive via <see cref="Pk3Writer.Write"/>, then overwrites - no
	/// <c>.bak</c> backup here, unlike <see cref="SaveLump"/>, matching
	/// UDB's own real <c>PK3Reader.SaveFile</c> (confirmed by reading its
	/// source directly), which backs up WAD/map saves but not PK3 saves.
	/// The fresh read is disposed (end of the <c>using</c> block) before
	/// the overwrite runs - sequential, never a concurrent read+write
	/// handle on the same path. <see cref="ResourceContainerCache.Invalidate"/>
	/// drops any stale cached container for this path so the next tab
	/// that opens it sees the saved change.
	/// </summary>
	private void SavePk3Entry()
	{
		IReadOnlyList<(string Path, byte[] Data)> entries;
		using (var pk3 = Pk3File.Open(_pk3SourcePath))
		{
			entries = pk3.WithReplacedEntry(_pk3EntryPath, System.Text.Encoding.UTF8.GetBytes(_codeEdit.Text));
		}

		var bytes = Pk3Writer.Write(entries);
		System.IO.File.WriteAllBytes(_pk3SourcePath, bytes);
		ResourceContainerCache.Invalidate(_pk3SourcePath);
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
