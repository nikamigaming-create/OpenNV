using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void AuditOwnedSourceMenuLifecycles(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        AuditOwnedSourceMenuLifecycles(records, main);
    }

    internal static void AuditOwnedSourceMenuLifecycles(FalloutPluginStack records, FalloutMainScriptCallerSource main)
    {
        var owned = records.OwnedSource ?? throw new InvalidOperationException("Menu audit requires its actual selected owned stack.");
        var source = FalloutExecutableStringTable.ReadStandaloneMenuLifecycles(owned.FalloutExecutablePath,
            FalloutStandaloneInterfaceSource.Read(main));
        var ui = FalloutUiComponentStore.Open(records, owned); var root = ui.BindMenuRoot("StartMenu");
        var tree = ui.CopyComponent(root);
        if (tree.Element("class")?.Value.Trim() != "entity_StartMenu" ||
            !FalloutStandaloneDialogSource.Registers(BitConverter.SingleToUInt32Bits(FalloutStandaloneDialogSource.Stacking(
                tree.Element("stackingtype") ?? throw new InvalidDataException("Owned StartMenu omitted stackingtype.")))))
            throw new InvalidDataException("Owned StartMenu changed its selected class/root stack behavior.");
        var duration = FalloutStandaloneMenuLifecycleSource.Duration(BitConverter.SingleToUInt32Bits(ui.ReadFloat(root, "fadein")),
            BitConverter.SingleToUInt32Bits(ui.ReadFloat(root, "fadeout")));
        if (!owned.TryRead(FalloutStandaloneMenuLifecycleSource.StartMenuPath, null, out var bytes, out _))
            throw new InvalidDataException("Owned StartMenu source lifetime lost its winning XML.");
        Console.WriteLine("OPENNV_OWNED_SOURCE_MENU_LIFECYCLE_PASS source=" + source.Identity + " stack=" + owned.StackId +
            " xml=" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + " durationBits=" + duration +
            " sourceClassQualified=true inheritedFieldsQualified=true orderedChildrenQualified=true " +
            "originalExecution=UNEXECUTED nativeLifecycle=UNEXECUTED pixels=UNMEASURED");
    }
}
