namespace DoomArchitect.Core.Input;

/// <summary>
/// One rebindable action, minus the fields this project has no use for
/// (<c>allowmouse</c>/<c>allowscroll</c> - this pass is keyboard-only, see
/// TODO.md; <c>disregardshift</c>/<c>disregardcontrol</c>/<c>disregardalt</c> -
/// this project checks modifiers via genuinely separate rebindable
/// modifier-role actions instead, so there's no single action whose own
/// key match needs to selectively ignore one).
/// </summary>
/// <param name="Name">
/// Stable identifier - doubles as the real Godot <c>InputMap</c> action
/// name and the key under which a user override is saved, so renaming
/// this is a real (if easy) migration, not just a label change.
/// </param>
/// <param name="Category">Groups actions in the rebind UI.</param>
/// <param name="Title">Short display name.</param>
/// <param name="Description">One sentence, shown when the action is selected in the rebind UI.</param>
/// <param name="Default">The compiled-in binding - what a fresh install, or an explicit "reset to default", uses.</param>
/// <param name="AllowEcho">
/// Whether holding the key down should keep re-firing it (OS key-repeat),
/// rather than only the initial press.
/// </param>
public sealed record KeyBindingDefinition(string Name, string Category, string Title, string Description, KeyBinding Default, bool AllowEcho = false);
