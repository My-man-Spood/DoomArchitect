using System;
using System.Collections.Generic;
using DoomArchitect.Core.Geometry;
using Godot;

/// <summary>
/// "Check Map for Errors" results - mirrors <see cref="MapSelectDialog"/>'s
/// own minimal shape exactly (one <see cref="ItemList"/>, both
/// <see cref="ItemList.ItemActivated"/> and <see cref="AcceptDialog.Confirmed"/>
/// resolving the selected row), just carrying the real
/// <see cref="MapErrorResult"/> back out instead of a plain row index -
/// the caller needs the actual elements/bounds involved to select and
/// zoom to them, not just which row was picked.
/// </summary>
public partial class MapErrorCheckDialog : AcceptDialog
{
	public event Action<MapErrorResult> ResultActivated;

	private ItemList _list;
	private IReadOnlyList<MapErrorResult> _results = Array.Empty<MapErrorResult>();

	public override void _Ready()
	{
		_list = GetNode<ItemList>("ItemList");
		_list.ItemActivated += index =>
		{
			if (index >= _results.Count) return; // the "No issues found." placeholder row
			Hide();
			ResultActivated?.Invoke(_results[(int)index]);
		};

		Confirmed += () =>
		{
			var selected = _list.GetSelectedItems();
			if (selected.Length > 0 && selected[0] < _results.Count) ResultActivated?.Invoke(_results[selected[0]]);
		};
	}

	public void SetResults(IReadOnlyList<MapErrorResult> results)
	{
		_results = results;
		_list.Clear();

		if (results.Count == 0)
		{
			_list.AddItem("No issues found.");
			_list.SetItemDisabled(0, true);
			return;
		}

		foreach (var result in results) _list.AddItem(result.Description);
	}
}
