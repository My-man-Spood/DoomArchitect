namespace DoomArchitect.Core.Map;

/// <summary>
/// One field's value: a <see cref="UniversalType"/> tag plus the boxed
/// CLR value itself. A close port of UDB's own <c>UniValue</c>, with two
/// deliberate divergences: <see cref="Value"/> validates against
/// <see cref="long"/> rather than UDB's <see cref="int"/> - a
/// continuation of <c>UdmfValue.ToObject()</c>'s already-established
/// choice to box UDMF integers as <c>long</c> to avoid a narrowing
/// conversion on load, not a new one - and <see cref="Type"/> is the real
/// <see cref="UniversalType"/> enum rather than a raw <c>int</c>. UDB's
/// <c>Owner</c>/<c>BeforeFieldsChange()</c>/<c>ReadWrite(...)</c> members
/// (its automatic undo-snapshot machinery) are not ported - they're
/// incompatible with this codebase's explicit <c>ICommand.Do()/Undo()</c>
/// model, used instead (see <c>SetFieldCommand</c>).
/// </summary>
public sealed class UniValue
{
    private object _value = null!;

    public UniValue(UniversalType type, object value)
    {
        Type = type;
        Value = value;
    }

    public UniValue(UniValue copyFrom)
    {
        Type = copyFrom.Type;
        _value = copyFrom._value;
    }

    public UniversalType Type { get; set; }

    public object Value
    {
        get => _value;
        set => _value = value is long or double or bool or string
            ? value
            : throw new ArgumentException("Universal field values can only be of type long, double, bool, or string.", nameof(value));
    }
}
