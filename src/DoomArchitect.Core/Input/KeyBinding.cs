namespace DoomArchitect.Core.Input;

/// <summary>
/// One rebindable action's bound key, kept Godot-free the same way
/// <c>Core.Geometry</c> keeps <see cref="System.Numerics.Vector2"/> instead
/// of Godot's own <c>Vector2</c> - only the App layer (which already owns
/// the one real <c>Godot.Key</c> dependency) converts to/from an actual
/// <c>InputEventKey</c>. <see cref="KeyName"/> is Godot's own real
/// <c>Key</c> enum member name as plain text (e.g. <c>"Left"</c>,
/// <c>"Z"</c>, <c>"Bracketleft"</c>) rather than its numeric value - keeps
/// a saved settings file human-readable and hand-editable, matching UDB's
/// own real <c>shortcuts {{ }}</c> file being exactly that (even though
/// UDB's own encoding is a raw bit-packed integer, not a name - this
/// project has no reason to replicate that specific storage choice, only
/// the concept of a user-editable saved override).
///
/// A modifier key (Shift/Ctrl/Alt) is itself a perfectly valid
/// <see cref="KeyName"/> - "is Shift held" is exactly as real a rebindable
/// action as "is Enter pressed", just usually read via a live poll rather
/// than a one-shot press (see <c>KeyBindingRegistry</c>'s own remarks on
/// why every modifier *role* gets its own action rather than a hardcoded
/// literal).
/// </summary>
public readonly record struct KeyBinding(string KeyName, bool Shift = false, bool Ctrl = false, bool Alt = false);
