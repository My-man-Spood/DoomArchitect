namespace DoomArchitect.Core.IO;

public sealed class UdmfAssignment
{
    public UdmfAssignment(string key, UdmfValue value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    public UdmfValue Value { get; }
}
