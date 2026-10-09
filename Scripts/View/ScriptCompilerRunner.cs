using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DoomArchitect.Core.Compilers;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.ZDoom.Bcs;
using DoomArchitect.Settings;

/// <summary>What <see cref="ScriptCompilerRunner.Compile"/> resolved to - exactly one of "nothing to do" (no compiler available at all), a successful compile, or a failed one.</summary>
public sealed class ScriptCompileOutcome
{
	public bool IsConfigured { get; private init; }
	public byte[] BehaviorBytes { get; private init; }
	public IReadOnlyList<ScriptCompileError> Errors { get; private init; } = Array.Empty<ScriptCompileError>();

	public static ScriptCompileOutcome NotConfigured() => new() { IsConfigured = false };
	public static ScriptCompileOutcome Success(byte[] behaviorBytes) => new() { IsConfigured = true, BehaviorBytes = behaviorBytes };
	public static ScriptCompileOutcome Failure(IReadOnlyList<ScriptCompileError> errors) => new() { IsConfigured = true, Errors = errors };
}

/// <summary>
/// Shells out to the resolved <c>zt-bcc</c> executable (the Preferences
/// override if set and present, otherwise <see cref="BundledScriptCompiler"/>'s
/// own per-OS resolution) to compile one map's <c>SCRIPTS</c> source into
/// <c>BEHAVIOR</c> bytecode - this project's first synchronous,
/// output-capturing process launch (<see cref="TestMapLauncher"/>'s own
/// <c>OS.CreateProcess</c> is fire-and-forget by design, wrong shape here),
/// so it uses plain <see cref="Process"/> directly, exactly what Ultimate
/// Doom Builder's own compiler integration does.
/// </summary>
public static class ScriptCompilerRunner
{
	private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "DoomArchitect_Compile");

	/// <summary>
	/// <paramref name="resourcePaths"/> supplies additional <c>#include</c>/<c>#import</c>
	/// resolution: real on-disk folders join the compiler's own <c>-i</c>
	/// search directories directly, while every configured resource
	/// (WAD/PK3 included) is also opened into a <see cref="ResourceSet"/>
	/// and searched by name for anything the script's own includes
	/// reference that isn't a real sibling file (<c>ExtractResourceIncludes</c>
	/// below) - mirroring UDB's own real approach: its `AccCompiler.Run()`
	/// runs its own lightweight ACS preprocessor purely to *discover*
	/// `#include`/`#import` names, resolves each via
	/// `DataManager.GetTextResourceData` (a generic "search every loaded
	/// resource" lookup - confirmed from its source), and physically
	/// copies the resolved text into the compiler's temp directory before
	/// invoking the real external binary, which then finds them exactly
	/// like normal sibling files via its own <c>-i</c> search. This
	/// project's own <see cref="BcsParser"/> (built for the LSP, not a
	/// compiler, but the same shape of tool) plays the same discovery
	/// role here via <see cref="BcsProgram.IncludedPaths"/>.
	/// </summary>
	public static ScriptCompileOutcome Compile(string wadPath, byte[] scriptSource, IReadOnlyList<string> resourcePaths)
	{
		var executablePath = ResolveExecutablePath();
		if (executablePath == null) return ScriptCompileOutcome.NotConfigured();

		if (Directory.Exists(TempDirectory)) Directory.Delete(TempDirectory, recursive: true);
		Directory.CreateDirectory(TempDirectory);

		try
		{
			var inputFile = Path.Combine(TempDirectory, "SCRIPTS.bcs");
			var outputFile = Path.Combine(TempDirectory, "SCRIPTS.o");
			File.WriteAllBytes(inputFile, scriptSource);

			var resources = new ResourceSet(resourcePaths.Select(ResourceContainerCache.Open).ToList());
			ExtractResourceIncludes(scriptSource, resources);

			var includeDirs = new List<string> { TempDirectory };
			var wadDirectory = Path.GetDirectoryName(wadPath);
			if (wadDirectory != null) includeDirs.Add(wadDirectory);
			includeDirs.AddRange(resourcePaths.Where(Directory.Exists));
			// Last, so a mapper's own same-named resource (if they have
			// one) still wins a name collision - zt-bcc searches -i dirs
			// in order, first match wins.
			var bundledLibDirectory = BundledScriptCompiler.ResolveLibDirectory();
			if (bundledLibDirectory != null) includeDirs.Add(bundledLibDirectory);

			var arguments = ZtBccArguments.Build(inputFile, outputFile, includeDirs);

			var processInfo = new ProcessStartInfo
			{
				FileName = executablePath,
				UseShellExecute = false,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			foreach (var arg in arguments) processInfo.ArgumentList.Add(arg);

			using var process = Process.Start(processInfo);
			if (process == null) return ScriptCompileOutcome.Failure(new[] { new ScriptCompileError(null, 0, $"Couldn't launch the script compiler ({executablePath}).") });

			var stderr = process.StandardError.ReadToEnd();
			process.WaitForExit();

			if (process.ExitCode == 0 && File.Exists(outputFile))
			{
				return ScriptCompileOutcome.Success(File.ReadAllBytes(outputFile));
			}

			var errors = ZtBccErrorParser.Parse(stderr);
			return ScriptCompileOutcome.Failure(errors.Count > 0
				? errors
				: new[] { new ScriptCompileError(null, 0, $"The script compiler exited with code {process.ExitCode} and produced no output.") });
		}
		finally
		{
			try { Directory.Delete(TempDirectory, recursive: true); }
			catch (Exception) { /* best-effort cleanup, same tolerance TestMapLauncher's own temp file reuse already has */ }
		}
	}

	/// <summary>
	/// Parses just enough to discover <paramref name="scriptSource"/>'s
	/// own full <c>#include</c>/<c>#import</c> closure (ignoring any
	/// diagnostics the parse itself raises - this pass only exists to
	/// resolve resource-backed includes, not to validate syntax; a real
	/// error is still correctly caught and reported by the actual
	/// <c>zt-bcc</c> run that follows), then physically writes each one
	/// <paramref name="resources"/> can resolve but the filesystem can't
	/// into <see cref="TempDirectory"/> under its own referenced name -
	/// so the real compiler's own include search (already given
	/// <see cref="TempDirectory"/> as one of its own <c>-i</c> dirs)
	/// finds it exactly like a normal sibling file.
	/// </summary>
	private static void ExtractResourceIncludes(byte[] scriptSource, ResourceSet resources)
	{
		var discovery = BcsParser.ParseProgram(Encoding.UTF8.GetString(scriptSource), null, resources.FindIncludeText);
		foreach (var included in discovery.IncludedPaths)
		{
			if (File.Exists(included)) continue; // a genuinely absolute path - already a real file, nothing to extract

			var text = resources.FindIncludeText(included);
			if (text == null) continue; // not resolvable either way - the real compiler will report its own "not found" below

			var target = Path.Combine(TempDirectory, included);
			var targetDirectory = Path.GetDirectoryName(target);
			if (targetDirectory != null) Directory.CreateDirectory(targetDirectory);
			if (!File.Exists(target)) File.WriteAllBytes(target, Encoding.UTF8.GetBytes(text));
		}
	}

	private static string ResolveExecutablePath()
	{
		var overridePath = AppSettingsFile.Load().GetScriptCompilerPathOverride();
		if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)) return overridePath;

		return BundledScriptCompiler.ResolvePath();
	}
}
