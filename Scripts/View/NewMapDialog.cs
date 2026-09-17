using System;
using Godot;

/// <summary>
/// A small "name your new map" dialog - the map-slot name (e.g. "MAP01")
/// entered before <c>OpenMapMenu.ShowNewMapDialog</c> hands off to the same
/// Map Options (game config + resources) flow Open Map already uses.
/// </summary>
public partial class NewMapDialog : AcceptDialog
{
	public event Action<string> MapNameEntered;

	private LineEdit _nameEdit;

	public override void _Ready()
	{
		_nameEdit = GetNode<LineEdit>("NameEdit");
		_nameEdit.TextChanged += _ => UpdateOkButtonEnabled();

		Confirmed += () => MapNameEntered?.Invoke(_nameEdit.Text.Trim());
	}

	public void PopupWithDefault(string defaultName)
	{
		_nameEdit.Text = defaultName;
		UpdateOkButtonEnabled();
		PopupCentered();
		_nameEdit.GrabFocus();
		_nameEdit.SelectAll();
	}

	private void UpdateOkButtonEnabled()
	{
		GetOkButton().Disabled = string.IsNullOrWhiteSpace(_nameEdit.Text);
	}
}
