using Godot;

/// <summary>
/// Resolves this project's own bundled <c>zdbsp</c> binary for the
/// current OS - a real executable checked into
/// <c>Compilers/zdbsp/&lt;platform&gt;/</c> (only a Linux one exists so
/// far, same gap as <see cref="BundledScriptCompiler"/>'s own Windows/
/// macOS builds - see <c>TODO/resources-browser-panel.md</c>). Built
/// from zdbsp's own real upstream source (GNU GPL v2 - see
/// <c>Compilers/zdbsp/LICENSE-zdbsp</c>), the exact tool Ultimate Doom
/// Builder itself bundles for node-building, confirmed from its own
/// real <c>zdbsp.cfg</c> node-builder profiles.
/// </summary>
public static class BundledNodeBuilder
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
		var executableName = platformFolder == "windows" ? "zdbsp.exe" : "zdbsp";

		var resourcePath = $"res://Compilers/zdbsp/{platformFolder}/{executableName}";
		var realPath = ProjectSettings.GlobalizePath(resourcePath);
		return System.IO.File.Exists(realPath) ? realPath : null;
	}
}
