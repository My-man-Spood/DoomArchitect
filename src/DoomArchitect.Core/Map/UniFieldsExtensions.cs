namespace DoomArchitect.Core.Map;

/// <summary>
/// Typed accessors for <see cref="UniFields"/>. Ported from UDB's own
/// static <c>UniFields.SetFloat</c>/<c>GetFloat</c>/etc. (which take the
/// field bag as an explicit first parameter, an older C# idiom), as
/// extension methods instead so call sites read
/// <c>element.Fields.GetInteger(...)</c> - an intentional idiom
/// modernization, not a behavior change. <c>Set*</c> methods preserve
/// UDB's "omit the key entirely when the value equals the default" rule
/// exactly: a field bag never stores an explicit default value, only
/// what differs from it. <see cref="GetBool"/>/<see cref="SetBool"/> and
/// <see cref="GetString"/> don't exist in UDB's own surface (confirmed
/// via source grep - a real gap there) - added here deliberately so the
/// accessor surface covers all four <see cref="UniversalType"/> kinds,
/// not two of four.
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
