namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal readonly record struct NativeNvseInputKey(bool Raw, bool Game, bool Inserted,
    bool Hold, bool Tap, bool UserDisabled, bool ScriptDisabled)
{
    internal bool SameControls(NativeNvseInputKey other)
        => Hold == other.Hold && Tap == other.Tap && UserDisabled == other.UserDisabled && ScriptDisabled == other.ScriptDisabled;
}
internal readonly record struct NativeNvseInputControlChange(int Key, NativeNvseInputKey Before, NativeNvseInputKey After);
internal sealed record NativeNvseInputSnapshot(string Source, ulong Window, ulong Sample,
    long Revision, IReadOnlyList<NativeNvseInputKey> Keys, bool Acquired, string? AcquisitionFailure)
{
    internal const int KeyCount = 266;
}
internal enum NativeNvseDataCall : uint
{
    InputCreate = 1, InputExchange = 2, Function = 3, InventoryCreate = 4,
    InventoryGet = 5, InventorySelf = 6, LambdaSave = 7, LambdaUnsave = 8,
    IsLambda = 9, Singleton = 10, Data = 11, ClearCache = 12
}
internal sealed record NativeNvseDataCallback(ulong Generation, ulong Callback, ulong Parent,
    NativeNvseDataCall Call, uint Identity, string? Failure);
internal sealed record NativeNvseInventoryPublication(uint FormId, uint Reference, uint InventoryReference,
    uint Entry, uint ExtraData, IDisposable Lifetime, Action RequireCurrent);

internal abstract class NativeNvseRuntimeDataAuthority
{
    internal abstract string SourceIdentity { get; }
    internal abstract void RequireCurrent();
    internal abstract IDisposable RetainSource();
    internal abstract NativeNvseInputSnapshot CaptureInput();
    internal abstract void PublishInput(IReadOnlyList<NativeNvseInputControlChange> changes);
    internal virtual NativeNvseInventoryPublication CreateInventory(uint container, uint item, int count, uint extra)
        => throw new NotSupportedException("InventoryCreateEntry requires genuine TESObjectREFR/item/EntryData/ExtraDataList constructors and current frame ownership.");
    internal virtual void RequireInventoryAbsence(uint formId)
        => throw new NotSupportedException("Inventory-reference absence requires the complete shared frame map, including other original modules.");
    internal virtual void ClearScriptDataCache()
        => throw new NotSupportedException("Data ClearScriptDataCache requires shared token/UDF caches and its actual invalidation event consumer.");
}

// The producer of a lambda must join a genuine native Script and its parent
// event list. This lease does not compile functions or make ordinary SCPT a lambda.
internal abstract class NativeNvseLambdaCaptureAuthority
{
    internal abstract string SourceOwner { get; }
    internal abstract void RequireCurrent();
    internal abstract IReadOnlyList<NativeNvseSourceObject> ChildLambdas();
    internal abstract IDisposable RetainValues();
}
internal sealed class NativeNvseLambdaCapture(NativeNvseSourceObject script, NativeNvseLocalContext context,
    NativeNvseLambdaCaptureAuthority authority)
{
    internal NativeNvseSourceObject Script { get; } = script;
    internal NativeNvseLocalContext Context { get; } = context;
    internal NativeNvseLambdaCaptureAuthority Authority { get; } = authority;
    internal int Saves;
    internal IReadOnlyList<NativeNvseSourceObject>? Children;
    internal readonly Stack<NativeNvseLambdaSaveLease> Leases = [];
}
internal sealed record NativeNvseLambdaSaveLease(IReadOnlyList<NativeNvseLambdaCapture> Captures, IDisposable Values);
