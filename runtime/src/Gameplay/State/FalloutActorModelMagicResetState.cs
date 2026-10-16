using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerModelMagicRead(string Owner, FalloutFormKey Form, string Type,
    uint? Kind, int? Slot = null, Guid? Temporary = null, bool TemporaryRetired = false);
internal sealed record FalloutPlayerModelMagicReset(FalloutReferenceModelDataInvocation Load,
    Guid Factory, long Entered, long Changed, long InventoryRevision,
    IReadOnlyList<FalloutPlayerModelMagicRead> Reads, IReadOnlyList<FalloutPlayerModelLoadKindScope> Slots,
    bool Returned, string? Failure);

// This helper borrows the already loaded forms and SAME inventory. It owns
// only the actual reset call and its temporary entry leases, never a second
// spell list, equipped set or effect owner.
internal sealed class FalloutActorModelMagicResetState
{
    private readonly FalloutPlayerModelLoadContinuationSource _source;
    private readonly FalloutPluginStack _records;
    private readonly FalloutPlayerInventory _inventory;
    private readonly FalloutPlayerThreadContextState _thread;
    private readonly Action<FalloutReferenceModelDataInvocation> _requireActual;
    private readonly Action<FalloutPlayerInventory> _requireInventory;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private long _sequence, _reentry;
    private bool _busy;
    private string? _writer, _failure;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal string SourceIdentity => _source.Identity;
    internal FalloutPlayerModelMagicReset? LastCall { get; private set; }
    internal string? SaveBlocker => _busy ? "source-model-magic-reset-entered" : _writer ?? _failure;
    internal FalloutActorModelMagicResetState(FalloutPlayerModelLoadContinuationSource source,
        FalloutPluginStack records, FalloutPlayerInventory inventory, FalloutPlayerThreadContextState thread,
        Action<FalloutReferenceModelDataInvocation> requireActual, Action<FalloutPlayerInventory> requireInventory)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(thread); ArgumentNullException.ThrowIfNull(requireActual);
        ArgumentNullException.ThrowIfNull(requireInventory);
        records.RequireLiveLoadReceipt(records.LoadReceipt);
        if (thread.Source.Main != source.Animation.Tail.Scene.Main)
            throw new InvalidDataException("Model magic reset changed its actual source records or thread owner.");
        _source = source; _records = records; _inventory = inventory; _thread = thread;
        _requireActual = requireActual; _requireInventory = requireInventory;
    }
    internal FalloutPlayerModelMagicReset Reset(FalloutReferenceModelDataInvocation load,
        Func<FalloutReferenceModelDataInvocation, FalloutLoadedAnimationBaseRead> readBase,
        Func<FalloutReferenceModelDataInvocation, FalloutPluginRecord?> readRace)
    {
        if (_busy) { ++_reentry; throw new InvalidOperationException("Actor model magic reset reentered its source call."); }
        Check(); var faults = _reentry; var inventoryRevision = _inventory.Revision;
        var reads = new List<FalloutPlayerModelMagicRead>(); var slots = new List<FalloutPlayerModelLoadKindScope>();
        var entered = Next(); _busy = true; string? failure = null;
        try
        {
            var basis = readBase(load); CheckCall();
            if (!basis.Assignment.Returned || basis.Assignment.After != basis.Record.FormKey ||
                !basis.Assignment.PointerStored || basis.Assignment.Failure is not null ||
                basis.Assignment.Invocation.Actor != load.Actor || basis.Assignment.Invocation.Epoch != load.Epoch ||
                basis.Assignment.Invocation.ReferenceProcess != load.ReferenceProcess ||
                basis.Assignment.Invocation.ReferenceConstructor != load.ReferenceConstructor ||
                basis.Assignment.Invocation.CommonProcess != load.CommonProcess || basis.Record.Signature != "NPC_" ||
                !ReferenceEquals(_records.GetEffective(basis.Record.FormKey), basis.Record))
                throw new InvalidOperationException("Magic reset did not read the actual current assigned NPC base.");
            ReadSpellList(basis.Record, "base");
            // The source invokes the virtual twice on a nonnull first result.
            var race = readRace(load); CheckCall();
            if (race is not null)
            {
                race = readRace(load); CheckCall();
                if (race is null || race.Signature != "RACE" || !ReferenceEquals(_records.GetEffective(race.FormKey), race))
                    throw new InvalidOperationException("Repeated current race read has no actual loaded race form.");
                ReadSpellList(race, "race");
            }
            for (var slot = 0; slot < 5; ++slot)
            {
                var scope = _thread.EnterPlayerLoadWornEntry(load, Guid.NewGuid(), Factory, _requireActual);
                try
                {
                    CheckCall(); var item = _inventory.ReadModelLoadWornEntry(_records, slot); CheckCall();
                    scope = _thread.ReturnPlayerModelLoadKind(scope, _requireActual); slots.Add(scope);
                    if (item is null) continue;
                    var temporary = Guid.NewGuid(); var record = _records.GetEffective(item.FormKey);
                    var at = reads.Count; reads.Add(new("worn", item.FormKey, record.Signature, null, slot, temporary));
                    try
                    {
                        if (record.Signature is not ("ARMO" or "WEAP")) continue;
                        var enchantment = OptionalForm(record, "EITM");
                        if (enchantment is { } form)
                        {
                            var actual = _records.GetEffective(form);
                            if (actual.Signature != "ENCH") throw new InvalidDataException("Current worn enchantment has another source form class.");
                            reads.Add(new("worn-enchantment", form, actual.Signature, null, slot, temporary));
                            throw new NotSupportedException("source-model-magic-effect-visual-reset-child-unowned:" + form);
                        }
                    }
                    finally { reads[at] = reads[at] with { TemporaryRetired = true }; }
                }
                catch (Exception error)
                {
                    if (!scope.Scope.Returned) slots.Add(_thread.RetainPlayerModelLoadKindFailure(scope, error));
                    throw;
                }
            }
            var reserved = _records.RuntimeFormKey(0x13);
            if (_records.TryGetEffective(reserved, out var reservedForm))
            {
                reads.Add(new("reserved-cast", reservedForm.FormKey, reservedForm.Signature, null));
                if (reservedForm.Signature == "SPEL") ReadSpell(reservedForm, "reserved");
            }
            CheckCall();
        }
        catch (Exception error) { _failure ??= failure = error.Message; throw; }
        finally
        {
            LastCall = new(load, Factory, entered, Next(), inventoryRevision, reads.ToArray(), slots.ToArray(), failure is null, failure);
            _busy = false;
        }
        return LastCall!;

        void Check()
        {
            if (_ownerThread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Model magic reset changed its actual source thread.");
            _requireActual(load); _requireInventory(_inventory);
            _records.RequireLiveLoadReceipt(_records.LoadReceipt);
            if (_writer is { } writer) throw new NotSupportedException(writer);
            if (_failure is { } retained) throw new NotSupportedException(retained);
        }
        void CheckCall()
        {
            Check();
            if (_reentry != faults) throw new InvalidOperationException("Model magic callback swallowed actual reentry.");
            if (_inventory.Revision != inventoryRevision) throw new InvalidOperationException("Model magic reset changed its actual current inventory during a borrowed read.");
        }
        void ReadSpellList(FalloutPluginRecord owner, string role)
        {
            foreach (var row in owner.ReadSubrecords().Where(row => row.Signature == "SPLO"))
            {
                if (row.Data.Length != 4) throw new InvalidDataException("Current magic list has an invalid source extent.");
                var form = owner.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(row.Data.Span));
                if (form is null) continue;
                var spell = _records.GetEffective(form.Value);
                if (spell.Signature != "SPEL") throw new InvalidDataException("Current magic list member is not its actual SpellItem.");
                ReadSpell(spell, role);
            }
        }
        void ReadSpell(FalloutPluginRecord spell, string role)
        {
            CheckCall();
            var data = spell.ReadSubrecords().Where(row => row.Signature == "SPIT").ToArray() is [var declaration] && declaration.Data.Length == 16 ?
                declaration.Data : throw new InvalidDataException("Current spell has no exact source kind declaration.");
            var kind = BinaryPrimitives.ReadUInt32LittleEndian(data.Span); reads.Add(new(role, spell.FormKey, spell.Signature, kind));
            if (kind is 1 or 4 or 10) throw new NotSupportedException("source-model-magic-effect-visual-reset-child-unowned:" + spell.FormKey);
        }
    }
    internal void ObserveEnteredWriter(string writer, Action requireActual)
    {
        if (_busy) { ++_reentry; throw new InvalidOperationException("Magic-list writer reentered the borrowed model reset."); }
        requireActual(); ArgumentException.ThrowIfNullOrWhiteSpace(writer); _writer ??= writer; Next();
    }
    private static FalloutFormKey? OptionalForm(FalloutPluginRecord record, string signature)
    {
        var rows = record.ReadSubrecords().Where(row => row.Signature == signature).ToArray();
        if (rows.Length == 0) return null;
        if (rows is not [var field] || field.Data.Length != 4) throw new InvalidDataException("Current magic link has an invalid source extent: " + signature);
        return record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
    }
    private long Next() => _sequence = checked(_sequence + 1);
}
