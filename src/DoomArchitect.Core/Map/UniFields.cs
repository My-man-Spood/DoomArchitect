namespace DoomArchitect.Core.Map;

/// <summary>
/// A map element's bag of format-recognized fields that aren't modeled as
/// typed properties - UDB's own naming and shape (a <c>Dictionary</c>
/// subclass), kept deliberately close so porting UDB's dialog logic later
/// needs less translation. UDB's <c>Owner</c>/<c>BeforeFieldsChange()</c>
/// (its automatic undo-snapshot hook) are not ported - see
/// <see cref="UniValue"/>'s doc comment for why - and its mixed-value
/// comparison helpers (<c>AllFieldsMatch</c>/<c>CustomFieldsMatch</c>/
/// <c>UniValuesMatch</c>/<c>ValuesMatch</c>) are deferred to whenever a
/// multi-select-editing dialog actually needs them.
/// </summary>
public sealed class UniFields : Dictionary<string, UniValue>
{
    /// <summary>
    /// Returns the value of a field by name, or <paramref name="defaultValue"/>
    /// when no such field exists or its value isn't a <typeparamref name="T"/>.
    /// </summary>
    public T GetValue<T>(string key, T defaultValue)
    {
        if (!TryGetValue(key, out var value)) return defaultValue;

        try
        {
            return (T)value.Value;
        }
        catch (InvalidCastException)
        {
            return defaultValue;
        }
    }
}
