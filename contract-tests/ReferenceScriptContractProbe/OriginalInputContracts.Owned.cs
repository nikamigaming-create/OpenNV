using OpenNV.Runtime.Content;

internal static partial class OriginalInputContracts
{
    internal static int AuditOwned(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var controls = FalloutPlayerMovementControlSource.Reviewed(main);
        var source = FalloutExecutableStringTable.ReadOriginalInputQueries(content.FalloutExecutablePath, controls);
        source.Validate();
        var defaults = FalloutExecutableStringTable.ReadControlDefaults(content.FalloutExecutablePath);
        Require(defaults["Ready Item"] == ((byte)0x13, (byte)255), "Owned Ready Item default changed its actual independent keyboard/mouse bytes.");
        Require(source.Controls == controls && source.Main == main, "Owned original input query changed its exact selected Player/Main source.");
        Console.WriteLine("Owned FO3 original input source PASS: constructor-selected manager, current getter, Ready Item call order, mode tables, source poll and exact platform imports. Native device/event parity remains unverified.");
        return 0;
    }
}
