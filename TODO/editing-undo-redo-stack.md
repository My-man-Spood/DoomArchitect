# Undo/redo command stack

**Status:** Done  
**Area:** 2D editing

- [x] `Core.Undo`: command-based undo/redo stack (pure Core, no Godot).
      Deliberately NOT a port of UDB's actual `UndoManager` - that's a
      1400-line byte-level binary diff/snapshot system tightly coupled to
      its own `MapElement` serialization format, a background thread that
      compresses old snapshots, and WinForms-era plugin/ticket plumbing.
      None of that carries a correctness risk the way the map-format
      algorithms do (a command stack and a binary-diff stack produce
      identical user-facing undo/redo), so it isn't a case for porting
      literally - a plain `ICommand`/`UndoStack` two-stack design fits
      this codebase's much smaller data model instead. What *did* carry
      over, because it's genuinely the same problem: UDB's exact
      Ctrl+Z/Ctrl+Y keys (checked against its default keybind config, not
      guessed), its 2000-level history cap (`UndoStack.MaxHistory`, same
      number on a different mechanism), and its grouping behavior - one
      undo step per user gesture rather than one per intermediate write,
      via `CommandGroup` (undoes members in reverse order). Vertex/
      linedef/sector drags in `MapOverlay` now record a command only at
      mouse-release (comparing the drag's start snapshot to the final
      position), not per mouse-motion frame, and skip recording entirely
      if nothing actually moved
