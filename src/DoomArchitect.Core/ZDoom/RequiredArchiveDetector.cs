using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Answers "is this specific resource the archive a `.cfg`
/// <see cref="RequiredArchive"/> describes" by content, not file name -
/// e.g. recognizing gzdoom.pk3 by the fact that it actually defines a
/// class named `Actor` and contains `x11r6rgb.txt`, matching UDB's own
/// real `ResourceOptionsForm.CheckRequiredArchives`. Uses the real
/// <see cref="ZScriptParser"/>/<see cref="DecorateParser"/> from this
/// port for the class check - a genuine content fingerprint, not an
/// approximation - checked directly against <see cref="ZScriptParser.DeclaredClassNames"/>
/// (no <see cref="ZScriptParser.CompleteParsing"/> needed, so an unrelated
/// class elsewhere in the same resource failing full inheritance
/// resolution can never cause a false negative here).
/// </summary>
public static class RequiredArchiveDetector
{
    public static bool Matches(RequiredArchive archive, IResourceContainer container)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.LumpName != null && container.FindLump(Path.GetFileNameWithoutExtension(entry.LumpName)) == null)
                return false;

            if (entry.ClassName != null && !DefinesClass(container, entry.ClassName))
                return false;
        }

        return true;
    }

    private static bool DefinesClass(IResourceContainer container, string className)
    {
        var lower = className.ToLowerInvariant();

        var zscriptRoot = container.FindLump("ZSCRIPT");
        if (zscriptRoot != null)
        {
            var zscript = new ZScriptParser { OnInclude = container.FindByPath };
            zscript.Parse(zscriptRoot.Data, "ZSCRIPT");
            if (zscript.DeclaredClassNames.Contains(lower)) return true;
        }

        var decorateRoot = container.FindLump("DECORATE");
        if (decorateRoot != null)
        {
            var decorate = new DecorateParser { OnInclude = container.FindByPath };
            decorate.Parse(decorateRoot.Data, "DECORATE");
            if (decorate.AllActorsByClass.ContainsKey(lower)) return true;
        }

        return false;
    }
}
