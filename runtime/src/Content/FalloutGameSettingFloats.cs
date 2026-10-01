namespace OpenNV.Runtime.Content;

internal static class FalloutGameSettingFloats
{
    internal static float Read(FalloutPluginStack records, string name) => records.NumericSettings.Float(name);
    internal static float ReadRetained(FalloutPluginStack records, string name, string owner)
    {
        records.NumericSettings.RequireRetainedConsumer(name, owner);
        return Read(records, name);
    }
}
