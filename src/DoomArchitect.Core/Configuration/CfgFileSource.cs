using System.Reflection;

namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Where <see cref="CfgLoader"/> reads a <c>.cfg</c> file's text from, and
/// how it resolves an <c>include()</c> path relative to the file that
/// referenced it. Two implementations: DoomArchitect's own bundled
/// <c>Doom.cfg</c>/<c>Doom2.cfg</c> are embedded resources (so they're
/// covered by Core's own unit tests with no Godot dependency), but a real
/// UDB <c>.cfg</c> file a user points DoomArchitect at is a plain file on
/// disk - both need <c>include()</c> to work, which is why this is an
/// interface rather than <see cref="CfgLoader"/> hardcoding
/// <see cref="File"/> calls.
/// </summary>
public interface ICfgFileSource
{
    string ReadText(string path);

    string ResolveRelative(string basePath, string relativePath);
}

public sealed class FileSystemCfgFileSource : ICfgFileSource
{
    public string ReadText(string path) => File.ReadAllText(path);

    public string ResolveRelative(string basePath, string relativePath)
    {
        // Real UDB .cfg files always write include() paths with a
        // backslash (its own Windows-native convention) - Path.Combine
        // only treats that as a separator on Windows itself, so on any
        // other platform the whole literal "Includes\Foo.cfg" string
        // would otherwise be read as a single (nonexistent) file name.
        var normalizedRelativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(baseDirectory, normalizedRelativePath));
    }
}

/// <summary>
/// Reads <c>.cfg</c> files bundled as embedded resources under a fixed
/// resource-name prefix (e.g. <c>DoomArchitect.Core.Configuration.GameConfigs</c>),
/// addressed by a "virtual path" using forward slashes (e.g.
/// <c>Includes/VanillaCommon.cfg</c>) that mirrors the real folder layout
/// under that prefix in the project - the same mapping the SDK's own
/// default embedded-resource naming uses (folder separators become dots).
/// </summary>
public sealed class EmbeddedResourceCfgFileSource : ICfgFileSource
{
    private readonly Assembly _assembly;
    private readonly string _resourceRootPrefix;

    public EmbeddedResourceCfgFileSource(Assembly assembly, string resourceRootPrefix)
    {
        _assembly = assembly;
        _resourceRootPrefix = resourceRootPrefix;
    }

    public string ReadText(string path)
    {
        var resourceName = $"{_resourceRootPrefix}.{path.Replace('/', '.')}";
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded game-configuration resource not found: '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public string ResolveRelative(string basePath, string relativePath)
    {
        // Real UDB .cfg files always write include() paths with a
        // backslash (its own Windows-native convention) - this virtual
        // path space only ever uses forward slashes, so that literal
        // backslash needs normalizing first or it ends up baked into the
        // resource name as-is instead of acting as a separator.
        var normalizedRelativePath = relativePath.Replace('\\', '/');
        var lastSlash = basePath.LastIndexOf('/');
        var baseDirectory = lastSlash >= 0 ? basePath[..lastSlash] : string.Empty;
        var combined = baseDirectory.Length > 0 ? $"{baseDirectory}/{normalizedRelativePath}" : normalizedRelativePath;
        return Normalize(combined);
    }

    private static string Normalize(string virtualPath)
    {
        var segments = new List<string>();
        foreach (var segment in virtualPath.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == ".." && segments.Count > 0) segments.RemoveAt(segments.Count - 1);
            else segments.Add(segment);
        }

        return string.Join('/', segments);
    }
}
