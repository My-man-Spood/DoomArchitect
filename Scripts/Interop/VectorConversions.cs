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

    /// <summary>
    /// Doom's map-plane X/Y become Godot's ground-plane X/-Z, height
    /// becomes Godot's Y (up) - the one coordinate mapping used everywhere
    /// Core geometry turns into a Godot-space position, so every consumer
    /// (mesh building, overlay gizmos, ...) agrees on it.
    ///
    /// The Y negation is load-bearing, not cosmetic: the top-down camera
    /// looks straight down (-90 degrees about X), which makes screen-up
    /// correspond to world -Z. With a direct (un-negated) Y-to-Z mapping,
    /// increasing Doom Y - which is "north"/up on every real Doom
    /// automap/editor - would move toward the *bottom* of the screen
    /// instead, a north-south mirror. No camera rotation can fix this: a
    /// pure rotation always preserves handedness, so it can only choose
    /// which world axis lands on screen-up, never flip a single axis
    /// independently - the mapping itself has to carry the flip. (An
    /// earlier version of this mapping had no negation here; it went
    /// unnoticed because it was only ever checked against a fully
    /// symmetric sample room, which can't reveal a mirror by inspection.)
    /// </summary>
    public static Godot.Vector3 ToWorld(this System.Numerics.Vector2 doomPosition, float height) =>
        new(doomPosition.X, height, -doomPosition.Y);

    /// <summary>Inverse of <see cref="ToWorld"/> - drops the height component.</summary>
    public static System.Numerics.Vector2 ToDoom(this Godot.Vector3 worldPosition) =>
        new(worldPosition.X, -worldPosition.Z);
}
