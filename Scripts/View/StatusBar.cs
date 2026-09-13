using Godot;

/// <summary>
/// Bottom-of-screen status text - previously mirrored <see cref="MapOverlay"/>'s
/// live editing state (mode, grid size, snap), but every one of those is
/// now also shown on a toolbar button (<c>ModeToolbar</c>/<c>GridToolbar</c>),
/// making the old text redundant (and stale - it referenced 1/2/3 for mode
/// switching, when the real keys have been V/L/S/T for a while). Repurposed
/// to show <see cref="TextureIconCache"/>'s warm-up progress instead, since
/// that's a real, transient thing worth surfacing and nothing else does -
/// blank once every icon is decoded, so this stays quiet the rest of the time.
/// </summary>
public partial class StatusBar : Label
{
	public MapOverlay Overlay { get; set; }

	public override void _Process(double delta)
	{
		var cache = Overlay?.TextureIconCache;
		if (cache == null || cache.TotalCount == 0 || cache.PendingCount == 0)
		{
			Text = "";
			return;
		}

		var done = cache.TotalCount - cache.PendingCount;
		Text = $"Caching textures... {done}/{cache.TotalCount}";
	}
}
