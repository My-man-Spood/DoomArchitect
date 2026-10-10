namespace DoomArchitect.Core.Input;

/// <summary>
/// Every rebindable action in the app - a flat namespace of named actions
/// with a compiled-in default binding, expressed as plain C# data instead
/// of a parsed file.
///
/// A physical key legitimately defaults the same for two actions that are
/// never simultaneously active (<c>mode_draw</c> and <c>camera_forward</c>
/// both default to <c>W</c> - one only ever matters in 2D, the other only
/// while the 3D fly camera is current).
///
/// Every modifier role - "is Shift held to invert grid snap", "is Ctrl
/// held to nudge by the grid size" - is its own action here, not a
/// hardcoded literal shared across features, even though several default
/// to the same physical key: rebinding one must never affect another,
/// per the user's own explicit request.
/// </summary>
public static class KeyBindingRegistry
{
    private const string CategoryTestMap = "Test Map";
    private const string CategoryEdit = "Edit";
    private const string CategoryModes = "Modes";
    private const string CategoryView = "View";
    private const string CategoryGrid = "Grid";
    private const string CategoryDraw = "Draw";
    private const string CategoryTexture = "3D Texture";
    private const string CategoryMarquee = "Marquee Selection";
    private const string CategoryNumericFields = "Numeric Fields";
    private const string CategoryCamera = "Camera";
    private const string CategoryPanels = "Panels";

    public static IReadOnlyList<KeyBindingDefinition> All { get; } = new[]
    {
        new KeyBindingDefinition("test_map", CategoryTestMap, "Test Map", "Launches the current map in the active test engine, at the last skill/monsters setting used from the Map menu.", new KeyBinding("F9")),

        new KeyBindingDefinition("undo", CategoryEdit, "Undo", "Undoes the last change.", new KeyBinding("Z", Ctrl: true)),
        new KeyBindingDefinition("redo", CategoryEdit, "Redo", "Redoes the last undone change.", new KeyBinding("Y", Ctrl: true)),
        new KeyBindingDefinition("delete_item", CategoryEdit, "Delete", "Deletes the current selection (or the hovered element, if nothing is selected) in Vertices/Linedefs/Sectors/Things mode.", new KeyBinding("Delete")),
        new KeyBindingDefinition("dissolve_item", CategoryEdit, "Dissolve", "Deletes the current selection (or the hovered element, if nothing is selected) in Vertices/Linedefs mode, trying to preserve the rest of the map geometry intact. Sectors mode has no separate Dissolve in UDB either - it's the same as Delete there.", new KeyBinding("Backspace")),
        new KeyBindingDefinition("flip_linedef", CategoryEdit, "Flip Linedef", "In Linedefs mode, reverses the current selection's (or hovered line's) direction - Start/End swapped together with Front/Back, so each side keeps facing the same sector. A pure one-sided line is left untouched.", new KeyBinding("F")),
        new KeyBindingDefinition("deselect_all", CategoryEdit, "Deselect All", "Clears the current selection - every selected vertex/linedef/sector/thing in 2D, every selected floor/ceiling/wall/thing in 3D.", new KeyBinding("C")),
        new KeyBindingDefinition("save_map", CategoryEdit, "Save Map", "Saves the active map tab to its WAD file - the same action as File > Save Map, which had no keyboard shortcut at all until this was added.", new KeyBinding("S", Ctrl: true)),
        new KeyBindingDefinition("save_document", CategoryEdit, "Save Document", "Saves the active script tab's file to disk.", new KeyBinding("S", Ctrl: true)),

        new KeyBindingDefinition("mode_vertices", CategoryModes, "Vertices Mode", "Switches to Vertices editing mode.", new KeyBinding("V")),
        new KeyBindingDefinition("mode_linedefs", CategoryModes, "Linedefs Mode", "Switches to Linedefs editing mode.", new KeyBinding("L")),
        new KeyBindingDefinition("mode_sectors", CategoryModes, "Sectors Mode", "Switches to Sectors editing mode.", new KeyBinding("S")),
        new KeyBindingDefinition("mode_things", CategoryModes, "Things Mode", "Switches to Things editing mode.", new KeyBinding("T")),
        new KeyBindingDefinition("mode_draw", CategoryModes, "Draw Mode", "Switches to Draw Lines mode.", new KeyBinding("W")),
        new KeyBindingDefinition("toggle_2d_3d", CategoryModes, "Toggle 2D/3D View", "Switches between the 2D top-down view and the 3D visual view.", new KeyBinding("Tab")),

        new KeyBindingDefinition("pan_view_modifier", CategoryView, "Pan View", "While held, moving the mouse pans the 2D view instead of interacting with the map.", new KeyBinding("Space")),
        new KeyBindingDefinition("toggle_tag_indicators", CategoryView, "Toggle Tag Indicators", "Toggles sector tag labels and hover-triggered tag arrows.", new KeyBinding("I")),

        new KeyBindingDefinition("toggle_snap", CategoryGrid, "Toggle Grid Snap", "Toggles whether new/moved geometry snaps to the grid.", new KeyBinding("G")),
        new KeyBindingDefinition("toggle_dynamic_grid", CategoryGrid, "Toggle Dynamic Grid Size", "Toggles automatically adjusting grid size with zoom level.", new KeyBinding("D")),
        new KeyBindingDefinition("grid_size_decrease", CategoryGrid, "Halve Grid Size", "Halves the current grid size.", new KeyBinding("Bracketright")),
        new KeyBindingDefinition("grid_size_increase", CategoryGrid, "Double Grid Size", "Doubles the current grid size.", new KeyBinding("Bracketleft")),
        new KeyBindingDefinition("grid_snap_invert_modifier", CategoryGrid, "Invert Grid Snap", "While held, temporarily inverts the grid snap toggle.", new KeyBinding("Shift")),

        new KeyBindingDefinition("draw_cancel", CategoryDraw, "Cancel Draw", "Discards the in-progress drawn shape.", new KeyBinding("Escape")),
        new KeyBindingDefinition("draw_remove_last_point", CategoryDraw, "Remove Last Drawn Point", "Removes the most recently placed point while drawing.", new KeyBinding("Backspace")),
        new KeyBindingDefinition("draw_cardinal_lock_modifier", CategoryDraw, "Cardinal Direction Lock", "While held, constrains the next drawn point to 45-degree increments from the last one.", new KeyBinding("Alt", Shift: true)),

        new KeyBindingDefinition("texture_nudge_left", CategoryTexture, "Nudge Texture Left", "Nudges the targeted wall's texture offset left.", new KeyBinding("Left"), AllowEcho: true),
        new KeyBindingDefinition("texture_nudge_right", CategoryTexture, "Nudge Texture Right", "Nudges the targeted wall's texture offset right.", new KeyBinding("Right"), AllowEcho: true),
        new KeyBindingDefinition("texture_nudge_up", CategoryTexture, "Nudge Texture Up", "Nudges the targeted wall's texture offset up.", new KeyBinding("Up"), AllowEcho: true),
        new KeyBindingDefinition("texture_nudge_down", CategoryTexture, "Nudge Texture Down", "Nudges the targeted wall's texture offset down.", new KeyBinding("Down"), AllowEcho: true),
        new KeyBindingDefinition("texture_nudge_amount_x8_modifier", CategoryTexture, "Nudge by 8 Pixels", "While held, nudges the targeted texture by 8 pixels instead of 1.", new KeyBinding("Alt")),
        new KeyBindingDefinition("texture_nudge_amount_grid_modifier", CategoryTexture, "Nudge by Grid Size", "While held, nudges the targeted texture by the current grid size instead of 1 pixel.", new KeyBinding("Ctrl")),
        new KeyBindingDefinition("texture_auto_align", CategoryTexture, "Auto-Align Texture", "Aligns the targeted wall's texture with its same-textured neighbors.", new KeyBinding("E")),
        new KeyBindingDefinition("texture_auto_align_axis_swap_modifier", CategoryTexture, "Auto-Align Vertically Instead", "While held, auto-align affects the texture's vertical offset instead of horizontal.", new KeyBinding("Ctrl")),
        new KeyBindingDefinition("texture_auto_align_both_modifier", CategoryTexture, "Auto-Align Both Axes", "While held, auto-align affects both the texture's horizontal and vertical offset.", new KeyBinding("Q")),
        new KeyBindingDefinition("texture_copy", CategoryTexture, "Copy Texture", "Copies the targeted wall/floor/ceiling's texture. Paste with Ctrl+V or Middle Mouse Button (not rebindable).", new KeyBinding("C", Ctrl: true)),
        new KeyBindingDefinition("paste_selection", CategoryEdit, "Paste", "Pastes the copied texture onto the targeted wall/floor/ceiling.", new KeyBinding("V", Ctrl: true)),
        new KeyBindingDefinition("select_connected_texture_modifier", CategoryTexture, "Select Connected Same Texture", "While held during a 3D select click, also (de)selects every connected wall part/sector sharing the exact same texture, out to where it changes.", new KeyBinding("Ctrl")),
        new KeyBindingDefinition("select_connected_height_modifier", CategoryTexture, "Select Connected Same Height", "While held during a 3D floor/ceiling select click, also (de)selects every connected sector at the exact same height, out to where it changes. Combine with Select Connected Same Texture to require both.", new KeyBinding("Alt")),

        new KeyBindingDefinition("marquee_add_modifier", CategoryMarquee, "Add to Selection", "While held, a marquee selection adds to the current selection instead of replacing it.", new KeyBinding("Shift")),
        new KeyBindingDefinition("marquee_subtract_modifier", CategoryMarquee, "Subtract from Selection", "While held, a marquee selection removes from the current selection instead of replacing it - held together with Add to Selection, it intersects instead.", new KeyBinding("Ctrl")),

        new KeyBindingDefinition("stepper_small_step_modifier", CategoryNumericFields, "Small Step", "While held, a numeric field's up/down buttons nudge by a smaller amount.", new KeyBinding("Ctrl")),
        new KeyBindingDefinition("stepper_big_step_modifier", CategoryNumericFields, "Large Step", "While held, a numeric field's up/down buttons nudge by a larger amount.", new KeyBinding("Shift")),

        new KeyBindingDefinition("camera_forward", CategoryCamera, "Fly Forward", "Moves the 3D camera forward.", new KeyBinding("W")),
        new KeyBindingDefinition("camera_backward", CategoryCamera, "Fly Backward", "Moves the 3D camera backward.", new KeyBinding("S")),
        new KeyBindingDefinition("camera_strafe_left", CategoryCamera, "Strafe Left", "Moves the 3D camera left.", new KeyBinding("A")),
        new KeyBindingDefinition("camera_strafe_right", CategoryCamera, "Strafe Right", "Moves the 3D camera right.", new KeyBinding("D")),
        new KeyBindingDefinition("camera_fly_up", CategoryCamera, "Fly Up", "Moves the 3D camera straight up.", new KeyBinding("Space")),
        new KeyBindingDefinition("camera_fly_down", CategoryCamera, "Fly Down", "Moves the 3D camera straight down.", new KeyBinding("Shift")),

        new KeyBindingDefinition("toggle_resource_browser", CategoryPanels, "Toggle Resource Browser", "Shows or hides the resource browser panel.", new KeyBinding("B", Ctrl: true)),
    };
}
