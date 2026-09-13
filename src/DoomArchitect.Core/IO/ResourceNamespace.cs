namespace DoomArchitect.Core.IO;

/// <summary>
/// The resource "kinds" a container can be asked to enumerate - in a WAD
/// these are marker-bounded lump ranges (e.g. <c>P_START</c>/<c>P_END</c>);
/// in a PK3 they're fixed folder names (e.g. <c>patches/</c>) standing in
/// for the same real GZDoom/ZDoom convention. Only the five namespaces
/// <see cref="Textures.TextureSet"/> actually consumes today are modeled -
/// HiRes/Colormaps/Voxels are real namespaces too but nothing reads them
/// yet from either a WAD or a PK3, so they're left out until something
/// needs them.
/// </summary>
public enum ResourceNamespace
{
    Patches,
    Textures,
    Flats,
    Sprites,
    Graphics,
}
