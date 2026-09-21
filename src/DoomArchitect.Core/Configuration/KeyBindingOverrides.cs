using DoomArchitect.Core.Input;

namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Reads/writes the <c>keybinds {{ }}</c> block of a user's own
/// <see cref="AppSettings"/> - one child block per action whose binding
/// has actually been changed from <see cref="KeyBindingRegistry"/>'s own
/// compiled-in default (an action with no block here just uses that
/// default), matching UDB's own real "the user's own settings file only
/// ever needs to store what differs" shape.
/// </summary>
internal static class KeyBindingOverrides
{
    private const string KeyKey = "key";
    private const string ShiftKey = "shift";
    private const string CtrlKey = "ctrl";
    private const string AltKey = "alt";

    public static IReadOnlyDictionary<string, KeyBinding> Read(CfgBlock? keybindsBlock)
    {
        if (keybindsBlock == null) return new Dictionary<string, KeyBinding>();

        var result = new Dictionary<string, KeyBinding>();
        foreach (var block in keybindsBlock.Blocks)
        {
            var keyValue = block.Find(KeyKey);
            if (keyValue == null) continue;

            result[block.Key] = new KeyBinding(
                keyValue.Value.AsString(),
                Shift: block.Find(ShiftKey)?.AsBool() ?? false,
                Ctrl: block.Find(CtrlKey)?.AsBool() ?? false,
                Alt: block.Find(AltKey)?.AsBool() ?? false);
        }

        return result;
    }

    public static CfgBlock Write(IReadOnlyDictionary<string, KeyBinding> overrides)
    {
        var blocks = overrides
            .Select(entry => WriteOne(entry.Key, entry.Value))
            .ToList();
        return new CfgBlock("keybinds", Array.Empty<CfgAssignment>(), blocks);
    }

    private static CfgBlock WriteOne(string action, KeyBinding binding)
    {
        var assignments = new List<CfgAssignment> { new(KeyKey, CfgValue.OfString(binding.KeyName)) };
        if (binding.Shift) assignments.Add(new CfgAssignment(ShiftKey, CfgValue.OfBool(true)));
        if (binding.Ctrl) assignments.Add(new CfgAssignment(CtrlKey, CfgValue.OfBool(true)));
        if (binding.Alt) assignments.Add(new CfgAssignment(AltKey, CfgValue.OfBool(true)));

        return new CfgBlock(action, assignments, Array.Empty<CfgBlock>());
    }
}
