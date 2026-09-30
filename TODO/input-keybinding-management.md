# Keybinding management

**Status:** Done  
**Area:** Input

- [x] Keybinding management, done 2026-09-20 - built on Godot's own real
      `InputMap` (the user's own explicit suggestion), researched directly
      against UDB's real `Actions.cfg`/`ActionManager`/`PreferencesForm`
      rebind-UI source and Godot 4.7's actual `InputMap` API (verified via
      IL disassembly + docs, not guessed) via a full Plan Mode cycle before
      implementation.

      New Core: `Core.Input.KeyBinding` (a Godot-free `KeyName`+Shift/Ctrl/
      Alt struct - keeps Core free of a `Godot.Key` dependency the same way
      `Core.Geometry` stays free of Godot's own `Vector2`), `KeyBindingDefinition`
      (UDB's own real per-action `Actions.cfg` block - name/category/title/
      description/default/repeat - minus the fields this project has no use
      for yet, `allowmouse`/`allowscroll`/`disregard*`), `KeyBindingRegistry`
      (36 actions, one static curated list rather than a parsed file - this
      project has no plugin-assembly architecture motivating UDB's own
      file-per-assembly split). `Core.Configuration.AppSettings` gained
      `GetKeyBindingOverrides`/`WithKeyBindingOverride`/`WithKeyBindingReset`,
      mirroring `GetDefaultResources`'s own exact pattern (a `keybinds { }`
      block, one child per *overridden* action only) - reuses the existing
      `user://settings.cfg` persistence (`AppSettingsFile`) rather than
      inventing a new file/format.

      New App: `Scripts/Input/KeyBindings.cs` - the one place `Godot.Key`
      and `Core.Input.KeyBinding` meet. `Bootstrap()` (called from
      `MapView._Ready`, before any input can occur) registers every
      action's default binding with `InputMap.AddAction`/`ActionAddEvent`,
      then overlays any saved override - this project has no `[input]`
      section in `project.godot` at all (confirmed directly), so every
      action is defined entirely at runtime, matching Godot's own real,
      fully-supported "ideal for reassigning or creating different actions
      at runtime" use of `InputMap`. `ApplyBinding`/`FindConflicts` back the
      rebind UI.

      Every inventoried keybind site migrated from a raw `Key.X`/
      `Input.IsKeyPressed` check to `@event.IsActionPressed("name")`/
      `Input.IsActionPressed("name")`: `MapView.cs`'s whole global dispatch
      (undo/redo, mode switches V/L/S/T/W, 2D/3D toggle, grid controls,
      texture nudge/auto-align), `FreeFlyCamera`'s WASD+Space+Shift fly
      controls, `DrawOverlayHandler`'s Escape/Backspace/cardinal-lock,
      `MapOverlay`'s snap-invert/pan-view, `MarqueeSelector`'s add/subtract
      modifiers, `StepperLineEdit`'s step-size modifiers - a real,
      non-trivial restructure of `MapView._UnhandledInput`'s own dispatch
      shape (pattern-matching a `Keycode` inside a `case` clause can't
      express an action check, which is a method call - became a sequence
      of `if` checks instead), not a one-line swap per site. Two real bugs
      caught and fixed mid-migration, before they ever shipped: the grid-
      size bracket keys' own default bindings were initially assigned
      backwards relative to which method they actually call (`[`/`]` do
      the *opposite* of the intuitive halve/double mapping in this
      project's own real, pre-existing behavior - preserved exactly, not
      "corrected"), and the texture-nudge action checks needed explicit
      `allowEcho: true` (`IsActionPressed`'s own real default rejects OS
      key-repeat, unlike the code this replaced).

      Every modifier role - "is Shift held to invert grid snap", "is Ctrl
      held to nudge by the grid size" - is its own independently rebindable
      action, not a hardcoded literal shared across features, per the
      user's own explicit request ("what if i want ctrl to increase
      texture nudge but not ctrl for some other thing"). A modifier key is
      itself a valid `InputMap` base keycode, so this needed no special
      mechanism - `draw_cardinal_lock_modifier` (Alt+Shift held together)
      binds with the modifier itself as the base `Keycode` and the other
      modifier as a qualifier on that same event; whether this exact shape
      matches correctly via `Input.IsActionPressed`'s own live polling
      wasn't independently verified against a running Godot instance
      (`dotnet build` only compiles C#, it can't validate `InputMap`
      runtime matching semantics) - flagged for the user's own live check,
      same as every other Godot-side behavior this session couldn't verify
      any other way.

      Rebind UI: `PreferencesDialog` generalized from a single "Game
      Configurations" page into a `TabContainer` (existing tab unchanged)
      plus a new "Keybinds" tab (`KeybindsEditor.cs`/`.tscn`) - UDB's own
      real Controls preferences UX ported directly: a category-grouped
      `Tree`, select an action to see its description/current binding, a
      dedicated "Press a New Key…" capture button
      (`_UnhandledKeyInput`-based, not a raw focused-field `KeyDown` hook -
      lets an unrelated GUI shortcut reach its own handler first, matching
      Godot's own docs' reasoning for preferring unhandled input for
      shortcut-style capture), a per-action "Reset to Default", and a
      **non-blocking** conflict warning (lists any other action already
      using the same combo, never refuses the rebind - UDB's own real
      `PreferencesForm.UpdateKeyUsedActions` behavior exactly). Confirming
      on the dialog saves to `user://settings.cfg` and immediately re-
      applies every binding live via `KeyBindings.Bootstrap()` - no
      restart needed.

      Scoped out, flagged rather than silently dropped (see TODO.md's own
      "Later/someday" list if this needs its own entry later): mouse-
      button/scroll bindings (3D select/edit clicks, wheel raise/lower,
      Draw mode's right-click finish) stay direct, unrebindable
      `InputEventMouseButton` checks - full mouse rebinding touches every
      element handler's own click semantics and is a separably-sized
      feature of its own. `FreeFlyCamera`'s Escape-releases-mouse-capture
      and `MapOverlay`'s original pan-view discovery mid-migration (not in
      the initial plan, added once found) both got their own real
      treatment rather than being silently skipped - the former
      deliberately stays a hardcoded literal (a universal "get my cursor
      back" safety hatch, not meant to be rebindable away), the latter
      became a real action (`pan_view_modifier`, UDB's own real action
      name).
