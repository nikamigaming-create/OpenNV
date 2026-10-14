using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceReferencePositionState
{
    private IReadOnlyList<FalloutReferencePositionDependency> ReadPrefixDependencies() => ReadPrefixDependencies(_records, Actor);
    private static IReadOnlyList<FalloutReferencePositionDependency> ReadPrefixDependencies(FalloutPluginStack records,
        FalloutCombatActorIdentity actor)
    {
        var dependencies = new List<FalloutReferencePositionDependency>();
        var seen = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        var basis = records.GetEffective(actor.Base);
        if (basis.Signature != "NPC_" || basis.FormKey != actor.Base || basis.Flags != actor.BaseFlags || Digest(basis) != actor.BaseSha256)
            throw new InvalidDataException("Reference prefix changed its actual winning NPC base.");
        var fields = basis.ReadSubrecords().ToArray(); Add(basis);
        NoCallbacks(fields, "NPC");
        var raceField = fields.Where(value => value.Signature == "RNAM").ToArray() is [var race] && race.Data.Length == 4 ? race :
            throw new InvalidDataException("Reference prefix has no exact winning NPC race link.");
        var raceRecord = records.GetEffective(basis.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(raceField.Data.Span)));
        if (raceRecord.Signature != "RACE") throw new InvalidDataException("Reference prefix race link changed its source kind.");
        NoCallbacks(raceRecord.ReadSubrecords().ToArray(), "Race"); Add(raceRecord);
        foreach (var field in fields.Where(value => value.Signature == "CNTO"))
        {
            if (field.Data.Length != 8) throw new InvalidDataException("Reference prefix has a malformed original container row.");
            var item = records.GetEffective(basis.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)));
            if (item.ReadSubrecords().Any(value => value.Signature == "SCRI" && (value.Data.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(value.Data.Span) != 0)))
                throw new NotSupportedException("source-reference-prefix-contained-item-Script-callback-writer-unbound");
            Add(item);
        }
        var defaults = records.EffectiveRecords("DOBJ").ToArray() is [var original] ? original :
            throw new NotSupportedException("source-reference-prefix-default-object-table-owner-unbound");
        var table = defaults.ReadSubrecords().Where(value => value.Signature == "DATA").ToArray() is [var data] && data.Data.Length == 84 ? data :
            throw new InvalidDataException("Reference prefix changed its original default-object table extent.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(table.Data.Span[(19 * 4)..]) != 0)
            throw new NotSupportedException("source-reference-prefix-default-ability-callback-writer-unbound");
        Add(defaults); return Array.AsReadOnly(dependencies.ToArray());

        void Add(FalloutPluginRecord record)
        {
            if (record.IsDeleted) throw new InvalidDataException("Reference prefix selected a deleted source dependency.");
            if (seen.Add(record.FormKey))
                dependencies.Add(new(record.FormKey, record.Signature, record.Flags, Digest(record)));
        }
        static void NoCallbacks(IReadOnlyList<FalloutPluginSubrecord> rows, string owner)
        {
            if (rows.Any(value => (value.Signature is "SPLO" or "SCRI" or "TPLT") &&
                (value.Data.Length != 4 || BinaryPrimitives.ReadUInt32LittleEndian(value.Data.Span) != 0)))
                throw new NotSupportedException("source-reference-prefix-" + owner + "-callback-or-template-writer-unbound");
        }
    }
    private static string Digest(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
    private static void ValidateForm(FalloutFormKey? value)
    {
        if (value is { } form && (string.IsNullOrWhiteSpace(form.OwnerPlugin) || form.ObjectId is 0 or > FalloutFormKey.ObjectIdMask))
            throw new InvalidDataException("Reference fields retained an unowned source FormID.");
    }
    private static FalloutReferencePositionValues Apply(FalloutReferencePositionValues before, FalloutReferencePositionStore store)
    {
        uint Bits() => store.Bits ?? throw new InvalidDataException("Reference scalar store omitted its original word.");
        return store.Step switch
        {
            FalloutReferencePositionStep.PositionX => before with { Position = before.Position with { X = Bits() } },
            FalloutReferencePositionStep.PositionY => before with { Position = before.Position with { Y = Bits() } },
            FalloutReferencePositionStep.PositionZ => before with { Position = before.Position with { Z = Bits() } },
            FalloutReferencePositionStep.PositionMode when Bits() <= byte.MaxValue => before with { PositionMode = (byte)Bits() },
            FalloutReferencePositionStep.Scale => before with { Scale = Bits() },
            FalloutReferencePositionStep.ActorYawMode => before with { ActorYawMode = Bits() },
            FalloutReferencePositionStep.ActorAssignmentByte when Bits() == 0 => before with { ActorAssignmentByte = 0 },
            FalloutReferencePositionStep.PersistentCell when store.Present is { } present && (present || store.Form is null) =>
                before with { PersistentCell = store.Form, PersistentCellPresent = present },
            _ => throw new InvalidDataException("Reference history contains an unknown scalar or Extra store.")
        };
    }
    internal FalloutReferencePositionSnapshot Capture()
    {
        if (_current is not null) { _fault++; throw new InvalidOperationException("Entered reference fields cannot be captured."); }
        var saved = new FalloutReferencePositionSnapshot(Schema, Source, Stack, Actor, Process, Factory, Constructor,
            _sequence, _values, Array.AsReadOnly(_publications.ToArray()), Array.AsReadOnly(_calls.Select(CloneCall).ToArray()),
            new Dictionary<FalloutReferencePositionLane, string>(_writers), _retired,
            Array.AsReadOnly(_frameReceipts.Runs.Select(ClonePositionFrames).ToArray()));
        Validate(saved, Source, Stack, Actor); return saved;
    }
    internal void Retire()
    {
        if (_current is not null) { _fault++; throw new InvalidOperationException("Entered reference fields cannot retire."); }
        _retired = true;
    }
    internal static void ValidateOwnedSources(FalloutReferencePositionSnapshot saved, FalloutPluginStack records)
    {
        if (saved.Calls.Any(value => value.Prefix is { Returned: true }) && records.TryGetWinner(saved.Actor.Reference, out _))
            throw new InvalidDataException("Cold reference prefix acquired an original reference-record writer.");
        foreach (var dependency in saved.Calls.Where(value => value.Prefix?.Dependencies is not null).SelectMany(value => value.Prefix!.Dependencies!))
        {
            var record = records.GetEffective(dependency.Form);
            if (record.IsDeleted || record.Signature != dependency.Signature || record.Flags != dependency.Flags || Digest(record) != dependency.Sha256)
                throw new InvalidDataException("Cold reference prefix changed its current winning dependency bytes.");
        }
        foreach (var prefix in saved.Calls.Where(value => value.Prefix is { Returned: true }).Select(value => value.Prefix!))
            if (!ReadPrefixDependencies(records, saved.Actor).SequenceEqual(prefix.Dependencies!))
                throw new InvalidDataException("Cold reference prefix replaced its complete winning callback dependency graph.");
    }
    private static FalloutReferencePositionCall CloneCall(FalloutReferencePositionCall call) => call with
    {
        Stores = Array.AsReadOnly(call.Stores.ToArray()),
        Prefix = call.Prefix is { } prefix ? prefix with
        { Dependencies = prefix.Dependencies is { } rows ? Array.AsReadOnly(rows.ToArray()) : null } : null
    };

    internal static void Validate(FalloutReferencePositionSnapshot saved, FalloutReferencePositionSource source,
        string stack, FalloutCombatActorIdentity actor)
    {
        ArgumentNullException.ThrowIfNull(saved); source.Validate(); actor.Validate();
        if (saved.Schema != Schema || saved.Source != source || saved.Stack != stack || saved.Actor != actor || saved.Sequence < 0 ||
            saved.Process == Guid.Empty || saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty ||
            saved.Publications is not { Count: > 0 } || saved.Calls is null || saved.UnownedWriters is null)
            throw new InvalidDataException("Reference fields changed their actual source, actor or cold schema.");
        var publications = new Dictionary<Guid, (int Ordinal, FalloutReferencePositionPublication Value)>();
        var factories = new HashSet<Guid>(); var constructors = new HashSet<Guid>(); FalloutReferencePositionPublication? previous = null;
        foreach (var publication in saved.Publications)
        {
            if (publication.Process == Guid.Empty || publication.Factory == Guid.Empty || publication.Constructor == Guid.Empty ||
                !factories.Add(publication.Factory) || !constructors.Add(publication.Constructor) ||
                !publications.TryAdd(publication.Process, (publications.Count, publication)) ||
                (previous is null ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != previous.Process || publication.PreviousFactory != previous.Factory))
                throw new InvalidDataException("Reference fields reused or replaced their actual constructor lineage.");
            previous = publication;
        }
        if (previous!.Process != saved.Process || previous.Factory != saved.Factory || previous.Constructor != saved.Constructor)
            throw new InvalidDataException("Reference fields omitted their current fresh owner.");
        var values = new FalloutReferencePositionValues(new(0, 0, 0), 0, FalloutReferencePositionSource.InitialScale, null, false);
        var writers = Enum.GetValues<FalloutReferencePositionLane>().ToDictionary(value => value, _ => PrefixBoundary);
        long sequence = 0; var ids = new HashSet<Guid>(); var threads = new Dictionary<Guid, int>(); var lastPublication = -1;
        var prefixCount = 0; FalloutReferencePositionCall? startup = null; var configuredCount = 0;
        var history = PositionHistory(saved).ToArray();
        foreach (var item in history)
        {
            if (item.Frames is { } frames)
            {
                ValidatePositionFrames(frames, saved, publications, threads, ref lastPublication, ref sequence, ref values, writers);
                continue;
            }
            var call = item.Call!;
            var invocation = call.Invocation;
            invocation.ValidatePackageCaller();
            if (call.Source != source.Identity || invocation.Call == Guid.Empty || !ids.Add(invocation.Call) || invocation.Actor != actor ||
                !publications.TryGetValue(invocation.ReferenceProcess, out var publication) || call.Factory != publication.Value.Factory ||
                invocation.ReferenceConstructor != publication.Value.Constructor || invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 ||
                invocation.ThreadId < 1 || !Enum.IsDefined(invocation.Ingress) || !Enum.IsDefined(call.Step) || call.Entered != ++sequence ||
                call.Before != values || call.Stores is null || call.Returned != (call.Failure is null) || call.Failure is { Length: 0 } ||
                call.Returned && call.Step != FalloutReferencePositionStep.Returned || publication.Ordinal < lastPublication ||
                threads.TryGetValue(invocation.ReferenceProcess, out var thread) && thread != invocation.ThreadId)
                throw new InvalidDataException("Reference fields omitted or reordered an original writer/failed prefix.");
            var expected = call.Kind switch
            {
                "constructed-reference-field-prefix" or "observed-reference-field-writer" => Array.Empty<FalloutReferencePositionStep>(),
                "base-reference-position" or "Actor-reference-position" or "constructor-startup-position" or "configured-startup-position" =>
                    [FalloutReferencePositionStep.PositionX, FalloutReferencePositionStep.PositionY, FalloutReferencePositionStep.PositionZ],
                "raw-adjusted-position-mode-store" => [FalloutReferencePositionStep.PositionMode],
                "raw-reference-scale-store" => [FalloutReferencePositionStep.Scale],
                "raw-Actor-yaw-selection-word-store" => [FalloutReferencePositionStep.ActorYawMode],
                "raw-Actor-assignment-byte-zero-store" => [FalloutReferencePositionStep.ActorAssignmentByte],
                "persistent-CELL-Extra-twelve-store" => [FalloutReferencePositionStep.PersistentCell],
                _ => throw new InvalidDataException("Reference fields contain an unknown writer kind.")
            };
            if (call.Stores.Count > expected.Length || !call.Stores.Select(value => value.Step).SequenceEqual(expected.Take(call.Stores.Count)) ||
                call.Returned && call.Stores.Count != expected.Length || expected.Length == 3 && call.Stores.Count == 1)
                throw new InvalidDataException("Reference writer invented stores or lost its committed scalar prefix.");
            foreach (var store in call.Stores)
            {
                if (store.Changed != ++sequence || store.Bits is not null && (store.Form is not null || store.Present is not null))
                    throw new InvalidDataException("Reference scalar stores changed order or combined unrelated lanes.");
                ValidateForm(store.Form); values = Apply(values, store);
            }
            if (call.Changed != ++sequence || call.After != values) throw new InvalidDataException("Reference current values differ from their original stores.");
            if (call.Stores.Any(value => value.Step == FalloutReferencePositionStep.PositionZ)) writers.Remove(FalloutReferencePositionLane.Position);
            foreach (var (step, lane) in new[] { (FalloutReferencePositionStep.PositionMode, FalloutReferencePositionLane.PositionMode),
                (FalloutReferencePositionStep.Scale, FalloutReferencePositionLane.Scale),
                (FalloutReferencePositionStep.PersistentCell, FalloutReferencePositionLane.PersistentCell),
                (FalloutReferencePositionStep.ActorYawMode, FalloutReferencePositionLane.ActorYawMode),
                (FalloutReferencePositionStep.ActorAssignmentByte, FalloutReferencePositionLane.ActorAssignmentByte) })
                if (call.Stores.Any(value => value.Step == step)) writers.Remove(lane);
            if ((call.Kind == "constructed-reference-field-prefix") != (call.Prefix is not null))
                throw new InvalidDataException("Reference prefix omitted or borrowed its independent source receipt.");
            if (call.Prefix is { } prefix)
            {
                if (++prefixCount != 1 || call.Kind != "constructed-reference-field-prefix" || prefix.Source != source.Identity ||
                    prefix.Factory != call.Factory || prefix.Constructor != invocation.ReferenceConstructor || prefix.Invocation != invocation ||
                    prefix.BaseSha256 != actor.BaseSha256 || prefix.Returned != call.Returned || prefix.Failure != call.Failure ||
                    prefix.Returned && (!actor.EnginePlayer || prefix.ReferenceRecordPresent || prefix.ConfiguredCell != "" || prefix.ConfiguredX != "" ||
                        prefix.Dependencies is not { Count: >= 3 } || saved.Calls.Any(value => value.Entered < call.Entered &&
                            (value.Kind != "constructor-startup-position" || !value.Returned))))
                    throw new InvalidDataException("Reference prefix invented winning-source or configured-start closure.");
                if (prefix.Returned)
                    foreach (var lane in new[] { FalloutReferencePositionLane.PositionMode,
                        FalloutReferencePositionLane.Scale, FalloutReferencePositionLane.PersistentCell, FalloutReferencePositionLane.ActorYawMode,
                        FalloutReferencePositionLane.ActorAssignmentByte, FalloutReferencePositionLane.FollowerExtra, FalloutReferencePositionLane.ReferenceKind }) writers.Remove(lane);
            }
            if (call.InvalidatedLane is { } invalidated)
            {
                if (!Enum.IsDefined(invalidated) || string.IsNullOrWhiteSpace(call.WriterBoundary) || call.Kind != "observed-reference-field-writer")
                    throw new InvalidDataException("Reference invalidation lost its real writer lane.");
                writers[invalidated] = call.WriterBoundary;
            }
            else if (call.WriterBoundary is not null || call.Kind == "observed-reference-field-writer")
                throw new InvalidDataException("Reference fields omitted their actual writer invalidation.");
            if (call.Kind == "constructor-startup-position")
            {
                if (startup is not null || invocation.Ingress != FalloutReferencePositionIngress.ConstructorStartup)
                    throw new InvalidDataException("Reference constructor startup replayed or changed its source caller.");
                startup = call;
                if (call.Stores.Count != 0 && call.Stores.Select(value => value.Bits).SequenceEqual(new uint?[] { 0x45000000, 0x45000000, 0 }.Take(call.Stores.Count)) == false)
                    throw new InvalidDataException("Constructor startup substituted a pending/native position.");
            }
            if (call.Kind == "configured-startup-position" && (++configuredCount != 1 ||
                invocation.Ingress != FalloutReferencePositionIngress.ConfiguredStartup || call.Stores.Count != 0 && startup is not { Returned: true }))
                throw new InvalidDataException("Configured placement crossed an unreturned preceding child or replayed.");
            if (call.Change is { } change && (change.Invocation != invocation || change.Mask != 2 || !change.Returned || change.Source != source.Identity))
                throw new InvalidDataException("Reference setter substituted its mandatory actual mask2 child.");
            if (call.Returned && expected.Length == 3 && call.Change is null)
                throw new InvalidDataException("Reference position returned without its mandatory mask2 callback.");
            threads.TryAdd(invocation.ReferenceProcess, invocation.ThreadId); lastPublication = publication.Ordinal;
        }
        if (sequence != saved.Sequence || values != saved.Values || writers.Count != saved.UnownedWriters.Count ||
            writers.Any(value => !saved.UnownedWriters.TryGetValue(value.Key, out var boundary) || boundary != value.Value))
            throw new InvalidDataException("Reference snapshot changed its current state or writer availability.");
        FalloutReferencePublicationSequence.Validate(saved.Publications.Select(value => (value.Process, value.PublishedSequence)),
            history.Select(value => (value.Process, value.Entered, value.Changed)), saved.Sequence);
    }
}
