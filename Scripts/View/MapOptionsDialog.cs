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
	private OptionButton _gameConfigOption;
	private ResourceListEditor _resourceListEditor;

	public override void _Ready()
	{
		_gameConfigOption = GetNode<OptionButton>("Container/GameConfigOption");
		_gameConfigOption.AddItem("Doom");
		_gameConfigOption.AddItem("Doom2");

		_resourceListEditor = GetNode<ResourceListEditor>("Container/ResourceListEditor");
		_resourceListEditor.HintText =
			"Additional resources (e.g. the IWAD) - lower items override higher ones, this map's own file always wins.";
	}

	public void SetGameConfiguration(GameConfigurationKind kind) =>
		_gameConfigOption.Selected = kind == GameConfigurationKind.Doom2 ? 1 : 0;

	public GameConfigurationKind GetGameConfiguration() =>
		_gameConfigOption.Selected == 1 ? GameConfigurationKind.Doom2 : GameConfigurationKind.Doom;

	public void SetResourcePaths(IReadOnlyList<string> paths) => _resourceListEditor.SetResourcePaths(paths);

	public IReadOnlyList<string> GetResourcePaths() => _resourceListEditor.GetResourcePaths();

	public IReadOnlyList<WadFile> GetResourceWads() => _resourceListEditor.GetResourceWads();
}
