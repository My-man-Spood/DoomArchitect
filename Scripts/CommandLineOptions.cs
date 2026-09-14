using System.Collections.Generic;
using System.IO;
using Godot;

/// <summary>
/// Reads dev-only <c>--file</c>/<c>--map</c> launch arguments so a
/// specific WAD/map can auto-load on startup instead of going through
/// <c>File &gt; Open Map...</c> every time - meant to be set once in
/// Godot's own "Debug &gt; Customize Run Instances" dialog (its "Main Run
/// Args" field) for faster manual testing, not a real UDB feature (UDB
/// itself has no command-line map-loading either).
///
/// Checks both <see cref="OS.GetCmdlineUserArgs"/> (the args after a
/// literal <c>--</c>/<c>++</c>, which is what a normal terminal launch or
/// an explicit editor "Main Run Args" value ending up after that
/// separator would populate) and the full <see cref="OS.GetCmdlineArgs"/>
/// - unclear from here, with no way to click Play in a GUI to check
/// directly, whether the editor's "Main Run Args"/"Customize Run
/// Instances" mechanism always inserts that separator itself before
/// appending the configured string to the spawned instance's command
/// line, or just appends it directly with no separator. `--file`/`--map`
/// aren't real engine flag names, so scanning the unfiltered full args
/// too costs nothing and covers either behavior.
///
/// Supports both <c>--file=value</c> and <c>--file value</c> forms -
/// Godot's own engine args use the former, but the latter seemed easier
/// to type by hand into "Main Run Args".
/// </summary>
public static class CommandLineOptions
{
	public static bool TryGetFileAndMap(out string filePath, out string mapName)
	{
		var options = Parse(OS.GetCmdlineUserArgs());
		var fullArgsOptions = Parse(OS.GetCmdlineArgs());
		foreach (var (key, value) in fullArgsOptions) options.TryAdd(key, value);

		filePath = ExpandHome(options.GetValueOrDefault("file"));
		mapName = options.GetValueOrDefault("map");

		return !string.IsNullOrEmpty(filePath) && !string.IsNullOrEmpty(mapName);
	}

	/// <summary>
	/// A leading <c>~</c> is a shell convention (bash/zsh expand it before
	/// the program ever sees it) - Godot's "Main Run Args" passes the
	/// string straight through with no shell involved, so a path typed the
	/// way you'd type it at a terminal would otherwise silently fail to
	/// resolve. Only a bare <c>~</c> or a leading <c>~/</c> is expanded
	/// (not <c>~otheruser/...</c> - resolving another account's home
	/// directory isn't worth the platform-specific lookup for a dev-only
	/// convenience flag).
	/// </summary>
	private static string ExpandHome(string path)
	{
		if (string.IsNullOrEmpty(path) || path[0] != '~') return path;
		if (path.Length > 1 && path[1] != '/') return path;

		var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
		return path.Length == 1 ? home : Path.Combine(home, path[2..]);
	}

	private static Dictionary<string, string> Parse(string[] args)
	{
		var result = new Dictionary<string, string>();

		for (var i = 0; i < args.Length; i++)
		{
			var arg = args[i];
			if (!arg.StartsWith("--")) continue;

			var key = arg[2..];
			var equalsIndex = key.IndexOf('=');
			if (equalsIndex >= 0)
			{
				result[key[..equalsIndex]] = key[(equalsIndex + 1)..];
			}
			else if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
			{
				result[key] = args[++i];
			}
			else
			{
				result[key] = "";
			}
		}

		return result;
	}
}
