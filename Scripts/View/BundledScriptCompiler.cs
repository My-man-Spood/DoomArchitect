using Godot;

/// <summary>
/// Resolves this project's own bundled <c>zt-bcc</c> binary for the
/// current OS - a real executable checked into <c>Compilers/zt-bcc/&lt;platform&gt;/</c>
/// (only a Linux one exists so far; Windows/macOS builds are a separate
/// follow-up - see <c>TODO/resources-browser-panel.md</c>), resolved via
/// <see cref="ProjectSettings.GlobalizePath"/> the same way
/// <c>AppSettingsFile</c>/<c>MapSettingsFile</c> already resolve
/// <c>user://</c> paths to real ones. Works correctly for the
/// editor/dev-run case this project can actually test today, since
/// <c>res://</c> is real files on disk pre-export; whether this survives
/// being packed into an actual exported build as a directly
/// <c>Process.Start</c>-able loose file is explicitly unverified (see
/// that same TODO note) - this project currently has only one working
/// export preset at all.
/// </summary>
public static class BundledScriptCompiler
{
	/// <summary>Null if there's no bundled binary for this OS yet (e.g. Windows/macOS right now) - the caller treats that exactly like "nothing configured".</summary>
	public static string ResolvePath()
	{
		var platformFolder = OS.GetName() switch
		{
			"Windows" => "windows",
			"macOS" => "macos",
			_ => "linux",
		};
		var executableName = platformFolder == "windows" ? "zt-bcc.exe" : "zt-bcc";

		var resourcePath = $"res://Compilers/zt-bcc/{platformFolder}/{executableName}";
		var realPath = ProjectSettings.GlobalizePath(resourcePath);
		return System.IO.File.Exists(realPath) ? realPath : null;
	}

	/// <summary>
	/// <c>zcommon.acs</c>/<c>zcommon.bcs</c> and friends - confirmed NOT
	/// something the real engine ships (a real <c>gzdoom.pk3</c> has no
	/// such entries at all, checked directly) - these compatibility
	/// headers are distributed with the compiler itself. Copied verbatim
	/// from <c>zt-bcc</c>'s own upstream <c>lib/</c> folder (MIT-licensed,
	/// same repo the bundled binary is built from), platform-independent
	/// (plain text). Null if this bundled copy doesn't exist for some
	/// reason - same "nothing extra to offer" treatment as
	/// <see cref="ResolvePath"/>.
	/// </summary>
	public static string ResolveLibDirectory()
	{
		var realPath = ProjectSettings.GlobalizePath("res://Compilers/zt-bcc/lib");
		return System.IO.Directory.Exists(realPath) ? realPath : null;
	}
}
