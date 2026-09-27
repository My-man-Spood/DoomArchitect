namespace DoomArchitect.Core.Map;

/// <summary>
/// Typed accessors for <see cref="UniFields"/>, as extension methods so
/// call sites read <c>element.Fields.GetInteger(...)</c>. <c>Set*</c>
/// methods never store an explicit default value in the field bag - only
/// what differs from it. <see cref="GetBool"/>/<see cref="SetBool"/> and
/// <see cref="GetString"/> round out the accessor surface to cover all
/// four <see cref="UniversalType"/> kinds.
/// </summary>
public static class UniFieldsExtensions
{
    public static long GetInteger(this UniFields fields, string key, long defaultValue = 0) =>
        fields.GetValue(key, defaultValue);

    public static void SetInteger(this UniFields fields, string key, long value, long defaultValue = 0)
    {
        if (value == defaultValue) fields.Remove(key);
        else fields[key] = new UniValue(UniversalType.Integer, value);
    }

    public static double GetFloat(this UniFields fields, string key, double defaultValue = 0.0) =>
        fields.GetValue(key, defaultValue);

    public static void SetFloat(this UniFields fields, string key, double value, double defaultValue = 0.0)
    {
        if (value == defaultValue) fields.Remove(key);
        else fields[key] = new UniValue(UniversalType.Float, value);
    }

    public static string GetString(this UniFields fields, string key, string defaultValue = "") =>
        fields.GetValue(key, defaultValue);

    public static void SetString(this UniFields fields, string key, string value, string defaultValue)
    {
        if (value == defaultValue) fields.Remove(key);
        else fields[key] = new UniValue(UniversalType.String, value);
    }

    public static bool GetBool(this UniFields fields, string key, bool defaultValue = false) =>
        fields.GetValue(key, defaultValue);

    public static void SetBool(this UniFields fields, string key, bool value, bool defaultValue = false)
    {
        if (value == defaultValue) fields.Remove(key);
        else fields[key] = new UniValue(UniversalType.Boolean, value);
    }

    public static void RemoveField(this UniFields fields, string key) => fields.Remove(key);

    public static void RemoveFields(this UniFields fields, IEnumerable<string> keys)
    {
        foreach (var key in keys) fields.Remove(key);
    }
}
