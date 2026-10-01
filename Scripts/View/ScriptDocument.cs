using Godot;

/// <summary>
/// A plain-text editing tab - one <see cref="CodeEdit"/> (Godot's own
/// built-in code-editing control: line numbers, folding, basic editing all
/// come for free, no custom widget needed), a header showing the open
/// file's path, and load/save-to-disk. Deliberately not language-aware -
/// no ACS/BCS/ZScript syntax highlighting or compiler integration here;
/// that's its own later initiative once a scripting language is actually
/// chosen (see TODO/documents-and-tabs.md). "Script" just names the kind
/// of file this tab is for, the same way UDB's own script editor opens
/// plain text regardless of what's eventually compiled from it.
/// </summary>
public partial class ScriptDocument : VBoxContainer
{
	private Label _pathLabel;
	private CodeEdit _codeEdit;
	private string _filePath;

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
