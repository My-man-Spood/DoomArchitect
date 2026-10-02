using System.Collections.Generic;
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

	private static readonly Color ErrorLineColor = new(1, 0, 0, 0.15f);
	private static readonly Color WarningLineColor = new(1, 1, 0, 0.12f);

	/// <summary>Null until <see cref="LoadFile"/> is called - an unsaved, as-yet-nameless document.</summary>
	public string FilePath => _filePath;

	/// <summary>The tab title - just the file's own name, matching how every other editor names an open-file tab.</summary>
	public string DisplayName => _filePath == null ? "untitled" : System.IO.Path.GetFileName(_filePath);

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
			RefreshBcsHighlightingAndDiagnostics();
		}
	}

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

		foreach (var line in _diagnosticLines) _codeEdit.SetLineBackgroundColor(line, Colors.Transparent);
		_diagnosticLines.Clear();

		var (_, diagnostics) = BcsParser.Parse(text);
		foreach (var diagnostic in diagnostics)
		{
			var line = Mathf.Clamp(diagnostic.Line - 1, 0, _codeEdit.GetLineCount() - 1);
			_codeEdit.SetLineBackgroundColor(line, diagnostic.Severity == BcsDiagnosticSeverity.Warning ? WarningLineColor : ErrorLineColor);
			_diagnosticLines.Add(line);
		}

		_codeEdit.QueueRedraw();
	}

	public void Save()
	{
		if (_filePath == null) return;

		using var file = Godot.FileAccess.Open(_filePath, Godot.FileAccess.ModeFlags.Write);
		file.StoreString(_codeEdit.Text);
	}

	public override void _UnhandledInput(InputEvent @event)
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
