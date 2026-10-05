namespace DoomArchitect.Core.IO;

/// <summary>
/// Resolves a map's own real "mod root" from the WAD it lives in - the
/// path everything about that mod's configuration (game configuration,
/// resources) should be scoped to, rather than the individual WAD file
/// itself. For a true standalone WAD (an IWAD, or a user's own single
/// multi-map WAD with no enclosing structure), the mod root is just that
/// WAD's own path - "per-WAD" and "per-mod" are the same thing there. For
/// the real GZDoom/ZDoom convention of a pk3-style mod folder holding one
/// small per-map WAD per map (<c>maps/MAP01.wad</c>, <c>maps/MAP02.wad</c>,
/// ...), the individual WAD is just an implementation detail of how that
/// *one* mod happens to store its maps - the mod root is the folder
/// itself, matching <see cref="DoomArchitect.Core.IO"/>'s own already-
/// established convention for finding this same <c>maps/</c> subfolder
/// (see <c>OpenMapMenu.FindMapsSubfolder</c>), reused here in reverse.
///
/// Deliberately pure string logic, no filesystem I/O at all - this is
/// about *detecting the convention*, not verifying the result actually
/// exists on disk; a caller that needs to know whether the detected root
/// is a real folder or a real file already has to check that itself
/// (e.g. deciding which <c>MapSettings</c> API to use).
/// </summary>
public static class ModRootDetector
{
    public static string DetectFrom(string wadPath)
    {
        var parent = Path.GetDirectoryName(wadPath);
        if (parent != null && Path.GetFileName(parent).Equals("maps", StringComparison.OrdinalIgnoreCase))
        {
            var grandparent = Path.GetDirectoryName(parent);
            if (grandparent != null) return grandparent;
        }

        return wadPath;
    }
}
