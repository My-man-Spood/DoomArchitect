using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.Tests.Configuration;

/// <summary>A fake <see cref="ICfgFileSource"/> backed by an in-memory dictionary, so <c>include()</c> resolution can be tested without touching disk.</summary>
internal sealed class InMemoryCfgFileSource : ICfgFileSource
{
    private readonly Dictionary<string, string> _files;
    public int ReadCount { get; private set; }

    public InMemoryCfgFileSource(Dictionary<string, string> files)
    {
        _files = files;
    }

    public string ReadText(string path)
    {
        ReadCount++;
        return _files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);
    }

    public string ResolveRelative(string basePath, string relativePath)
    {
        var lastSlash = basePath.LastIndexOf('/');
        var baseDirectory = lastSlash >= 0 ? basePath[..lastSlash] : string.Empty;
        return baseDirectory.Length > 0 ? $"{baseDirectory}/{relativePath}" : relativePath;
    }
}
