using Godot;

/// <summary>
/// A small fixed "+" at the center of the 3D viewport. The targeting
/// highlight alone doesn't pin down an exact point on a large flat
/// surface - without something marking where the camera is actually
/// aimed, it's genuinely hard to tell what MapView's targeting system is
/// evaluating.
/// </summary>
public partial class Crosshair : Control
{
    private const float ArmLength = 8f;
    private const float Gap = 3f;
    private const float Thickness = 2f;

    private static readonly Color LineColor = new(1f, 1f, 1f, 0.85f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // Anchor presets resolve against the nearest Control ancestor,
        // which this doesn't have (it's parented under a screen-space
        // overlay layer sitting over the 3D scene, not another Control) -
        // sizing directly from the viewport sidesteps that ambiguity
        // entirely instead of trusting an unverified fallback behavior.
        Position = Vector2.Zero;
        Size = GetViewport().GetVisibleRect().Size;
        GetViewport().SizeChanged += OnViewportSizeChanged;
    }

    private void OnViewportSizeChanged()
    {
        Size = GetViewport().GetVisibleRect().Size;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = Size / 2f;
        DrawLine(center + new Vector2(-ArmLength - Gap, 0), center + new Vector2(-Gap, 0), LineColor, Thickness);
        DrawLine(center + new Vector2(Gap, 0), center + new Vector2(ArmLength + Gap, 0), LineColor, Thickness);
        DrawLine(center + new Vector2(0, -ArmLength - Gap), center + new Vector2(0, -Gap), LineColor, Thickness);
        DrawLine(center + new Vector2(0, Gap), center + new Vector2(0, ArmLength + Gap), LineColor, Thickness);
    }
}
