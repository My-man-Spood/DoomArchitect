using System.IO;
using DoomArchitect.Core.Configuration;
using Godot;

namespace DoomArchitect.Settings;

/// <summary>
/// Reads/writes DoomArchitect's own global, app-wide settings (currently
/// just per-game-configuration default resources) at a fixed location
/// under Godot's own per-user data directory - global, not tied to any
/// specific map/WAD (see <see cref="MapSettingsFile"/> for that). All
/// actual parsing/formatting lives in <see cref="AppSettings"/>
/// (Godot-free, unit-testable); this class only resolves the real path
/// and reads/writes bytes.
/// </summary>
public static class AppSettingsFile
{
    private const string VirtualPath = "user://settings.cfg";

    public static AppSettings Load()
    {
        var path = ProjectSettings.GlobalizePath(VirtualPath);
        if (!File.Exists(path)) return AppSettings.Empty();

        try
        {
            return AppSettings.Parse(File.ReadAllText(path));
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Failed to read app settings at '{path}' - starting fresh. {ex.Message}");
            return AppSettings.Empty();
        }
    }

    public static void Save(AppSettings settings)
    {
        var path = ProjectSettings.GlobalizePath(VirtualPath);
        File.WriteAllText(path, settings.ToText());
    }
}
