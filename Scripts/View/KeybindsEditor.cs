using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Input;
using DoomArchitect.Input;
using Godot;

/// <summary>
/// UDB's own real Controls preferences UX, ported directly: a category-
/// grouped list, select an action to see its description and current
/// binding, capture a replacement via a dedicated "listening" state, and a
/// <b>non-blocking</b> conflict warning (lists any other action already
/// using the same combo, never refuses the rebind - see
/// <c>PreferencesForm.UpdateKeyUsedActions</c>'s own real behavior).
/// Works entirely against an in-memory working copy
/// (<see cref="Load"/>/<see cref="GetOverrides"/>) - <see cref="PreferencesDialog"/>
/// owns actually saving it, matching its own existing load-fresh/working-
/// copy/save-on-Confirm shape for the game-configurations tab already
/// next to this one.
/// </summary>
public partial class KeybindsEditor : HSplitContainer
{
    private Tree _tree;
    private Label _titleLabel;
    private Label _descriptionLabel;
    private Label _currentBindingLabel;
    private Button _rebindButton;
    private Button _resetButton;
    private Label _conflictLabel;

    private Dictionary<string, KeyBinding> _workingOverrides = new();
    private string _selectedAction;
    private bool _listening;

    public override void _Ready()
    {
        _tree = GetNode<Tree>("ActionTree");
        _titleLabel = GetNode<Label>("DetailsPanel/TitleLabel");
        _descriptionLabel = GetNode<Label>("DetailsPanel/DescriptionLabel");
        _currentBindingLabel = GetNode<Label>("DetailsPanel/CurrentBindingLabel");
        _rebindButton = GetNode<Button>("DetailsPanel/RebindButton");
        _resetButton = GetNode<Button>("DetailsPanel/ResetButton");
        _conflictLabel = GetNode<Label>("DetailsPanel/ConflictLabel");

        _tree.Columns = 2;
        _tree.HideRoot = true;
        _tree.SetColumnTitle(0, "Action");
        _tree.SetColumnTitle(1, "Binding");
        _tree.ColumnTitlesVisible = true;
        _tree.ItemSelected += OnTreeItemSelected;

        _rebindButton.Pressed += OnRebindPressed;
        _resetButton.Pressed += OnResetPressed;

        ClearDetails();
    }

    /// <summary>Resets the working copy to <paramref name="overrides"/> (a fresh load from disk) and rebuilds the tree - call every time the dialog opens, same reasoning as <see cref="PreferencesDialog.Open"/>'s own "load fresh every time" comment.</summary>
    public void Load(IReadOnlyDictionary<string, KeyBinding> overrides)
    {
        _workingOverrides = new Dictionary<string, KeyBinding>(overrides);
        _selectedAction = null;
        _listening = false;
        RebuildTree();
        ClearDetails();
    }

    public IReadOnlyDictionary<string, KeyBinding> GetOverrides() => _workingOverrides;

    private void RebuildTree()
    {
        _tree.Clear();
        var root = _tree.CreateItem();

        TreeItem categoryItem = null;
        string lastCategory = null;

        foreach (var definition in KeyBindingRegistry.All.OrderBy(d => d.Category, StringComparer.Ordinal).ThenBy(d => d.Title, StringComparer.Ordinal))
        {
            if (definition.Category != lastCategory)
            {
                categoryItem = _tree.CreateItem(root);
                categoryItem.SetText(0, definition.Category);
                categoryItem.SetSelectable(0, false);
                categoryItem.SetSelectable(1, false);
                lastCategory = definition.Category;
            }

            var item = _tree.CreateItem(categoryItem);
            item.SetText(0, definition.Title);
            item.SetText(1, Describe(EffectiveBinding(definition.Name)));
            item.SetMetadata(0, definition.Name);
        }
    }

    private KeyBinding EffectiveBinding(string action) =>
        _workingOverrides.TryGetValue(action, out var binding) ? binding : KeyBindingRegistry.All.First(d => d.Name == action).Default;

    private static string Describe(KeyBinding binding)
    {
        var parts = new List<string>();
        if (binding.Ctrl) parts.Add("Ctrl");
        if (binding.Shift) parts.Add("Shift");
        if (binding.Alt) parts.Add("Alt");
        parts.Add(binding.KeyName);
        return string.Join("+", parts);
    }

    private void OnTreeItemSelected()
    {
        _listening = false;

        var item = _tree.GetSelected();
        var actionName = item?.GetMetadata(0).AsString();
        if (string.IsNullOrEmpty(actionName))
        {
            ClearDetails();
            return;
        }

        _selectedAction = actionName;
        var definition = KeyBindingRegistry.All.First(d => d.Name == actionName);

        _titleLabel.Text = definition.Title;
        _descriptionLabel.Text = definition.Description;
        _currentBindingLabel.Text = $"Current binding: {Describe(EffectiveBinding(actionName))}";
        _rebindButton.Text = "Press a New Key…";
        _rebindButton.Disabled = false;
        _resetButton.Disabled = !_workingOverrides.ContainsKey(actionName);
        _conflictLabel.Text = "";
    }

    private void ClearDetails()
    {
        _selectedAction = null;
        _titleLabel.Text = "";
        _descriptionLabel.Text = "Select an action on the left to rebind it.";
        _currentBindingLabel.Text = "";
        _rebindButton.Text = "Press a New Key…";
        _rebindButton.Disabled = true;
        _resetButton.Disabled = true;
        _conflictLabel.Text = "";
    }

    private void OnRebindPressed()
    {
        _listening = true;
        _rebindButton.Text = "Press Any Key…";
        _conflictLabel.Text = "";
    }

    private void OnResetPressed()
    {
        if (_selectedAction == null) return;

        _workingOverrides.Remove(_selectedAction);
        RebuildTree();
        OnTreeItemSelected();
    }

    /// <summary>
    /// The actual key-capture - deliberately <c>_UnhandledKeyInput</c>
    /// rather than reading a raw <c>KeyDown</c> off a focused control
    /// (UDB's own real WinForms equivalent): lets a genuinely unrelated
    /// GUI shortcut/focused-field keystroke reach its own handler first,
    /// same reasoning Godot's own docs give for preferring "unhandled"
    /// input for shortcut-style capture over intercepting every raw event.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!_listening || _selectedAction == null) return;
        if (@event is not InputEventKey { Pressed: true } keyEvent) return;
        if (keyEvent.Keycode == Key.None) return;

        _listening = false;
        var binding = KeyBindings.FromInputEventKey(keyEvent);
        _workingOverrides[_selectedAction] = binding;

        _rebindButton.Text = "Press a New Key…";
        _currentBindingLabel.Text = $"Current binding: {Describe(binding)}";
        _resetButton.Disabled = false;

        var conflicts = KeyBindingRegistry.All
            .Where(d => d.Name != _selectedAction && EffectiveBinding(d.Name) == binding)
            .Select(d => d.Title)
            .ToList();
        _conflictLabel.Text = conflicts.Count == 0
            ? ""
            : $"Also bound to: {string.Join(", ", conflicts)}";

        RebuildTree();
        GetViewport().SetInputAsHandled();
    }
}
