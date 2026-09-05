using Godot;

/// <summary>
/// Bottom-of-screen status text mirroring <see cref="MapOverlay"/>'s live
/// editing state (mode, grid size, snap). A real UI <see cref="Label"/>
/// on the <c>UI</c> canvas layer rather than drawn into MapOverlay's
/// gizmo layer - the same separation-of-concerns reason as
/// <see cref="ModeToolbar"/>: general UI shouldn't live inside the
/// map-editing surface it's reporting on.
/// </summary>
public partial class StatusBar : Label
{
	public MapOverlay Overlay { get; set; }

	public override void _Process(double delta)
	{
		if (Overlay == null) return;

		var snapState = Overlay.EffectiveSnap ? "on" : "off";
		var dynamicState = Overlay.DynamicGridSizeEnabled ? "on" : "off";
		Text = $"Mode: {Overlay.Mode}  (1 Vertices · 2 Linedefs · 3 Sectors)  " +
			$"Grid: {Overlay.GridSize} ([ larger, ] smaller)  " +
			$"Snap: {snapState} (G to toggle, hold Shift to invert)  " +
			$"Dynamic: {dynamicState} (D to toggle)";
	}
}
