namespace DoomArchitect.Core.Undo;

/// <summary>
/// Sets one typed property on one element (e.g. <c>Sector.FloorHeight</c>) -
/// the typed-property counterpart to <see cref="SetFieldCommand"/>, which
/// only knows how to touch a <c>Fields</c> bag. Generic over both the owner
/// and value type so it serves any element's typed properties (Sector
/// today, Linedef/Thing's own typed properties in later property dialogs)
/// without a near-duplicate command class per property.
///
/// Deliberately does <b>not</b> capture <c>oldValue</c> via a getter at
/// construction time the way <see cref="SetFieldCommand"/> does - a
/// property dialog applies edits live as the user types (for immediate
/// visual feedback) and only builds this command afterward, once, to
/// record the whole edit as a single undo step; by then the "current"
/// value read from the object would already equal <c>newValue</c>, not the
/// pre-edit original. Both values must be supplied explicitly instead,
/// from whatever snapshot the caller took before any live edits began.
/// </summary>
public sealed class SetPropertyCommand<TOwner, TValue> : ICommand
{
    private readonly TOwner owner;
    private readonly Action<TOwner, TValue> setter;
    private readonly Action<TOwner>? onChanged;
    private readonly TValue oldValue;
    private readonly TValue newValue;

    public SetPropertyCommand(TOwner owner, Action<TOwner, TValue> setter, TValue oldValue, TValue newValue, Action<TOwner>? onChanged = null)
    {
        this.owner = owner;
        this.setter = setter;
        this.oldValue = oldValue;
        this.newValue = newValue;
        this.onChanged = onChanged;
    }

    public void Do() => Apply(newValue);

    public void Undo() => Apply(oldValue);

    private void Apply(TValue value)
    {
        setter(owner, value);
        onChanged?.Invoke(owner);
    }
}
