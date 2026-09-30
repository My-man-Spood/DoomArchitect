using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.ZDoom;
using DoomArchitect.Settings;
using Godot;

/// <summary>
/// Test Map's own actual launch - resolves the active <see cref="TestEngine"/>
/// for the current map's own game configuration, writes a throwaway temp
/// WAD (never the map's real saved file), builds the launch arguments via
/// <see cref="TestLaunchCommandBuilder"/>, and starts the source port as a
/// genuinely independent process via <see cref="OS.CreateProcess(string,string[],bool)"/> -
/// this project's first use of process-launching. A single, fixed temp
/// file path is reused across repeated tests in the same session (deleted
/// and rewritten each time) rather than a fresh one per launch, matching
/// UDB's own real one-temp-file-per-session approach.
/// </summary>
public static class TestMapLauncher
{
	/// <summary>
	/// This project's resource list has no distinct "which one is the
	/// IWAD" concept (see <see cref="MapOptionsDialog"/>'s own remarks - it's
	/// one flat, layered priority list, the map's own file always highest).
	/// Test Map needs one specific answer for <c>%WP</c>/<c>%WF</c>
	/// regardless, so the *first* configured resource is treated as the
	/// IWAD by convention (the natural order a mapper would add them in),
	/// everything after it as additional <c>%AP</c> resources - a
	/// deliberate simplification, not a hidden assumption elsewhere in the
	/// codebase.
	/// </summary>
	public static string Launch(OpenMapMenu openMapMenu, int skill, bool noMonsters)
	{
		if (openMapMenu.CurrentMapName == null)
		{
			return "No map is currently loaded.";
		}

		var kind = openMapMenu.CurrentGameConfigurationKind;
		var settings = AppSettingsFile.Load();
		var engines = settings.GetTestEngines(kind);
		var activeIndex = settings.GetActiveTestEngineIndex(kind);

		if (activeIndex < 0 || activeIndex >= engines.Count)
		{
			return $"No test engine configured for {kind} yet - set one up in Preferences > Test Engines.";
		}

		var engine = engines[activeIndex];
		if (string.IsNullOrWhiteSpace(engine.ExecutablePath) || !File.Exists(engine.ExecutablePath))
		{
			return $"Test engine \"{engine.Name}\"'s executable path is missing or doesn't exist:\n{engine.ExecutablePath}";
		}

		var resourcePaths = openMapMenu.CurrentResourcePaths;
		if (resourcePaths.Count == 0)
		{
			return "No IWAD/resources configured for this map yet - set one via Map > Map Options...";
		}

		var gameConfiguration = GameConfigurations.Get(kind);
		var template = engine.UseCustomParameters && !string.IsNullOrWhiteSpace(engine.CustomParameters)
			? engine.CustomParameters
			: gameConfiguration.TestParameters;

		if (string.IsNullOrWhiteSpace(template))
		{
			return $"{kind} has no Test Map command-line template configured.";
		}

		var tempWadPath = Path.Combine(Path.GetTempPath(), "DoomArchitect_TestMap.wad");
		try
		{
			File.WriteAllBytes(tempWadPath, openMapMenu.BuildCurrentMapBytes());
		}
		catch (Exception ex)
		{
			return $"Couldn't write the temporary test WAD: {ex.Message}";
		}

		var additionalResourcePaths = ExcludeRequiredArchives(resourcePaths.Skip(1), gameConfiguration);

		var arguments = TestLaunchCommandBuilder.Build(
			template, tempWadPath, resourcePaths[0], additionalResourcePaths,
			openMapMenu.CurrentMapName, skill, noMonsters);

		var pid = OS.CreateProcess(engine.ExecutablePath, arguments.ToArray());
		return pid == -1 ? $"Couldn't launch \"{engine.Name}\" ({engine.ExecutablePath})." : null;
	}

	/// <summary>
	/// Drops any resource that matches a `.cfg` `RequiredArchive` flagged
	/// `ExcludeFromTesting` (e.g. gzdoom.pk3) - the source port already
	/// loads that content internally, so forwarding it again produces
	/// "overriding core lump" warnings at best. A resource that can't be
	/// opened for the check is kept rather than dropped, so it still fails
	/// normally downstream instead of silently vanishing from the launch.
	/// </summary>
	private static List<string> ExcludeRequiredArchives(IEnumerable<string> paths, IGameConfiguration gameConfiguration)
	{
		var excludable = gameConfiguration.GetRequiredArchives().Where(a => a.ExcludeFromTesting).ToList();
		if (excludable.Count == 0) return paths.ToList();

		var result = new List<string>();
		foreach (var path in paths)
		{
			IResourceContainer container;
			try
			{
				container = ResourceContainerFactory.Open(path);
			}
			catch (Exception)
			{
				result.Add(path);
				continue;
			}

			try
			{
				if (!excludable.Any(archive => RequiredArchiveDetector.Matches(archive, container))) result.Add(path);
			}
			finally
			{
				if (container is IDisposable disposable) disposable.Dispose();
			}
		}

		return result;
	}
}
