namespace DoomArchitect.Core.Textures;

/// <summary>One patch's placement within a composite texture, resolved to an actual patch name via PNAMES.</summary>
public sealed record PatchPlacement(int OriginX, int OriginY, string PatchName);

/// <summary>A parsed TEXTURE1/TEXTURE2 entry: a named canvas of a given size, built from one or more patches.</summary>
public sealed record CompositeTextureDefinition(string Name, int Width, int Height, IReadOnlyList<PatchPlacement> Patches);
