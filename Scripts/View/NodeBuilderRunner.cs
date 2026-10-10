using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DoomArchitect.Core.Compilers;
using DoomArchitect.Core.IO;

/// <summary>
/// Shells out to the resolved <c>zdbsp</c> executable to rebuild
/// <c>ZNODES</c> for one map's own just-saved geometry - matching
/// Ultimate Doom Builder's own real node-building step (confirmed from
/// its source: <c>MapManager.cs</c>'s <c>SaveMap</c> calls
/// <c>BuildNodes</c> with a different configured nodebuilder profile
/// for a real save vs. testing) rather than this project's earlier,
/// safer-but-slower fallback of just dropping <c>ZNODES</c>/<c>BLOCKMAP</c>/
/// <c>REJECT</c> (<see cref="MapFileSaver.IsStaleAfterGeometryEdit"/>)
/// and relying on the source port to rebuild them at every single map
/// load. That drop-only behavior is still exactly what happens here if
/// zdbsp isn't bundled for this OS, or the build fails for any reason -
/// same "deliberately silent, correctness unaffected either way"
/// fallback <see cref="ScriptCompilerRunner"/> already established for
/// a missing/failing script compiler.
/// </summary>
public static class NodeBuilderRunner
{
	private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "DoomArchitect_NodeBuild");

	/// <summary>
	/// <paramref name="lumps"/> is the complete, about-to-be-written WAD
	/// content (every map/resource it already has, not just this one -
	/// <c>-m</c> targets just <paramref name="mapName"/>, and only that
	/// map's own group is ever touched on the way back in via
	/// <see cref="WadFile.WithGeometryLumpsFrom"/> - zdbsp's own
	/// handling of everything else in the file is never trusted
	/// wholesale). Returns <paramref name="lumps"/> completely
	/// unchanged if zdbsp isn't bundled for this OS, the map isn't
	/// found, or the build fails for any reason.
	/// </summary>
	public static IReadOnlyList<WadLump> Build(IReadOnlyList<WadLump> lumps, string mapName, bool forTesting)
	{
		var executablePath = BundledNodeBuilder.ResolvePath();
		if (executablePath == null) return lumps;

		var markerIndex = WadFile.FindMarkerIndex(lumps, mapName);
		if (markerIndex < 0) return lumps;

		if (Directory.Exists(TempDirectory)) Directory.Delete(TempDirectory, recursive: true);
		Directory.CreateDirectory(TempDirectory);

		try
		{
			var inputFile = Path.Combine(TempDirectory, "input.wad");
			var outputFile = Path.Combine(TempDirectory, "output.wad");
			File.WriteAllBytes(inputFile, WadWriter.Write(lumps));

			var arguments = ZdbspArguments.Build(inputFile, outputFile, mapName, forTesting);

			var processInfo = new ProcessStartInfo
			{
				FileName = executablePath,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			foreach (var arg in arguments) processInfo.ArgumentList.Add(arg);

			using var process = Process.Start(processInfo);
			if (process == null) return lumps;

			// Real, reported bug: reading two redirected streams
			// *sequentially* (stdout fully, then stderr) is a classic
			// .NET Process deadlock - confirmed live, this exact pattern
			// hung the whole app. zdbsp writes its own real-time
			// progress bar to stderr specifically (confirmed from its
			// own source, nodebuild.cpp's own fprintf calls - not stdout,
			// despite how it looks interleaved on a real terminal),
			// repeated once per percent of BSP progress; a complex
			// enough map produces enough of it to fill stderr's own OS
			// pipe buffer while nobody's draining it yet (this code was
			// still blocked waiting for *stdout* to reach EOF) - at
			// which point zdbsp itself blocks trying to write more, so
			// it never finishes, so stdout never reaches EOF either.
			// Reading both concurrently (ReadToEndAsync, not read-one-
			// then-the-other) is the standard, documented fix - neither
			// stream can ever back up far enough to block the other.
			var stdoutTask = process.StandardOutput.ReadToEndAsync();
			var stderrTask = process.StandardError.ReadToEndAsync();
			process.WaitForExit();
			Task.WaitAll(stdoutTask, stderrTask);
			if (process.ExitCode != 0 || !File.Exists(outputFile)) return lumps;

			var builtWad = WadFile.Read(outputFile);
			var builtMarkerIndex = WadFile.FindMarkerIndex(builtWad.Lumps, mapName);
			if (builtMarkerIndex < 0) return lumps;

			return WadFile.WithGeometryLumpsFrom(lumps, markerIndex, builtWad.Lumps, builtMarkerIndex);
		}
		catch (Exception)
		{
			return lumps;
		}
		finally
		{
			try { Directory.Delete(TempDirectory, recursive: true); }
			catch (Exception) { /* best-effort cleanup, same tolerance TestMapLauncher's own temp file reuse already has */ }
		}
	}
}
