using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeSourceModelOperandEvent(FalloutRaceMenuModelPathInvocation Invocation, uint Action,
    uint Caller, uint Address, uint Extent, uint Requested, uint Output, uint OldBefore, uint OldAfter,
    uint SdkResult, uint LastError, uint Protection, string RuntimeSha256, string ModuleSha256, string StackSha256,
    string Family, string? Failure = null);

internal sealed partial class NativePluginExecutionDomain
{
    private FalloutRaceMenuModelPath? _raceMenuModelPath;
    private NativePluginModuleModelPathSource? _raceMenuModelSource;
    private NativeNvsePlugin? _raceMenuModelModule;
    private ulong _raceMenuModelLease;
    private uint _raceMenuProtection = 0x20, _raceMenuOutput;
    private readonly List<NativeSourceModelOperandEvent> _modelOperandEvents = [];
    internal FalloutRaceMenuModelPath? NvseRaceMenuModelPath => _raceMenuModelPath;
    internal IReadOnlyList<NativeSourceModelOperandEvent> SourceModelOperandEvents => _modelOperandEvents.AsReadOnly();

    internal void BindNvseRaceMenuModelPath(NativeNvsePlugin primary, FalloutRaceMenuModelPath field)
    {
        VerifyNvse(primary); RequireNvseEmptyCall();
        var host = _nvseHostSource!;
        if (!ReferenceEquals(primary, _primaryNvseModule) || _raceMenuModelPath is not null ||
            !StringComparer.OrdinalIgnoreCase.Equals(host.RuntimePath, field.Source.RuntimePath) ||
            !StringComparer.OrdinalIgnoreCase.Equals(host.RuntimeSha256, field.Source.RuntimeSha256))
            throw new InvalidDataException("Model input binding changed its selected executable or actual shared host.");
        RequireSourceAddressSpace(host); field.Source.CheckSame(FalloutRaceMenuModelPathSource.Read(host.RuntimePath));
        _raceMenuModelPath = field;
        field.Bind(this, RequireRaceMenuModelCurrent, ReadRaceMenuNativeModel);
    }
    private void RequireRaceMenuModelCurrent()
    {
        VerifyOwner(); _nvseHostSource!.Check(Generation);
        if (_raceMenuModelModule is { } module && module.Phase is NativeNvsePhase.Faulted or NativeNvsePhase.Retired)
            throw new InvalidOperationException("Model input retains its failed or retired actual module lifetime.");
    }
    private void PrepareNvseRaceMenuModelPath(NativeNvsePlugin plugin)
    {
        if (_raceMenuModelPath is not { } field || !IsSupplementalNvseModule(plugin)) return;
        RequireNvseEmptyCall();
        var source = NativePluginModuleModelPathSource.Read(_nvseModuleDeclarations[plugin.Module], field.Source);
        if (source is null) return;
        if (_raceMenuModelSource is not null || _raceMenuModelLease != 0)
            throw new NotSupportedException("A second original module cannot substitute the same mutable model operand owner.");
        _raceMenuModelSource = source; _raceMenuModelModule = plugin;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer =>
            {
                writer.Write(4U); writer.Write(plugin.Module); writer.Write(field.Source.Instruction); writer.Write(field.Source.Pointer);
                writer.Write(source.LoadRva); writer.Write(source.ProtectReturnRva); writer.Write(source.RestoreReturnRva);
                writer.Write(source.BufferRva); writer.Write(source.Capacity); writer.Write(source.InitialBytes.AsSpan());
            }));
            _raceMenuModelLease = reader.ReadUInt64(); var instruction = reader.ReadUInt32();
            var pointer = reader.ReadUInt32(); var protection = reader.ReadUInt32(); Finish(reader);
            if (_raceMenuModelLease == 0 || instruction != field.Source.Instruction || pointer != checked(plugin.Image + source.BufferRva) || protection != 0x20)
                throw new InvalidDataException("Model input publication lost its actual source/module-data/page lease.");
            _raceMenuProtection = protection;
            if (!ReadModelInstruction().AsSpan().SequenceEqual(field.Source.Preimage.AsSpan()) || !ReadModelBuffer().AsSpan().SequenceEqual(source.InitialBytes.AsSpan()))
                throw new InvalidDataException("Model input publication changed its exact source preimage or actual mapped CString.");
        }
        catch (Exception failure) { throw Fatal(failure); }
    }
    private uint DispatchNvseModelOperand(Frame frame, ulong parent, BinaryReader reader)
    {
        var field = _raceMenuModelPath ?? throw new NotSupportedException("Model input mutation has no actual product field owner.");
        var plugin = _raceMenuModelModule ?? throw new InvalidDataException("Model input mutation lost its actual source module.");
        var source = _raceMenuModelSource!;
        var invocation = new FalloutRaceMenuModelPathInvocation(Generation, ProcessId, plugin.Module, NativeThread, parent, frame.Id);
        var action = reader.ReadUInt32(); var caller = reader.ReadUInt32(); var address = reader.ReadUInt32();
        var extent = reader.ReadUInt32(); var requested = reader.ReadUInt32(); var output = reader.ReadUInt32();
        var before = reader.ReadUInt32(); var after = reader.ReadUInt32(); var result = reader.ReadUInt32(); var last = reader.ReadUInt32();
        var protection = reader.ReadUInt32(); var cohort = reader.ReadUInt32(); var cohortBytes = reader.ReadUInt32(); Finish(reader);
        var observation = new NativeSourceModelOperandEvent(invocation, action, caller, address, extent, requested, output, before, after,
            result, last, protection, field.Source.RuntimeSha256, source.Sha256, _nvseHostSource!.StackIdentity, source.Family);
        _modelOperandEvents.Add(observation);
        try
        {
            source.Check(_nvseModuleDeclarations[plugin.Module]);
            if (!_process.ReadModuleModelWriter(plugin.Image, source.ImageBytes, source.WriterRva).AsSpan().SequenceEqual(source.MappedWriterCode(plugin.Image)))
                throw new NotSupportedException("The current native model writer changed its actual source instructions or import association.");
            if (!ReferenceEquals(plugin, _nvsePlugin) || plugin.Phase != NativeNvsePhase.Loading ||
                _operation != nameof(NativePluginDomainOperation.NvseLoad) || _raceMenuModelLease == 0 ||
                address != field.Source.Operand || extent != 4 || action > 3 || output == 0)
                throw new NotSupportedException("Model mutation escaped its exact original Load/module/operand scope.");
            if (action is 0 or 1)
            {
                if (caller != checked(plugin.Image + source.ProtectReturnRva) || requested != 0x40)
                    throw new NotSupportedException("Model mutation has a foreign first protection caller or request.");
            }
            else if (caller != checked(plugin.Image + source.RestoreReturnRva) || output != _raceMenuOutput)
                throw new NotSupportedException("Model restoration lost its actual helper caller/output cell.");
            if (action == 0)
            {
                if (cohort != 0 || cohortBytes != 0 || result != 0 || after != before || protection != _raceMenuProtection)
                    throw new InvalidDataException("Model protection entry invented an SDK result or page mutation.");
                _raceMenuOutput = output; field.EnterProtect(this, invocation, ReadModelInstruction(), before);
            }
            else if (action == 1)
            {
                if (output != _raceMenuOutput) throw new InvalidDataException("Model protection result changed its original output cell.");
                ApplyModelProtection(result, protection, cohort, cohortBytes, after, requested);
                field.Protected(this, invocation, result, last, before, after);
                if (!ReadModelInstruction().AsSpan().SequenceEqual(field.Source.Preimage.AsSpan()))
                    throw new InvalidDataException("The model DWORD changed before the original source store.");
            }
            else if (action == 2)
            {
                if (result != 0 || before != after || protection != _raceMenuProtection || cohort != 0 || cohortBytes != 0)
                    throw new InvalidDataException("Original store observation invented a protection result.");
                var buffer = ReadModelBuffer();
                if (!buffer.AsSpan().SequenceEqual(source.InitialBytes.AsSpan()))
                    throw new NotSupportedException("The model buffer has an unowned preceding mutable writer.");
                field.ObserveStore(this, invocation, ReadModelInstruction(), checked(plugin.Image + source.BufferRva),
                    FalloutExecutableStringTable.ReadRaceMenuModelPathBytes(buffer));
            }
            else
            {
                ApplyModelProtection(result, protection, cohort, cohortBytes, after, requested);
                field.Restored(this, invocation, requested, result, last, before, after);
                var instruction = ReadModelInstruction();
                if (instruction[0] != 0x68 || BinaryPrimitives.ReadUInt32LittleEndian(instruction.AsSpan(1)) != field.Pointer)
                    throw new InvalidDataException("Model restoration lost its already stored current pointer.");
            }
            return 1;
        }
        catch (Exception failure)
        {
            _modelOperandEvents[^1] = observation with { Failure = failure.Message };
            field.Fail(this, invocation, failure.Message); throw;
        }
    }
    private void ApplyModelProtection(uint result, uint protection, uint cohort, uint cohortBytes, uint old, uint requested)
    {
        var field = _raceMenuModelPath!;
        if (result == 0)
        {
            if (cohort != 0 || cohortBytes != 0 || protection != _raceMenuProtection)
                throw new InvalidDataException("Failed model SDK result invented a committed page transition.");
            return;
        }
        var reservation = SourceAddressSpaceReceipt!; var end = (ulong)cohort + cohortBytes;
        if (cohortBytes == 0 || cohort < reservation.ImageBase || end > (ulong)reservation.ImageBase + reservation.ReservedBytes ||
            field.Source.Operand < cohort || (ulong)field.Source.Operand + 4 > end || old != _raceMenuProtection ||
            protection != requested || protection is not (0x20 or 0x40))
            throw new InvalidDataException("Model protection changed its actual old access or SDK page cohort.");
        PublishPointerProtectionCohort(cohort, cohortBytes, protection);
    }

    private byte[] ReadModelInstruction()
    {
        var host = _nvseHostSource!;
        return _process.ReadSourceModelOperand(_raceMenuModelPath!.Source.Instruction, host.SourceAddressSpace.ImageBase,
            host.SourceAddressSpace.ImageBytes, _raceMenuProtection);
    }
    private byte[] ReadModelBuffer()
    {
        var source = _raceMenuModelSource!; var module = _raceMenuModelModule!;
        return _process.ReadModuleModelBuffer(module.Image, source.ImageBytes, source.BufferRva, source.Capacity);
    }
    private string ReadRaceMenuNativeModel()
    {
        var field = _raceMenuModelPath!;
        if (_raceMenuModelSource is null) return field.Source.Model;
        var bytes = ReadModelInstruction();
        if (bytes[0] != 0x68 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(1)) != field.Pointer)
            throw new InvalidDataException("The current model operand changed outside its retained store history.");
        if (field.Pointer == field.Source.Pointer) return field.Source.Model;
        var buffer = ReadModelBuffer();
        if (!buffer.AsSpan().SequenceEqual(_raceMenuModelSource.InitialBytes.AsSpan()))
            throw new NotSupportedException("The original model buffer needs its actual current source-copy producer.");
        return FalloutExecutableStringTable.ReadRaceMenuModelPathBytes(buffer);
    }
    private void RetireNvseRaceMenuModelPath(NativeNvsePlugin plugin)
    {
        if (!ReferenceEquals(plugin, _raceMenuModelModule) || _raceMenuModelLease == 0) return;
        _raceMenuModelPath!.RequireRetirement(this);
        using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer => { writer.Write(5U); writer.Write(plugin.Module); writer.Write(_raceMenuModelLease); }));
        var retired = reader.ReadUInt32(); var call = reader.ReadUInt64(); Finish(reader);
        if (retired != 1 || ReadModelInstruction().Any(value => value != 0xcc))
            throw new InvalidDataException("Model input retirement lost its actual trap readback and module-data release.");
        _raceMenuModelPath.Retired(this, new(Generation, ProcessId, plugin.Module, NativeThread, call, 0));
        _raceMenuModelLease = 0;
    }
    private void ClearRaceMenuModelAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("The model input data lease requires verified original child closure.");
        _raceMenuModelPath?.ClosedAfterChildExit(this); _raceMenuModelLease = 0;
    }
    private void FailRaceMenuModelCall(NativeNvsePlugin plugin, Exception failure)
    {
        if (!ReferenceEquals(plugin, _raceMenuModelModule) || _modelOperandEvents.Count == 0 || _raceMenuModelPath is null) return;
        _raceMenuModelPath.Fail(this, _modelOperandEvents[^1].Invocation, failure.Message);
    }
}
