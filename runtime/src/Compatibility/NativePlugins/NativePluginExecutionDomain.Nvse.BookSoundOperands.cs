using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeSourceBookSoundOperandEvent(FalloutSourceCStringInputInvocation Invocation,
    FalloutSpecialBookPageDirection Direction, uint Action, uint Caller, uint Address, uint Extent, uint Requested,
    uint Output, uint OldBefore, uint OldAfter, uint SdkResult, uint LastError, uint Protection,
    string RuntimeSha256, string ModuleSha256, string StackSha256, string Family, string? Failure = null);

internal sealed partial class NativePluginExecutionDomain
{
    private sealed class BookSoundOperand(FalloutSourceCStringInput field)
    {
        internal readonly FalloutSourceCStringInput Field = field;
        internal ulong Lease;
        internal uint Protection = 0x20, Output;
    }
    private FalloutSpecialBookPageSounds? _bookPageSounds;
    private NativePluginModuleBookSoundSource? _bookSoundSource;
    private NativeNvsePlugin? _bookSoundModule;
    private readonly Dictionary<FalloutSpecialBookPageDirection, BookSoundOperand> _bookSoundOperands = [];
    private readonly List<NativeSourceBookSoundOperandEvent> _bookSoundEvents = [];
    internal IReadOnlyList<NativeSourceBookSoundOperandEvent> SourceBookSoundOperandEvents => _bookSoundEvents.AsReadOnly();
    internal void BindNvseSpecialBookPageSounds(NativeNvsePlugin primary, FalloutSpecialBookPageSounds fields)
    {
        VerifyNvse(primary); RequireNvseEmptyCall(); var host = _nvseHostSource!;
        if (!ReferenceEquals(primary, _primaryNvseModule) || _bookPageSounds is not null ||
            !StringComparer.OrdinalIgnoreCase.Equals(host.RuntimePath, fields.Source.RuntimePath) ||
            !StringComparer.OrdinalIgnoreCase.Equals(host.RuntimeSha256, fields.Source.RuntimeSha256))
            throw new InvalidDataException("Book page inputs changed their actual selected executable/shared domain.");
        RequireSourceAddressSpace(host); fields.Source.CheckSame(FalloutSpecialBookPageSoundsSource.Read(host.RuntimePath));
        _bookPageSounds = fields;
        foreach (var direction in new[] { FalloutSpecialBookPageDirection.Forward, FalloutSpecialBookPageDirection.Backward })
            _bookSoundOperands.Add(direction, new(fields.Field(direction)));
        fields.Bind(this, RequireBookSoundCurrent, ReadNativeBookSound);
    }
    private void RequireBookSoundCurrent()
    {
        VerifyOwner(); _nvseHostSource!.Check(Generation);
        if (_bookSoundModule is { Phase: NativeNvsePhase.Faulted or NativeNvsePhase.Retired })
            throw new InvalidOperationException("Book input retains its actual failed/retired native module lifetime.");
    }
    private void PrepareNvseSpecialBookPageSounds(NativeNvsePlugin plugin)
    {
        if (_bookPageSounds is not { } fields || !IsSupplementalNvseModule(plugin)) return;
        RequireNvseEmptyCall(); var source = NativePluginModuleBookSoundSource.Read(_nvseModuleDeclarations[plugin.Module], fields.Source);
        if (source is null) return;
        if (_bookSoundSource is not null) throw new NotSupportedException("A second original module cannot substitute Book input producers.");
        _bookSoundSource = source; _bookSoundModule = plugin;
        try
        {
            foreach (var direction in new[] { FalloutSpecialBookPageDirection.Forward, FalloutSpecialBookPageDirection.Backward })
            {
                var slot = _bookSoundOperands[direction]; var input = slot.Field.Source;
                using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer =>
                {
                    writer.Write(6U); writer.Write((uint)direction); writer.Write(plugin.Module); writer.Write(input.Instruction); writer.Write(input.Pointer);
                    writer.Write(source.LoadRva); writer.Write(source.ProtectReturnRva); writer.Write(source.RestoreReturnRva);
                    writer.Write(checked((direction == FalloutSpecialBookPageDirection.Forward ? source.ForwardCallRva : source.BackwardCallRva) + 5));
                    writer.Write(source.BufferRva); writer.Write(source.Capacity); writer.Write(source.InitialBytes.AsSpan());
                }));
                slot.Lease = reader.ReadUInt64(); var instruction = reader.ReadUInt32(); var pointer = reader.ReadUInt32();
                slot.Protection = reader.ReadUInt32(); Finish(reader);
                if (slot.Lease == 0 || instruction != input.Instruction || pointer != checked(plugin.Image + source.BufferRva) || slot.Protection != 0x20 ||
                    !ReadBookSoundInstruction(slot).AsSpan().SequenceEqual(input.Preimage.AsSpan()) ||
                    !ReadBookSoundBuffer().AsSpan().SequenceEqual(source.InitialBytes.AsSpan()))
                    throw new InvalidDataException("Book input publication lost its independent source/module data/image lease.");
            }
        }
        catch (Exception failure) { throw Fatal(failure); }
    }
    private uint DispatchNvseBookSoundOperand(Frame frame, ulong parent, BinaryReader reader)
    {
        var direction = (FalloutSpecialBookPageDirection)reader.ReadUInt32();
        if (!_bookSoundOperands.TryGetValue(direction, out var slot)) throw new InvalidDataException("Book callback has no actual typed direction field.");
        var plugin = _bookSoundModule ?? throw new InvalidDataException("Book callback lost its retained original module.");
        var source = _bookSoundSource!; var field = slot.Field;
        var call = new FalloutSourceCStringInputInvocation(Generation, ProcessId, plugin.Module, NativeThread, parent, frame.Id);
        var action = reader.ReadUInt32(); var caller = reader.ReadUInt32(); var address = reader.ReadUInt32(); var extent = reader.ReadUInt32();
        var requested = reader.ReadUInt32(); var output = reader.ReadUInt32(); var before = reader.ReadUInt32(); var after = reader.ReadUInt32();
        var result = reader.ReadUInt32(); var last = reader.ReadUInt32(); var protection = reader.ReadUInt32();
        var cohort = reader.ReadUInt32(); var cohortBytes = reader.ReadUInt32(); Finish(reader);
        var observation = new NativeSourceBookSoundOperandEvent(call, direction, action, caller, address, extent, requested, output,
            before, after, result, last, protection, field.Source.RuntimeSha256, source.Sha256, _nvseHostSource!.StackIdentity, source.Family);
        _bookSoundEvents.Add(observation);
        try
        {
            source.Check(_nvseModuleDeclarations[plugin.Module]);
            if (!_process.ReadModuleModelWriter(plugin.Image, source.ImageBytes, source.WriterRva).AsSpan().SequenceEqual(source.MappedWriterCode(plugin.Image)))
                throw new NotSupportedException("Book pointer helper changed its current original instructions/imports.");
            if (!_process.ReadModuleBookSoundPair(plugin.Image, source.ImageBytes, checked(source.ForwardCallRva - 12)).AsSpan().SequenceEqual(source.MappedLoadPairCode(plugin.Image)))
                throw new NotSupportedException("Book pointer pair changed its actual Load calls/target operands/shared data pointer.");
            if (!ReferenceEquals(plugin, _nvsePlugin) || plugin.Phase != NativeNvsePhase.Loading ||
                _operation != nameof(NativePluginDomainOperation.NvseLoad) || slot.Lease == 0 || address != field.Source.Operand ||
                extent != 4 || action > 3 || output == 0)
                throw new NotSupportedException("Book pointer mutation escaped its exact original Load/module/input extent.");
            if (action is 0 or 1)
            {
                if (caller != checked(plugin.Image + source.ProtectReturnRva) || requested != 0x40)
                    throw new NotSupportedException("Book first SDK call changed its actual helper/request.");
            }
            else if (caller != checked(plugin.Image + source.RestoreReturnRva) || output != slot.Output)
                throw new NotSupportedException("Book second SDK call changed its actual helper/output cell.");
            if (action == 0)
            {
                if (cohort != 0 || cohortBytes != 0 || result != 0 || after != before || protection != slot.Protection ||
                    direction == FalloutSpecialBookPageDirection.Backward && _bookSoundOperands[FalloutSpecialBookPageDirection.Forward].Field.Phase != FalloutSourceCStringInputPhase.Returned)
                    throw new InvalidDataException("Book protection entry invented a previous direction return or SDK/page result.");
                slot.Output = output; field.EnterProtect(this, call, ReadBookSoundInstruction(slot), before);
            }
            else if (action == 1)
            {
                if (output != slot.Output) throw new InvalidDataException("Book first SDK result changed its original output cell.");
                ApplyBookSoundProtection(slot, result, protection, cohort, cohortBytes, after, requested);
                field.Protected(this, call, result, last, before, after);
                if (!ReadBookSoundInstruction(slot).AsSpan().SequenceEqual(field.Source.Preimage.AsSpan()))
                    throw new InvalidDataException("Book pointer changed before its original DWORD store.");
            }
            else if (action == 2)
            {
                if (result != 0 || before != after || protection != slot.Protection || cohort != 0 || cohortBytes != 0)
                    throw new InvalidDataException("Book DWORD observation invented a protection result.");
                var buffer = ReadBookSoundBuffer();
                if (!buffer.AsSpan().SequenceEqual(source.InitialBytes.AsSpan())) throw new NotSupportedException("Book module CString has an unowned source writer.");
                field.ObserveStore(this, call, ReadBookSoundInstruction(slot), checked(plugin.Image + source.BufferRva), FalloutExecutableStringTable.ReadSoundEditorIdBytes(buffer));
            }
            else
            {
                ApplyBookSoundProtection(slot, result, protection, cohort, cohortBytes, after, requested);
                field.Restored(this, call, requested, result, last, before, after);
                var instruction = ReadBookSoundInstruction(slot);
                if (instruction[0] != 0x68 || BinaryPrimitives.ReadUInt32LittleEndian(instruction.AsSpan(1)) != field.Pointer)
                    throw new InvalidDataException("Book restoration lost its actual already-stored pointer.");
            }
            return 1;
        }
        catch (Exception failure)
        { _bookSoundEvents[^1] = observation with { Failure = failure.Message }; field.Fail(this, call, failure.Message); throw; }
    }
    private void ApplyBookSoundProtection(BookSoundOperand slot, uint result, uint protection, uint cohort, uint cohortBytes, uint old, uint requested)
    {
        if (result == 0)
        {
            if (cohort != 0 || cohortBytes != 0 || protection != slot.Protection)
                throw new InvalidDataException("Failed Book SDK call invented a committed page transition.");
            return;
        }
        var reservation = SourceAddressSpaceReceipt!; var end = (ulong)cohort + cohortBytes;
        if (cohortBytes == 0 || cohort < reservation.ImageBase || end > (ulong)reservation.ImageBase + reservation.ReservedBytes ||
            slot.Field.Source.Operand < cohort || (ulong)slot.Field.Source.Operand + 4 > end || old != slot.Protection ||
            protection != requested || protection is not (0x20 or 0x40))
            throw new InvalidDataException("Book protection changed its actual old access or SDK page cohort.");
        PublishPointerProtectionCohort(cohort, cohortBytes, protection);
    }
    private void PublishPointerProtectionCohort(uint cohort, uint cohortBytes, uint protection)
    {
        var end = (ulong)cohort + cohortBytes;
        foreach (var slot in _bookSoundOperands.Values)
            if ((ulong)slot.Field.Source.Instruction < end && (ulong)slot.Field.Source.Instruction + 5 > cohort) slot.Protection = protection;
        if (_raceMenuModelPath is { } model && (ulong)model.Source.Instruction < end && (ulong)model.Source.Instruction + 5 > cohort)
            _raceMenuProtection = protection;
        foreach (var site in _sourceCallSites.Values)
        {
            if ((ulong)site.Source.Address >= end || (ulong)site.Source.Address + (uint)site.Extent <= cohort) continue;
            site.Protection = protection;
            if (ReadSourceCallSiteCode(site, RetainedNvseModule(site.Module), site.DefaultTarget, protection) != site.Code)
                throw new InvalidDataException("A neighboring source CALL changed during pointer input protection.");
        }
    }
    private byte[] ReadBookSoundInstruction(BookSoundOperand slot)
    {
        var host = _nvseHostSource!;
        return _process.ReadSourceModelOperand(slot.Field.Source.Instruction, host.SourceAddressSpace.ImageBase, host.SourceAddressSpace.ImageBytes, slot.Protection);
    }
    private byte[] ReadBookSoundBuffer()
    {
        var source = _bookSoundSource ?? throw new InvalidDataException("Book CString has no retained producer.");
        var module = _bookSoundModule ?? throw new InvalidDataException("Book CString has no actual module.");
        return _process.ReadModuleModelBuffer(module.Image, source.ImageBytes, source.BufferRva, source.Capacity);
    }
    private string ReadNativeBookSound(FalloutSpecialBookPageDirection direction)
    {
        var slot = _bookSoundOperands[direction]; var field = slot.Field;
        if (_bookSoundSource is null) return field.Source.Value;
        var instruction = ReadBookSoundInstruction(slot);
        if (instruction[0] != 0x68 || BinaryPrimitives.ReadUInt32LittleEndian(instruction.AsSpan(1)) != field.Pointer)
            throw new InvalidDataException("Book pointer changed outside its retained original store history.");
        if (field.Pointer == field.Source.Pointer) return field.Source.Value;
        var buffer = ReadBookSoundBuffer();
        if (!buffer.AsSpan().SequenceEqual(_bookSoundSource.InitialBytes.AsSpan())) throw new NotSupportedException("Book module CString needs its actual current-copy writer.");
        return FalloutExecutableStringTable.ReadSoundEditorIdBytes(buffer);
    }
    private void RetireNvseSpecialBookPageSounds(NativeNvsePlugin plugin)
    {
        if (!ReferenceEquals(plugin, _bookSoundModule)) return;
        _bookPageSounds!.RequireRetirement(this);
        foreach (var direction in new[] { FalloutSpecialBookPageDirection.Backward, FalloutSpecialBookPageDirection.Forward })
        {
            var slot = _bookSoundOperands[direction]; if (slot.Lease == 0) continue;
            using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer =>
            { writer.Write(7U); writer.Write((uint)direction); writer.Write(plugin.Module); writer.Write(slot.Lease); }));
            var retired = reader.ReadUInt32(); var call = reader.ReadUInt64(); Finish(reader);
            if (retired != 1 || ReadBookSoundInstruction(slot).Any(value => value != 0xcc))
                throw new InvalidDataException("Book input retirement lost its native traps/module-data release.");
            slot.Field.Retired(this, new(Generation, ProcessId, plugin.Module, NativeThread, call, 0)); slot.Lease = 0;
        }
    }
    private void ClearBookSoundsAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Book data leases require actual original child closure.");
        _bookPageSounds?.ClosedAfterChildExit(this);
        foreach (var slot in _bookSoundOperands.Values) slot.Lease = 0;
    }
    private void FailBookSoundCall(NativeNvsePlugin plugin, Exception failure)
    {
        if (!ReferenceEquals(plugin, _bookSoundModule)) return;
        foreach (var pair in _bookSoundOperands)
        {
            if (pair.Value.Field.Phase is not (FalloutSourceCStringInputPhase.ProtectEntered or FalloutSourceCStringInputPhase.ProtectReturned or FalloutSourceCStringInputPhase.Stored)) continue;
            var retained = _bookSoundEvents.LastOrDefault(row => row.Direction == pair.Key);
            if (retained is not null) pair.Value.Field.Fail(this, retained.Invocation, failure.Message);
        }
    }
}
