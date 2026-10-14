using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceInitialPlacementCells
{
    private static FalloutInitialPlacementCellCall CloneCall(FalloutInitialPlacementCellCall call) =>
        call with { Stores = Array.AsReadOnly(call.Stores.ToArray()) };
    internal FalloutInitialPlacementCellSnapshot Capture()
    {
        if (_current is not null) { _fault++; throw new InvalidOperationException("Entered TES cells cannot be captured."); }
        var saved = new FalloutInitialPlacementCellSnapshot(Schema, Source, Stack, Process, Factory, Constructor, _sequence, _currentCell,
            _diameter, Array.AsReadOnly(_slots.ToArray()), Array.AsReadOnly(_publications.ToArray()),
            Array.AsReadOnly(_calls.Select(CloneCall).ToArray()), new Dictionary<FalloutInitialPlacementCellLane, string>(_writers), _retired);
        Validate(saved, Source, Stack); return saved;
    }
    internal void Retire()
    { if (_current is not null) { _fault++; throw new InvalidOperationException("Entered TES cells cannot retire."); } _retired = true; }
    internal static void Validate(FalloutInitialPlacementCellSnapshot saved, FalloutInitialPlacementCellsSource source, string stack)
    {
        source.Validate();
        if (saved.Schema != Schema || saved.Source != source || saved.Stack != stack || saved.Sequence < 0 ||
            saved.Publications is not { Count: > 0 } || saved.Calls is null || saved.Slots is null || saved.Writers is null)
            throw new InvalidDataException("TES cells changed their actual source or cold schema.");
        var owners = new Dictionary<Guid, (int Index, FalloutReferencePositionPublication Owner)>();
        var factories = new HashSet<Guid>(); var constructors = new HashSet<Guid>(); FalloutReferencePositionPublication? previous = null;
        foreach (var owner in saved.Publications)
        {
            if (owner.Process == Guid.Empty || owner.Factory == Guid.Empty || owner.Constructor == Guid.Empty ||
                !factories.Add(owner.Factory) || !constructors.Add(owner.Constructor) || !owners.TryAdd(owner.Process, (owners.Count, owner)) ||
                (previous is null ? owner.PreviousProcess is not null || owner.PreviousFactory is not null :
                    owner.PreviousProcess != previous.Process || owner.PreviousFactory != previous.Factory))
                throw new InvalidDataException("TES cells reused their actual field constructor lineage.");
            previous = owner;
        }
        if (previous!.Process != saved.Process || previous.Factory != saved.Factory || previous.Constructor != saved.Constructor)
            throw new InvalidDataException("TES cells omitted their current fresh field owner.");
        var writers = new Dictionary<FalloutInitialPlacementCellLane, string> { [FalloutInitialPlacementCellLane.Grid] = GridBoundary };
        FalloutFormKey? current = null; uint diameter = 0; FalloutFormKey?[] slots = [];
        long sequence = 0; var last = -1; var threads = new Dictionary<Guid, int>(); var constructed = false;
        foreach (var call in saved.Calls)
        {
            var invocation = call.Invocation; invocation.ValidatePackageCaller(); invocation.Actor.Validate();
            if (call.Source != source.Identity || !owners.TryGetValue(call.Process, out var publication) || call.Factory != publication.Owner.Factory ||
                call.Entered != ++sequence || invocation.Call == Guid.Empty || invocation.ReferenceProcess == Guid.Empty || invocation.ReferenceConstructor == Guid.Empty ||
                invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 || invocation.ThreadId < 1 || call.Stores is null ||
                !Enum.IsDefined(call.Step) || call.Returned != (call.Failure is null) || call.Failure is { Length: 0 } ||
                call.Returned && call.Step != FalloutInitialPlacementCellStep.Returned || publication.Index < last ||
                threads.TryGetValue(call.Process, out var thread) && thread != invocation.ThreadId)
                throw new InvalidDataException("TES cell history lost its original caller or failed store prefix.");
            FalloutInitialPlacementCellStep[] expected = call.Kind switch
            {
                "original-grid-constructor" => [FalloutInitialPlacementCellStep.Diameter, FalloutInitialPlacementCellStep.Slots],
                "actual-current-TES-CELL-store" => [FalloutInitialPlacementCellStep.CurrentCell],
                "actual-current-grid-CELL-store" => [FalloutInitialPlacementCellStep.GridSlot],
                "initial-placement-selected-CELL" or "observed-current-TES-field-writer" => [],
                _ => throw new InvalidDataException("TES cells contain an unknown field operation.")
            };
            if (call.Stores.Count > expected.Length || !call.Stores.Select(value => value.Step).SequenceEqual(expected.Take(call.Stores.Count)) ||
                call.Returned && call.Stores.Count != expected.Length)
                throw new InvalidDataException("TES field history changed its original store order.");
            if (call.Kind == "original-grid-constructor")
            {
                if (constructed || call.Returned && call.Setting is null) throw new InvalidDataException("TES grid replayed its original constructor.");
                constructed = true;
                if (call.Setting is { } setting)
                {
                    var input = GridValue(setting); var normalized = FalloutInitialPlacementCellsSource.NormalizeGridDiameter(input);
                    if (call.Returned && call.SettingStored != (input != normalized) || call.SettingStored && input == normalized ||
                        call.Stores.Count != 0 && (input != normalized && !call.SettingStored || call.Stores[0].Bits != normalized))
                        throw new InvalidDataException("TES grid omitted its actual Main INI normalization store.");
                }
            }
            else if (call.Setting is not null || call.SettingStored)
                throw new InvalidDataException("TES field operation borrowed another constructor's setting.");
            foreach (var store in call.Stores)
            {
                if (store.Changed != ++sequence) throw new InvalidDataException("TES cells reordered an actual field store.");
                switch (store.Step)
                {
                    case FalloutInitialPlacementCellStep.Diameter:
                        if (store.Bits is not { } size || size < 5 || (size & 1) == 0 || store.Cell is not null || store.Index is not null)
                            throw new InvalidDataException("TES grid constructor changed its normalized diameter.");
                        diameter = size; break;
                    case FalloutInitialPlacementCellStep.Slots:
                        var length = checked((int)checked((ulong)diameter * diameter));
                        if (store.Bits != checked((uint)length) || store.Cell is not null || store.Index is not null)
                            throw new InvalidDataException("TES grid constructor changed its actual zero-slot allocation.");
                        slots = new FalloutFormKey?[length]; break;
                    case FalloutInitialPlacementCellStep.CurrentCell:
                        if (store.Bits is not null || store.Index is not null) throw new InvalidDataException("TES CELL store borrowed a grid scalar.");
                        current = store.Cell; writers.Remove(FalloutInitialPlacementCellLane.CurrentCell); break;
                    case FalloutInitialPlacementCellStep.GridSlot:
                        if (writers.ContainsKey(FalloutInitialPlacementCellLane.Grid) || store.Bits is not null || store.Index is not { } index || index < 0 || index >= slots.Length)
                            throw new InvalidDataException("TES grid slot store bypassed its current geometry owner.");
                        slots[index] = store.Cell; break;
                }
            }
            if (call.Kind == "original-grid-constructor" && call.Returned) writers.Remove(FalloutInitialPlacementCellLane.Grid);
            if (call.Invalidated is { } invalidated)
            {
                if (call.Kind != "observed-current-TES-field-writer" || !Enum.IsDefined(invalidated) || string.IsNullOrWhiteSpace(call.Writer))
                    throw new InvalidDataException("TES field invalidation lost its independent writer lane.");
                writers[invalidated] = call.Writer;
            }
            else if (call.Writer is not null || call.Kind == "observed-current-TES-field-writer")
                throw new InvalidDataException("TES field history omitted its actual current writer.");
            if (call.Read is { } read)
            {
                var slot = current is null ? checked((int)((diameter >> 1) * diameter + (diameter >> 1))) : (int?)null;
                if (call.Kind != "initial-placement-selected-CELL" || read.Source != source.Identity || read.Stack != stack ||
                    read.Process != call.Process || read.Factory != call.Factory || read.Constructor != publication.Owner.Constructor || read.Sequence != call.Entered ||
                    writers.ContainsKey(FalloutInitialPlacementCellLane.CurrentCell) || current is null && writers.ContainsKey(FalloutInitialPlacementCellLane.Grid) ||
                    read.CurrentCell != current || read.Diameter != (current is null ? diameter : (uint?)null) || read.Slot != slot ||
                    read.Cell != (slot is { } selected ? slots[selected] : current))
                    throw new InvalidDataException("TES selector substituted Player ParentCELL or pending travel for current fields.");
            }
            else if (call.Kind == "initial-placement-selected-CELL" && call.Returned)
                throw new InvalidDataException("TES selection returned without its actual current-field read.");
            if (call.Changed != ++sequence) throw new InvalidDataException("TES cell history lost its complete failed extent.");
            threads.TryAdd(call.Process, invocation.ThreadId); last = publication.Index;
        }
        if (saved.Sequence != sequence || saved.Diameter != diameter || saved.CurrentCell != current || !saved.Slots.SequenceEqual(slots) ||
            saved.Writers.Count != writers.Count || writers.Any(value => !saved.Writers.TryGetValue(value.Key, out var boundary) || value.Value != boundary))
            throw new InvalidDataException("Current TES fields differ from their complete source store history.");
    }
}
