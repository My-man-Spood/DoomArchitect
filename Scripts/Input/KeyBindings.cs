using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Input;
using DoomArchitect.Settings;
using Godot;

namespace DoomArchitect.Input;

/// <summary>
/// Bridges <see cref="KeyBindingRegistry"/>'s own Godot-free data to a real,
/// live Godot <see cref="InputMap"/> - the only place <c>Godot.Key</c> and
/// <c>Core.Input.KeyBinding</c> meet. This project has no <c>[input]</c>
/// section in <c>project.godot</c> at all (confirmed directly, not
/// assumed) - every action here is defined entirely at runtime, matching
/// Godot's own real, fully-supported "ideal for reassigning or creating
/// different actions at runtime" use of <see cref="InputMap"/> (its own
/// docs' wording), rather than needing a project-file counterpart to fall
/// back to.
/// </summary>
public static class KeyBindings
{
    /// <summary>
    /// Registers every action from <see cref="KeyBindingRegistry"/> with
    /// its own compiled-in default binding, then overlays whatever the
    /// user has actually saved (<see cref="AppSettingsFile"/>) on top -
    /// call once, before any input can occur (<c>MapView._Ready</c>).
    /// Safe to call more than once (e.g. after the Preferences dialog
    /// saves a change and wants every action freshly re-applied) -
    /// <see cref="InputMap.HasAction"/> guards the registration step, and
    /// re-applying a binding is just erase-then-add either way.
    /// </summary>
    public static void Bootstrap()
    {
        var overrides = AppSettingsFile.Load().GetKeyBindingOverrides();

        foreach (var definition in KeyBindingRegistry.All)
        {
            if (!InputMap.HasAction(definition.Name)) InputMap.AddAction(definition.Name);

            var binding = overrides.TryGetValue(definition.Name, out var overridden) ? overridden : definition.Default;
            ApplyBinding(definition.Name, binding);
        }
    }

    /// <summary>Live re-bind - erases whatever this action was previously bound to and binds it to <paramref name="binding"/> instead. Takes effect immediately, no restart needed.</summary>
    public static void ApplyBinding(string action, KeyBinding binding)
    {
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, ToInputEventKey(binding));
    }

    /// <summary>The binding an action is *currently* live-bound to (not necessarily its registry default - may be a saved override already applied by <see cref="Bootstrap"/>, or a not-yet-saved in-progress rebind).</summary>
    public static KeyBinding GetCurrentBinding(string action)
    {
        var events = InputMap.ActionGetEvents(action);
        foreach (var @event in events)
        {
            if (@event is InputEventKey keyEvent) return FromInputEventKey(keyEvent);
        }

        return new KeyBinding("None");
    }

    /// <summary>
    /// UDB's own real non-blocking rebind-conflict check: every *other*
    /// action currently bound to the exact same key+modifiers as
    /// <paramref name="binding"/> - the caller decides what to do with the
    /// list (UDB's own real Controls preferences just displays it as a
    /// warning, never refuses the rebind).
    /// </summary>
    public static IReadOnlyList<string> FindConflicts(KeyBinding binding, string excludingAction)
    {
        var conflicts = new List<string>();

        foreach (var definition in KeyBindingRegistry.All)
        {
            if (definition.Name == excludingAction) continue;
            if (GetCurrentBinding(definition.Name) == binding) conflicts.Add(definition.Name);
        }

        return conflicts;
    }

    public static InputEventKey ToInputEventKey(KeyBinding binding) => new()
    {
        Keycode = Enum.Parse<Key>(binding.KeyName),
        ShiftPressed = binding.Shift,
        CtrlPressed = binding.Ctrl,
        AltPressed = binding.Alt,
    };

    public static KeyBinding FromInputEventKey(InputEventKey keyEvent) =>
        new(keyEvent.Keycode.ToString(), keyEvent.ShiftPressed, keyEvent.CtrlPressed, keyEvent.AltPressed);
}
