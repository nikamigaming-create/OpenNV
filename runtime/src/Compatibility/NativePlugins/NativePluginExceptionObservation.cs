using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Raw addresses and operands belong only to private diagnosis. Even ToString
// returns the same address-free stage/code that the product error exposes.
internal sealed class NativePluginExceptionObservation
{
    internal required uint Thread { get; init; }
    internal required string Stage { get; init; }
    internal required uint InvocationEntry { get; init; }
    internal required uint Code { get; init; }
    internal required bool RecordPresent { get; init; }
    internal required uint RecordPointer { get; init; }
    internal required uint RecordCode { get; init; }
    internal required uint RecordFlags { get; init; }
    internal required uint NestedRecordPointer { get; init; }
    internal required uint ExceptionAddress { get; init; }
    internal required bool ContextPresent { get; init; }
    internal required uint ContextFlags { get; init; }
    internal required bool ControlPresent { get; init; }
    internal required uint ProgramCounter { get; init; }
    internal required uint DeclaredParameterCount { get; init; }
    internal required ImmutableArray<uint> Parameters { get; init; }
    internal required uint PriorCallbackCode { get; init; }
    internal required string PriorCallbackReason { get; init; }
    internal required bool PriorCallbackTruncated { get; init; }
    internal bool ParametersTruncated => DeclaredParameterCount != Parameters.Length;
    internal uint? AccessOperation => RecordPresent && (Code is 0xc0000005 or 0xc0000006) && Parameters.Length >= 2 ? Parameters[0] : null;
    internal uint? AccessOperand => AccessOperation.HasValue ? Parameters[1] : null;
    internal string? AccessKind => AccessOperation switch { 0 => "read", 1 => "write", 8 => "execute", null => null, _ => "unclassified" };
    internal uint? InPageStatus => RecordPresent && Code == 0xc0000006 && Parameters.Length >= 3 ? Parameters[2] : null;
    internal string PublicReason => $"Native call failed in {Stage} (exception code 0x{Code:x8})." +
        (PriorCallbackCode == 0 ? "" : " Source callback failure (" + PriorCallbackCode.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "): " + (PriorCallbackReason.Length == 0 ? "NVSE callback ownership failed." : PriorCallbackReason)) +
        (PriorCallbackTruncated ? " [Native callback diagnostics truncated.]" : "");
    public override string ToString() => PublicReason;

    internal static NativePluginExceptionObservation? Read(BinaryReader reader, uint transportCode, string reason,
        Func<BinaryReader, string> readText)
    {
        if (!Flag(reader)) return null;
        if (reader.ReadUInt32() != 1) throw Invalid("schema");
        var thread = reader.ReadUInt32();
        var stage = readText(reader);
        var entry = reader.ReadUInt32();
        var code = reader.ReadUInt32();
        var recordPresent = Flag(reader);
        var recordPointer = reader.ReadUInt32();
        var recordCode = reader.ReadUInt32();
        var recordFlags = reader.ReadUInt32();
        var nested = reader.ReadUInt32();
        var exceptionAddress = reader.ReadUInt32();
        var contextPresent = Flag(reader);
        var contextFlags = reader.ReadUInt32();
        var controlPresent = Flag(reader);
        var programCounter = reader.ReadUInt32();
        var declared = reader.ReadUInt32();
        var copied = reader.ReadUInt32();
        if (copied != Math.Min(declared, 15U)) throw Invalid("parameter extent");
        var parameters = ImmutableArray.CreateBuilder<uint>(checked((int)copied));
        for (uint index = 0; index < copied; ++index) parameters.Add(reader.ReadUInt32());
        var priorCode = reader.ReadUInt32();
        var priorReason = readText(reader);
        var priorTruncated = Flag(reader);
        if (thread == 0 || entry == 0 || stage.Length is 0 or > 256 || stage.Any(value => value is < ' ' or > '~'))
            throw Invalid("thread/stage/invocation");
        if (transportCode != (priorCode != 0 ? priorCode : code == 0 ? 574U : code)) throw Invalid("exception/transport code");
        if (recordPresent ? recordPointer == 0 || recordCode != code :
            recordPointer != 0 || recordCode != 0 || recordFlags != 0 || nested != 0 || exceptionAddress != 0 || declared != 0)
            throw Invalid("Windows record availability");
        if (contextPresent ? controlPresent != ((contextFlags & 0x00010001U) == 0x00010001U) : contextFlags != 0 || controlPresent)
            throw Invalid("x86 context availability");
        if (!controlPresent && programCounter != 0) throw Invalid("x86 control extent");
        if (priorReason.Length > 512 || priorCode == 0 && (priorReason.Length != 0 || priorTruncated)) throw Invalid("prior callback prefix");
        var observed = new NativePluginExceptionObservation
        {
            Thread = thread,
            Stage = stage,
            InvocationEntry = entry,
            Code = code,
            RecordPresent = recordPresent,
            RecordPointer = recordPointer,
            RecordCode = recordCode,
            RecordFlags = recordFlags,
            NestedRecordPointer = nested,
            ExceptionAddress = exceptionAddress,
            ContextPresent = contextPresent,
            ContextFlags = contextFlags,
            ControlPresent = controlPresent,
            ProgramCounter = programCounter,
            DeclaredParameterCount = declared,
            Parameters = parameters.MoveToImmutable(),
            PriorCallbackCode = priorCode,
            PriorCallbackReason = priorReason,
            PriorCallbackTruncated = priorTruncated
        };
        if (!StringComparer.Ordinal.Equals(reason, observed.PublicReason)) throw Invalid("public stage/code correlation");
        return observed;
    }

    private static bool Flag(BinaryReader reader) => reader.ReadUInt32() switch
    {
        0 => false,
        1 => true,
        _ => throw Invalid("Boolean flag")
    };
    private static InvalidDataException Invalid(string field) => new($"Native exception receipt has an invalid {field}.");
}

internal sealed class NativePluginExceptionReceipt
{
    internal required ulong Generation { get; init; }
    internal required int ProcessId { get; init; }
    internal required ulong Call { get; init; }
    internal required ulong ParentCall { get; init; }
    internal required NativePluginDomainOperation Operation { get; init; }
    internal required NativePluginExceptionObservation Observation { get; init; }
    internal required ulong? Module { get; init; }
    internal required uint? Image { get; init; }
    internal required string? ModulePath { get; init; }
    internal required string? ModuleSha256 { get; init; }
    internal required string? StackSha256 { get; init; }
    public override string ToString() => Observation.PublicReason;
}
