namespace OpenNV.Runtime.Content;

internal static class FalloutGameSettingIntegers
{
    internal static uint Read(FalloutPluginStack records, string name) => records.NumericSettings.IntegerBits(name);
    internal static uint ReadRetained(FalloutPluginStack records, string name, string owner)
    {
        records.NumericSettings.RequireRetainedConsumer(name, owner);
        return Read(records, name);
    }
}
