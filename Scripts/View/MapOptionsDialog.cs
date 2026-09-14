using System;
using System.Collections.Generic;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;
using Godot;

/// <summary>
/// The combined "which game does this map belong to, and what additional
/// resources (e.g. its IWAD) should it use" prompt - matches UDB's own
/// real single "Open Map Options" dialog (config + resources together)
/// rather than two separate steps. Shown both when a WAD is first opened
/// and (unchanged) when revisiting the currently loaded map's options -
/// see <c>OpenMapMenu</c>.
/// </summary>
public partial class MapOptionsDialog : AcceptDialog
{
	private static readonly GameConfigurationKind[] Kinds =
	{
		GameConfigurationKind.Doom, GameConfigurationKind.Doom2, GameConfigurationKind.GZDoomDoom2UDMF,
	};

	private OptionButton _gameConfigOption;
	private ResourceListEditor _resourceListEditor;

	public override void _Ready()
	{
		_gameConfigOption = GetNode<OptionButton>("Container/GameConfigOption");
		foreach (var kind in Kinds) _gameConfigOption.AddItem(kind.ToString());

		_resourceListEditor = GetNode<ResourceListEditor>("Container/ResourceListEditor");
		_resourceListEditor.HintText =
			"Additional resources (e.g. the IWAD, or a PK3 like gzdoom.pk3) - lower items override higher ones, this map's own file always wins.";
	}

	public void SetGameConfiguration(GameConfigurationKind kind) =>
		_gameConfigOption.Selected = Array.IndexOf(Kinds, kind);

	public GameConfigurationKind GetGameConfiguration() => Kinds[_gameConfigOption.Selected];

	public void SetResourcePaths(IReadOnlyList<string> paths) => _resourceListEditor.SetResourcePaths(paths);

	public IReadOnlyList<string> GetResourcePaths() => _resourceListEditor.GetResourcePaths();

	public IReadOnlyList<IResourceContainer> GetResourceContainers() => _resourceListEditor.GetResourceContainers();
}
