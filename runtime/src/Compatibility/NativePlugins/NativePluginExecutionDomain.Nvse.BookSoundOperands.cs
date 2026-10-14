using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativeSourceBookSoundOperandEvent(FalloutSourceCStringInputInvocation Invocation,
    FalloutSpecialBookSoundInput Direction, uint Action, uint Caller, uint Address, uint Extent, uint Requested,
    uint Output, uint OldBefore, uint OldAfter, uint SdkResult, uint LastError, uint Protection,
    string RuntimeSha256, string ModuleSha256, string StackSha256, string Family, string? Failure = null);

internal sealed partial class NativePluginExecutionDomain
{
    private sealed class BookSoundOperand(FalloutSourceCStringInput field)
    {
        internal readonly FalloutSourceCStringInput Field = field;
        internal NativePluginModuleBookSoundSource? Source;
        internal ulong Lease;
        internal uint Protection = 0x20, Output;
    }
    private FalloutSpecialBookPageSounds? _bookPageSounds;
    private NativePluginModuleBookSoundSource? _bookSoundSource;
    private NativeNvsePlugin? _bookSoundModule;
    private readonly Dictionary<FalloutSpecialBookSoundInput, BookSoundOperand> _bookSoundOperands = [];
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
        _bookSoundOperands.Add(FalloutSpecialBookSoundInput.PageForward, new(fields.Field(FalloutSpecialBookPageDirection.Forward)));
        _bookSoundOperands.Add(FalloutSpecialBookSoundInput.PageBackward, new(fields.Field(FalloutSpecialBookPageDirection.Backward)));
        if (fields.FocusSource is { } focus)
        {
            focus.CheckSame(FalloutSpecialBookFocusSoundsSource.Read(host.RuntimePath));
            _bookSoundOperands.Add(FalloutSpecialBookSoundInput.FocusPrevious, new(fields.FocusField(FalloutSpecialBookFocusDirection.Previous)));
            _bookSoundOperands.Add(FalloutSpecialBookSoundInput.FocusNext, new(fields.FocusField(FalloutSpecialBookFocusDirection.Next)));
        }
        fields.Bind(this, RequireBookSoundCurrent, direction => ReadNativeBookSound(direction == FalloutSpecialBookPageDirection.Forward
            ? FalloutSpecialBookSoundInput.PageForward : FalloutSpecialBookSoundInput.PageBackward),
            direction => ReadNativeBookSound(direction == FalloutSpecialBookFocusDirection.Previous ? FalloutSpecialBookSoundInput.FocusPrevious : FalloutSpecialBookSoundInput.FocusNext));
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
        var focus = fields.FocusSource is { } selectedFocus ? NativePluginModuleBookSoundSource.ReadFocus(_nvseModuleDeclarations[plugin.Module], fields.Source, selectedFocus) : null;
        if (focus is not null && (focus.WriterRva != source.WriterRva || focus.LoadRva != source.LoadRva || focus.FirstCallRva != source.SecondCallRva + 17))
            throw new InvalidDataException("Book focus changed its actual immediate page-pair/helper predecessor.");
        _bookSoundSource = source; _bookSoundModule = plugin;
        _bookSoundOperands[FalloutSpecialBookSoundInput.PageForward].Source = source;
        _bookSoundOperands[FalloutSpecialBookSoundInput.PageBackward].Source = source;
        if (focus is not null)
        {
            _bookSoundOperands[FalloutSpecialBookSoundInput.FocusPrevious].Source = focus;
            _bookSoundOperands[FalloutSpecialBookSoundInput.FocusNext].Source = focus;
        }
        try
        {
            foreach (var pair in _bookSoundOperands.OrderBy(pair => pair.Key))
            {
                var slot = pair.Value; if (slot.Source is not { } actual) continue;
                var input = slot.Field.Source;
                var first = pair.Key is FalloutSpecialBookSoundInput.PageForward or FalloutSpecialBookSoundInput.FocusPrevious;
                using var reader = Exchange(NativePluginDomainOperation.NvseModuleSet, Payload(writer =>
                {
                    writer.Write(6U); writer.Write((uint)pair.Key); writer.Write(plugin.Module); writer.Write(input.Instruction); writer.Write(input.Pointer);
                    writer.Write(actual.LoadRva); writer.Write(actual.ProtectReturnRva); writer.Write(actual.RestoreReturnRva);
                    writer.Write(checked((first ? actual.FirstCallRva : actual.SecondCallRva) + 5));
                    writer.Write(actual.BufferRva); writer.Write(actual.Capacity); writer.Write(actual.InitialBytes.AsSpan());
                }));
                slot.Lease = reader.ReadUInt64(); var instruction = reader.ReadUInt32(); var pointer = reader.ReadUInt32();
                slot.Protection = reader.ReadUInt32(); Finish(reader);
                if (slot.Lease == 0 || instruction != input.Instruction || pointer != checked(plugin.Image + actual.BufferRva) || slot.Protection != 0x20 ||
                    !ReadBookSoundInstruction(slot).AsSpan().SequenceEqual(input.Preimage.AsSpan()) ||
                    !ReadBookSoundBuffer(slot).AsSpan().SequenceEqual(actual.InitialBytes.AsSpan()))
                    throw new InvalidDataException("Book input publication lost its independent source/module data/image lease.");
            }
        }
        catch (Exception failure) { throw Fatal(failure); }
    }
    private uint DispatchNvseBookSoundOperand(Frame frame, ulong parent, BinaryReader reader)
    {
        var direction = (FalloutSpecialBookSoundInput)reader.ReadUInt32();
        if (!_bookSoundOperands.TryGetValue(direction, out var slot)) throw new InvalidDataException("Book callback has no actual typed direction field.");
        var plugin = _bookSoundModule ?? throw new InvalidDataException("Book callback lost its retained original module.");
        var source = slot.Source ?? throw new InvalidDataException("Book callback has no entered source producer."); var field = slot.Field;
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
                    !BookPredecessorReturned(direction))
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
                var buffer = ReadBookSoundBuffer(slot);
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
    private bool BookPredecessorReturned(FalloutSpecialBookSoundInput input)
    {
        if (input == FalloutSpecialBookSoundInput.PageForward) return true;
        var previous = (FalloutSpecialBookSoundInput)checked((uint)input - 1);
        return _bookSoundOperands.TryGetValue(previous, out var slot) && slot.Lease != 0 && slot.Field.Phase == FalloutSourceCStringInputPhase.Returned;
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
    private byte[] ReadBookSoundBuffer(BookSoundOperand slot)
    {
        var source = slot.Source ?? throw new InvalidDataException("Book CString has no retained producer.");
        var module = _bookSoundModule ?? throw new InvalidDataException("Book CString has no actual module.");
        return _process.ReadModuleModelBuffer(module.Image, source.ImageBytes, source.BufferRva, source.Capacity);
    }
    private string ReadNativeBookSound(FalloutSpecialBookSoundInput direction)
    {
        var slot = _bookSoundOperands[direction]; var field = slot.Field;
        if (slot.Source is not { } source) return field.Source.Value;
        var instruction = ReadBookSoundInstruction(slot);
        if (instruction[0] != 0x68 || BinaryPrimitives.ReadUInt32LittleEndian(instruction.AsSpan(1)) != field.Pointer)
            throw new InvalidDataException("Book pointer changed outside its retained original store history.");
        if (field.Pointer == field.Source.Pointer) return field.Source.Value;
        var buffer = ReadBookSoundBuffer(slot);
        if (!buffer.AsSpan().SequenceEqual(source.InitialBytes.AsSpan())) throw new NotSupportedException("Book module CString needs its actual current-copy writer.");
        return FalloutExecutableStringTable.ReadSoundEditorIdBytes(buffer);
    }
    private void RetireNvseSpecialBookPageSounds(NativeNvsePlugin plugin)
    {
        if (!ReferenceEquals(plugin, _bookSoundModule)) return;
        _bookPageSounds!.RequireRetirement(this);
        foreach (var direction in _bookSoundOperands.Keys.OrderDescending())
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
