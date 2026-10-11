using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Sets (or removes) one field on one element's <see cref="UniFields"/>.
/// The pre-existing value is captured automatically at construction time,
/// so callers never need to separately snapshot it - unlike
/// <see cref="MoveVertexCommand"/>/<see cref="MoveThingCommand"/>, where
/// the caller already has the "from" value on hand from its own drag-start
/// snapshot, a future property dialog is unlikely to have one. A null
/// <c>newValue</c> means "this field shouldn't exist" and removes the key
/// - the same "omit the key entirely" convention <see cref="UniFields"/>'s
/// own typed accessors already use, rather than storing an explicit
/// sentinel. Multi-element/multi-field edits compose via
/// <see cref="CommandGroup"/> as-is - one <see cref="SetFieldCommand"/>
/// per changed field per element.
/// </summary>
public sealed class SetFieldCommand : ICommand
{
    private readonly UniFields fields;
    private readonly string key;
    private readonly UniValue? oldValue;
    private readonly UniValue? newValue;
    private readonly Action? onChanged;

    /// <param name="onChanged">
    /// <see cref="SetPropertyCommand{TOwner,TValue}"/>'s own optional
    /// post-apply hook, ported here too - a real, reported bug this
    /// fixes: a field-only edit (e.g. a sidedef's own per-texture-part
    /// offset, which has no typed property of its own to go through
    /// <see cref="SetPropertyCommand{TOwner,TValue}"/> instead) saved
    /// correctly but never marked the owning sector dirty, so the 3D
    /// view stayed visually stale until some unrelated edit happened to
    /// trigger a rebuild. No owner parameter the way that class's own
    /// callback gets one - this command has no single typed "owner" to
    /// hand back, just a <see cref="UniFields"/> bag - so the caller
    /// closes over whatever it needs instead.
    /// </param>
    public SetFieldCommand(UniFields fields, string key, UniValue? newValue, Action? onChanged = null)
    {
        this.fields = fields;
        this.key = key;
        this.newValue = newValue;
        this.onChanged = onChanged;
        oldValue = fields.TryGetValue(key, out var existing) ? existing : null;
    }

    public void Do() => Apply(newValue);

    public void Undo() => Apply(oldValue);

    private void Apply(UniValue? value)
    {
        if (value == null) fields.Remove(key);
        else fields[key] = value;
        onChanged?.Invoke();
    }
}
