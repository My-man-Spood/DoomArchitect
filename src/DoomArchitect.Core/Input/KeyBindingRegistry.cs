namespace DoomArchitect.Core.Input;

/// <summary>
/// Every rebindable action in the app - the closest equivalent this
/// project has to UDB's own real <c>Actions.cfg</c> (a flat namespace of
/// named actions with a compiled-in default binding), expressed as plain
/// C# data instead of a parsed file: this project has no plugin-assembly
/// architecture motivating UDB's own file-per-assembly split, so a single
/// static list is the simpler faithful equivalent for the same content.
///
/// A physical key legitimately defaults the same for two actions that are
/// never simultaneously active (<c>mode_draw</c> and <c>camera_forward</c>
/// both default to <c>W</c> - one only ever matters in 2D, the other only
/// while the 3D fly camera is current) - UDB itself has the identical real
/// precedent (<c>classicselect</c>/<c>visualselect</c> both default to
/// left-click).
///
/// Every modifier role - "is Shift held to invert grid snap", "is Ctrl
/// held to nudge by the grid size" - is its own action here, not a
/// hardcoded literal shared across features, even though several default
/// to the same physical key: rebinding one must never affect another,
/// per the user's own explicit request.
/// </summary>
public static class KeyBindingRegistry
{
    private const string CategoryEdit = "Edit";
    private const string CategoryModes = "Modes";
    private const string CategoryView = "View";
    private const string CategoryGrid = "Grid";
    private const string CategoryDraw = "Draw";
    private const string CategoryTexture = "3D Texture";
    private const string CategoryMarquee = "Marquee Selection";
    private const string CategoryNumericFields = "Numeric Fields";
    private const string CategoryCamera = "Camera";

    public static IReadOnlyList<KeyBindingDefinition> All { get; } = new[]
    {
        new KeyBindingDefinition("undo", CategoryEdit, "Undo", "Undoes the last change.", new KeyBinding("Z", Ctrl: true)),
        new KeyBindingDefinition("redo", CategoryEdit, "Redo", "Redoes the last undone change.", new KeyBinding("Y", Ctrl: true)),

        new KeyBindingDefinition("mode_vertices", CategoryModes, "Vertices Mode", "Switches to Vertices editing mode.", new KeyBinding("V")),
        new KeyBindingDefinition("mode_linedefs", CategoryModes, "Linedefs Mode", "Switches to Linedefs editing mode.", new KeyBinding("L")),
        new KeyBindingDefinition("mode_sectors", CategoryModes, "Sectors Mode", "Switches to Sectors editing mode.", new KeyBinding("S")),
        new KeyBindingDefinition("mode_things", CategoryModes, "Things Mode", "Switches to Things editing mode.", new KeyBinding("T")),
        new KeyBindingDefinition("mode_draw", CategoryModes, "Draw Mode", "Switches to Draw Lines mode.", new KeyBinding("W")),
        new KeyBindingDefinition("toggle_2d_3d", CategoryModes, "Toggle 2D/3D View", "Switches between the 2D top-down view and the 3D visual view.", new KeyBinding("Tab")),

        new KeyBindingDefinition("pan_view_modifier", CategoryView, "Pan View", "While held, moving the mouse pans the 2D view instead of interacting with the map - UDB's own real pan_view action.", new KeyBinding("Space")),
        new KeyBindingDefinition("toggle_tag_indicators", CategoryView, "Toggle Tag Indicators", "Toggles sector tag labels and hover-triggered tag arrows - UDB's own real gztoggleeventlines/ViewSelectionEffects, combined into one toggle here.", new KeyBinding("I")),

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
        new KeyBindingDefinition("texture_auto_align", CategoryTexture, "Auto-Align Texture", "Aligns the targeted wall's texture with its same-textured neighbors.", new KeyBinding("A")),
        new KeyBindingDefinition("texture_auto_align_axis_swap_modifier", CategoryTexture, "Auto-Align Vertically Instead", "While held, auto-align affects the texture's vertical offset instead of horizontal.", new KeyBinding("Shift")),
        new KeyBindingDefinition("texture_auto_align_both_modifier", CategoryTexture, "Auto-Align Both Axes", "While held, auto-align affects both the texture's horizontal and vertical offset.", new KeyBinding("Ctrl")),

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
    };
}
