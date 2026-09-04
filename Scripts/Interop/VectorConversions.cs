namespace DoomArchitect.Interop;

/// <summary>
/// Core stays Godot-free, so it works entirely in <see cref="System.Numerics"/>
/// vector types. Godot has no built-in conversion to or from them (confirmed
/// via reflection - no implicit/explicit operator exists on <c>Godot.Vector2</c>
/// or <c>Godot.Vector3</c>), even though both are just sequential floats
/// under the hood - so every value crossing the Core/App boundary goes
/// through one of these. Fully-qualified names throughout on purpose:
/// <c>System.Numerics.Vector2/3</c> and <c>Godot.Vector2/3</c> share a bare
/// name, so a file with both namespaces in scope can't refer to either
/// unqualified without ambiguity.
/// </summary>
public static class VectorConversions
{
    public static Godot.Vector2 ToGodot(this System.Numerics.Vector2 v) => new(v.X, v.Y);

    public static System.Numerics.Vector2 ToNumerics(this Godot.Vector2 v) => new(v.X, v.Y);

    public static Godot.Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static System.Numerics.Vector3 ToNumerics(this Godot.Vector3 v) => new(v.X, v.Y, v.Z);
}
