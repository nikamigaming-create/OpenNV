using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativePluginModuleAdmission(string LogicalPath, string PhysicalPath, string Sha256,
    IReadOnlyList<NativePluginIoWriteScope> WriteScopes, IReadOnlyList<FalloutNativePluginIoReadDeclaration> ReadDeclarations,
    IReadOnlyList<string> InputRoots, string ImportOwner, IReadOnlySet<string> NonIoImports,
    NativeNvseExpressionAbi ExpressionAbi, NativeNvseHeapDeclaration? Heap);
internal sealed record FalloutNativePluginCampaignSelection(RuntimeLiveContentSource Source,
    string RuntimeSha256, string NvsePath, string NvseSha256, bool? NoGore, string EditionOwner,
    string LoadOrderOwner, IReadOnlyList<FalloutNativePluginModuleAdmission> Modules);
internal sealed record FalloutNativePluginCampaignModule(string LogicalPath, string PhysicalPath, string Sha256,
    ulong? Generation, int? ProcessId, bool? Query, bool? Load, string? Failure,
    IReadOnlyList<string> Unowned, bool Retired);
internal sealed class FalloutNativePluginCampaignConstructionFault(Exception original, Exception retirement, FalloutNativePluginCampaign owner)
    : AggregateException("Native campaign construction and retirement failed.", original, retirement)
{
    internal FalloutNativePluginCampaign Owner { get; } = owner;
}
internal sealed record FalloutNativePluginSourceCall(FalloutFormKey Owner, FalloutCompiledScriptProgram Program,
    ushort Opcode, ushort? Receiver, uint Start, uint End, Func<IReadOnlyList<FalloutScriptValue>> Evaluate,
    NativeNvseValueResultTarget? ResultTarget = null);

internal interface IFalloutNativePluginCampaign : IDisposable
{
    IReadOnlyList<FalloutNativePluginCampaignModule> Modules { get; }
    FalloutCompiledCommandDeclaration? Declaration(ushort opcode);
    FalloutScriptValue Invoke(FalloutNativePluginSourceCall call);
    void RequireIdleForSave();
}
