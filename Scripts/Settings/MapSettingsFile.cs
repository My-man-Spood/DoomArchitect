using System.IO;
using DoomArchitect.Core.Configuration;
using Godot;

namespace DoomArchitect.Settings;

/// <summary>
/// Reads/writes one WAD's <c>.dbs</c> sidecar file - the same extension
/// UDB uses (see <see cref="Core.Configuration.MapSettings"/>'s own
/// remarks on the exact shape), so an existing UDB <c>.dbs</c> already
/// sitting next to a WAD is read tolerantly (unmodeled fields survive a
/// load-then-save round-trip untouched) and only ever updated for the
/// fields DoomArchitect actually manages.
/// </summary>
public static class MapSettingsFile
{
    public static string PathFor(string wadPath) => Path.ChangeExtension(wadPath, ".dbs");

    public static MapSettings Load(string wadPath)
    {
        var path = PathFor(wadPath);
        if (!File.Exists(path)) return MapSettings.Empty();

        try
        {
            return MapSettings.Parse(File.ReadAllText(path));
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to read map settings at '{path}' - starting fresh. {ex.Message}");
            return MapSettings.Empty();
        }
    }

    public static void Save(string wadPath, MapSettings settings) =>
        File.WriteAllText(PathFor(wadPath), settings.ToText());
}
