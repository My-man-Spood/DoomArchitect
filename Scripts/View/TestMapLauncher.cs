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
/// for the current map's own game configuration, forces a real save
/// first (<see cref="OpenMapMenu.SaveMapThen"/> - matching UDB's own
/// Test Map, which recompiles before testing too via that same save
/// path; this is also what actually runs script compilation, so a
/// stale/missing <c>BEHAVIOR</c> lump is no longer possible), writes a
/// throwaway temp WAD copy of the just-saved result, builds the launch
/// arguments via <see cref="TestLaunchCommandBuilder"/>, and starts the
/// source port as a genuinely independent process via
/// <see cref="OS.CreateProcess(string,string[],bool)"/> - this project's
/// first use of process-launching.
///
/// A fresh, uniquely-named temp file is written for every launch (the
/// previous one, if any, is best-effort deleted first) rather than one
/// fixed path reused across a whole session (UDB's own approach, tried
/// here first) - switched after a real, reported case of Test Map
/// showing stale geometry despite a confirmed-correct save (verified:
/// the temp WAD DoomArchitect built was byte-for-byte identical to the
/// freshly-saved file on disk) that survived even a full DoomArchitect
/// restart, which rules out anything *this* process caches - something
/// downstream (most likely the source port itself, or the OS) appeared
/// to be keying off the reused path rather than the file's actual
/// content. Not confirmed against the source port's own code - a
/// pragmatic, safe hardening either way.
/// </summary>
public static class TestMapLauncher
{
	private static string _previousTempWadPath;


	/// <summary>
	/// Reports any failure via <paramref name="onError"/> rather than a
	/// plain return value - unlike before this forced a save first, this
	/// can no longer always resolve synchronously (a map that's never
	/// been saved pops a Save As dialog and only actually writes later,
	/// if/when the user picks a file - see <see cref="OpenMapMenu.SaveMapThen"/>'s
	/// own remarks on that).
	///
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
	public static void Launch(OpenMapMenu openMapMenu, int skill, bool noMonsters, Action<string> onError)
	{
		if (openMapMenu.CurrentMapName == null)
		{
			onError("No map is currently loaded.");
			return;
		}

		var kind = openMapMenu.CurrentGameConfigurationKind;
		var settings = AppSettingsFile.Load();
		var engines = settings.GetTestEngines(kind);
		var activeIndex = settings.GetActiveTestEngineIndex(kind);

		if (activeIndex < 0 || activeIndex >= engines.Count)
		{
			onError($"No test engine configured for {kind} yet - set one up in Preferences > Test Engines.");
			return;
		}

		var engine = engines[activeIndex];
		if (string.IsNullOrWhiteSpace(engine.ExecutablePath) || !File.Exists(engine.ExecutablePath))
		{
			onError($"Test engine \"{engine.Name}\"'s executable path is missing or doesn't exist:\n{engine.ExecutablePath}");
			return;
		}

		var resourcePaths = openMapMenu.CurrentResourcePaths;
		if (resourcePaths.Count == 0)
		{
			onError("No IWAD/resources configured for this map yet - set one via Map > Map Options...");
			return;
		}

		var gameConfiguration = GameConfigurations.Get(kind);
		var template = engine.UseCustomParameters && !string.IsNullOrWhiteSpace(engine.CustomParameters)
			? engine.CustomParameters
			: gameConfiguration.TestParameters;

		if (string.IsNullOrWhiteSpace(template))
		{
			onError($"{kind} has no Test Map command-line template configured.");
			return;
		}

		// Everything above is a static precondition check, independent of
		// the map's own saved state; everything below needs the just-saved
		// result (freshly compiled BEHAVIOR included), so it's deferred
		// until the save actually completes.
		openMapMenu.SaveMapThen(() => LaunchAfterSave(openMapMenu, skill, noMonsters, engine, gameConfiguration, template, resourcePaths, onError));
	}

	private static void LaunchAfterSave(OpenMapMenu openMapMenu, int skill, bool noMonsters, TestEngine engine, IGameConfiguration gameConfiguration, string template, IReadOnlyList<string> resourcePaths, Action<string> onError)
	{
		if (_previousTempWadPath != null)
		{
			try { File.Delete(_previousTempWadPath); }
			catch (Exception) { /* best-effort - a locked/already-gone previous temp file isn't fatal to this launch */ }
		}

		var tempWadPath = Path.Combine(Path.GetTempPath(), $"DoomArchitect_TestMap_{Guid.NewGuid():N}.wad");
		try
		{
			File.WriteAllBytes(tempWadPath, openMapMenu.BuildCurrentMapBytes());
		}
		catch (Exception ex)
		{
			onError($"Couldn't write the temporary test WAD: {ex.Message}");
			return;
		}

		_previousTempWadPath = tempWadPath;

		var additionalResourcePaths = ExcludeRequiredArchives(resourcePaths.Skip(1), gameConfiguration);

		var arguments = TestLaunchCommandBuilder.Build(
			template, tempWadPath, resourcePaths[0], additionalResourcePaths,
			openMapMenu.CurrentMapName, skill, noMonsters);

		var pid = OS.CreateProcess(engine.ExecutablePath, arguments.ToArray());
		if (pid == -1) onError($"Couldn't launch \"{engine.Name}\" ({engine.ExecutablePath}).");
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
