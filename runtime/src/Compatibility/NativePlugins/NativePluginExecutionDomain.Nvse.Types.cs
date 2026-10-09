using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativeNvseHostCall : uint
{
    QueryInterface = 1, SetOpcode = 2, RegisterCommand = 3, RegisterListener = 4,
    SerializationCallback = 5, Unsupported = 6, DispatchMessage = 7,
    DeliveredMessage = 8, DeliveredSerialization = 9, BeginSerialization = 10,
}
internal enum NativeNvsePhase { Mapped, Querying, QueriedTrue, QueriedFalse, Loading, LoadedTrue, LoadedFalse, Retired, Faulted }
internal enum NativeNvseSerializationEvent : uint { Save, Load, NewGame, PreLoad }
internal enum NativeNvseCommandReturn : byte { Default, Form, String, Array, ArrayIndex, Ambiguous }
// Native char declarations are bytes, not an assumed UTF-8/texture namespace.
// Latin1 is a reversible diagnostic display; only ASCII identifiers are looked up.
internal sealed record NativeNvseText(uint Address, ImmutableArray<byte> Bytes)
{
    internal string Display => System.Text.Encoding.Latin1.GetString(Bytes.AsSpan());
    internal bool IsAscii => Bytes.All(value => value < 128);
    internal bool IsNull => Address == 0;
}
internal sealed record NativeNvseParameter(NativeNvseText Name, uint Type, uint Optional);
internal sealed record NativeNvseCommand(
    ulong Generation, ulong Module, uint PluginHandle, ulong Registration,
    uint SourceAddress, uint RawOpcode, uint AssignedOpcode, uint OpcodeBase,
    byte RawReturnType, NativeNvseCommandReturn ReturnType, uint RequiredPluginVersion,
    ushort NeedsParent, uint Flags, uint ParameterAddress,
    NativeNvseText Name, NativeNvseText Alias, NativeNvseText Help,
    ImmutableArray<NativeNvseParameter> Parameters, uint Execute, uint Parse, uint Evaluate)
{
    internal string ExecutionAdmission => NeedsParent == 0 && Parse == 0x08000000 && ReturnType == NativeNvseCommandReturn.Default
        ? "explicit-objectless-expression-caller-required; original-execution-unverified" : "native-object-or-special-caller-owner-absent";
    internal string ParseAdmission => Parse switch
    {
        0 => "core-default-parser-owner-absent",
        0x08000000 => "original-source-argument-parser-admission-required; retained-parser-uninvoked",
        _ => "original-parser-retained-but-uninvoked",
    };
    internal string EvaluationAdmission => Evaluate == 0 ? "not-declared" : "original-evaluator-retained-but-uninvoked";
    internal string ResultPublication => "requires-authoritative-caller-result-target; original-command-execution-unverified";
}
internal sealed record NativeNvseInterfaceQuery(ulong Callback, uint Interface, uint Version, string? MissingOwner);
internal sealed record NativeNvseListenerRegistration(ulong Callback, uint Handle,
    NativeNvseText Sender, uint Function, bool Returned, uint? BoundSender, uint? RetainedFunction);
internal sealed record NativeNvseSerializationRegistration(ulong Callback, uint Handle,
    NativeNvseSerializationEvent Event, uint Function);
internal sealed record NativeNvseUnownedRequest(ulong Callback, string Operation);
internal sealed record NativeNvseMessageDispatch(ulong Callback, ulong Parent, ulong Dispatch,
    uint Sender, uint Type, uint DataAddress, ImmutableArray<byte> Data,
    NativeNvseText Receiver, uint ExpectedCallbacks);
internal sealed record NativeNvseMessageCompletion(ulong Callback, ulong Parent, ulong Dispatch,
    uint Function, uint Sender, uint Type, uint DataAddress, uint Length);
internal sealed record NativeNvseSerializationCompletion(ulong Callback, ulong Parent,
    NativeNvseSerializationEvent Event, uint Function);
internal sealed record NativeNvsePluginInfo(uint InfoVersion, uint Version, NativeNvseText Name);
internal readonly record struct NativeNvseRegistryCounts(uint CommandAttempts, uint ListenerAttempts,
    uint SerializationAttempts, uint Commands, uint Listeners);
internal sealed record NativeNvseInitializationReceipt(string Stage, bool Returned,
    uint RawEax, int StackDelta, uint PreservedRegisters, uint ExceptionCode,
    NativeNvsePluginInfo Info, NativeNvseRegistryCounts Registry);
internal sealed record NativeNvseRetirementReceipt(NativeNvseRegistryCounts Registry,
    bool ImageMappingPresent, bool InterfaceMappingPresent);

internal sealed class NativeNvsePlugin
{
    internal ulong Generation { get; }
    internal ulong Module { get; }
    internal uint Handle { get; set; }
    internal uint Image { get; }
    internal uint Interface { get; }
    internal uint Query { get; }
    internal uint Load { get; }
    internal string Path { get; }
    internal string Sha256 { get; }
    internal NativeNvsePhase Phase { get; set; }
    internal NativeNvseInitializationReceipt? QueryReceipt { get; set; }
    internal NativeNvseInitializationReceipt? LoadReceipt { get; set; }
    internal NativeNvseRegistry Registry { get; }
    internal NativeNvseRetirementReceipt? Retirement { get; set; }
    internal NativeNvsePlugin(ulong generation, ulong module, uint handle, uint image, uint nativeInterface,
        uint query, uint load, string path, string sha256)
    {
        Generation = generation; Module = module; Handle = handle; Image = image; Interface = nativeInterface;
        Query = query; Load = load; Path = path; Sha256 = sha256; Registry = new(this);
    }
}
