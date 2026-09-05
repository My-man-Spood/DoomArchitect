/// <summary>
/// Which map element the 2D view is editing - matches UDB's own mode
/// split (Vertices / Linedefs / Sectors / Things), minus Things since
/// there's no Things data model yet.
/// </summary>
public enum EditMode
{
	Vertices,
	Linedefs,
	Sectors,
}
