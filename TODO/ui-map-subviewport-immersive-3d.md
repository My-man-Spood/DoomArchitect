# Map view SubViewport fix + immersive full-view 3D mode

**Status:** Done
**Area:** UI / App shell

Follow-on from the resource browser panel landing: the browser and the
map view were stacking instead of sitting side by side. Root cause,
confirmed by reading the code rather than guessed: `MapDocument.tscn`'s
`TopDownCamera`/`PerspectiveCamera` (`Camera3D` nodes) always render
into the main window's own root viewport, which always fills the whole
window - `Control.OffsetLeft` has no effect on a `Camera3D` at all,
since it isn't a `Control` and there's no "render into part of the
screen" without an actual `Viewport` boundary. The resource browser and
the map were drawing into the exact same full-screen space; the browser
just layered on top.

While fixing this, a related UX question came up mid-review: should
switching into 3D mode (`Tab`) optionally take over the whole app
window - hiding the resource browser, tab strip, and menu bar entirely
- rather than staying docked beside the browser? Folded into this same
pass as a persisted, opt-in preference (off by default).

## What was built

- **Wrapped the Map tab's content in a `SubViewportContainer`/
  `SubViewport`**, built directly in code in `AppShell.AddMapTab()`
  (`Stretch = true`), with `mapDocument` reparented into the
  `SubViewport` instead of being added directly to `_contentArea`.
  `_mapViewportContainer` is a direct child of `AppShell` itself (a
  sibling of `_resourceBrowserPanel`/`_contentArea`), not nested inside
  `_contentArea`, so it can freely ignore `_contentArea`'s own bounds
  in immersive mode. Two research passes across every file touching
  viewport/mouse-coordinate math (`MapView`, `FreeFlyCamera`,
  `MapOverlay`, `MapOverlayCamera`, `Crosshair`, `DrawOverlayHandler`,
  `MarqueeSelector`, `ElementOverlayHandler`) confirmed this was low
  risk: every one of them is already viewport-*relative*
  (`GetViewport()`, `InputEvent.Position`, `Camera3D.ProjectRayOrigin`/
  `UnprojectPosition`, `Control.GetViewportRect()`), and a `CanvasLayer`
  always renders into its nearest ancestor `Viewport` - since the 3D
  cameras and the `Overlay`/`UI` `CanvasLayer`s are all children of the
  same root `Node3D`, wrapping that whole scene as one atomic unit
  keeps every one of these call sites correct with zero internal
  rewiring.
- `MapView.SetTabActive(bool)` keeps doing exactly what it already did
  (camera `Current`, `Input.MouseMode`, `ProcessMode`, hiding the
  subtree first so an inactive tab's cameras drop out of the
  viewport's "no current camera" fallback) - `AppShell.Activate`/
  `Deactivate` (now instance methods, not static, since they need
  `_mapViewportContainer`) additionally toggle the *outer*
  container's own `Visible` so an inactive Map tab doesn't leave a
  frozen last-rendered frame sitting in its layout space.
- **New `MapView.In3DChanged` event** (`Action<bool>`), raised
  alongside the existing `toggle_2d_3d` handler's own `_in3D` flip -
  the signal `AppShell` needs to re-run its own layout.
- **New persisted setting**: `AppSettings.GetImmersive3DView()` /
  `WithImmersive3DView(bool)`, following the same immutable
  `CfgBlock`-backed pattern every other setting already uses. New
  checkable "Immersive 3D View" item in `MainMenuBar`'s Preferences
  menu, persisting immediately on toggle (unlike the existing
  "Selection Box" item, which is deliberately session-only) via a new
  `Immersive3DViewToggled` callback property, the same settable-
  callback shape `OpenScriptRequested` already used.
- **`AppShell.UpdateMapViewportLayout()`** (new) - the one place that
  decides where `_mapViewportContainer` sits, called on initial setup,
  on `MapView.In3DChanged`, on the "Immersive 3D View" preference
  toggling, and on the resource browser's own collapse state changing.
  If currently in 3D *and* the preference is on: anchor the container
  to the full window and hide the menu bar, tab strip, and resource
  browser (remembering the browser's own prior `Visible` state first,
  so leaving immersive mode restores what the user actually had rather
  than popping a manually-collapsed browser back open). Otherwise:
  dock beside the browser and below the tab strip/menu bar, same as
  the base fix's normal layout, restoring the remembered chrome. Only
  ever touches the container's left/top offsets - its right/bottom
  anchors are fixed at 1 once, so window resizes alone need no
  recomputation.

## Verification

- `dotnet build DoomArchitect.sln` (including the Godot C# project
  itself) and `dotnet test src/DoomArchitect.Core.Tests/DoomArchitect.Core.Tests.csproj`
  (1035 tests, including 3 new direct round-trip tests for
  `GetImmersive3DView`/`WithImmersive3DView`) both pass clean.
- This is a real architectural change to the rendering/input path
  underneath the whole map editor - it needs a thorough **manual**
  pass in the running app, not just "it compiles": 2D vertex/linedef/
  sector/thing picking and dragging, marquee select, draw mode, the
  2D/3D toggle (specifically `PlacePerspectiveCameraAtMouse`'s mouse-
  to-ground-plane cast), the 3D fly camera (mouse-look + WASD,
  Captured/Visible mouse-mode switch), texture nudge/copy/paste/auto-
  align, Test Map, switching to/from a Script tab and back (confirming
  the Map tab's own container re-shows without a stale frame),
  toggling Immersive 3D View on/off from Preferences, entering/leaving
  3D with it on (chrome hides/restores correctly, including a
  manually-collapsed browser staying collapsed afterward), and
  confirming the setting survives an app restart. Not yet performed
  in-session - this project's own established convention for Godot-
  layer UI has no automated coverage for layout/visibility behavior.
