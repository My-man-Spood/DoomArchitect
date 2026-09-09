using System;
using System.Collections.Generic;
using Godot;

/// <summary>A small "pick a map from this WAD" dialog - shown only when a WAD contains more than one map (see <c>OpenMapMenu</c>).</summary>
public partial class MapSelectDialog : AcceptDialog
{
	public event Action<int> MapActivated;

	private ItemList _list;

	public override void _Ready()
	{
		_list = GetNode<ItemList>("ItemList");
		_list.ItemActivated += index =>
		{
			Hide();
			MapActivated?.Invoke((int)index);
		};

		Confirmed += () =>
		{
			var selected = _list.GetSelectedItems();
			if (selected.Length > 0) MapActivated?.Invoke(selected[0]);
		};
	}

	public void SetMaps(IReadOnlyList<string> names)
	{
		_list.Clear();
		foreach (var name in names) _list.AddItem(name);
	}
}
