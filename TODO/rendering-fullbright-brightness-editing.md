# Full-bright toggle + real brightness editing

**Status:** Pending  
**Area:** Rendering

- [ ] Full-bright toggle + real sector/wall brightness editing (Ctrl+Scroll,
      matching UDB's own `togglebrightness`/`raisebrightness8`/
      `lowerbrightness8` default keybinds) - the feature that originally
      motivated building 3D targeting above; now buildable as a
      straightforward consumer of `IMapTargetFinder` plus a
      `ChangeSectorBrightnessCommand` through the existing undo stack.
