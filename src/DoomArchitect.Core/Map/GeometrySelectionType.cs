namespace DoomArchitect.Core.Map;

/// <summary>
/// A target for <see cref="MapData.ConvertGeometrySelection"/> - the three
/// geometry element types a 2D edit mode can operate on. Deliberately
/// excludes Things (untouched by that conversion, matching UDB's real
/// behavior where Thing selection is independent of it) and lives here
/// rather than reusing the App-layer <c>EditMode</c> enum, which also has
/// a Things case and isn't available to Core.
/// </summary>
public enum GeometrySelectionType
{
    Vertices,
    Linedefs,
    Sectors,
}
