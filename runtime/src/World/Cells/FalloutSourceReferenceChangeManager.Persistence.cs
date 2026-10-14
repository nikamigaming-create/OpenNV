using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceReferenceChangeManager
{
    private static FalloutReferenceChangeCall CloneCall(FalloutReferenceChangeCall call) => call with
    { Stores = Array.AsReadOnly(call.Stores.ToArray()) };
    internal FalloutReferenceChangeSnapshot Capture()
    {
        if (_current is not null) { _fault++; throw new InvalidOperationException("Entered reference change map cannot be captured."); }
        var saved = new FalloutReferenceChangeSnapshot(Schema, Source, Stack, Process, Factory, Constructor,
            _sequence, _managerFlags, Array.AsReadOnly(_publications.ToArray()), Array.AsReadOnly(_entries.Values.ToArray()),
            Array.AsReadOnly(_buckets.Select(value => (IReadOnlyList<Guid>)Array.AsReadOnly(value.ToArray())).ToArray()),
            Array.AsReadOnly(_calls.Select(CloneCall).ToArray()), _writerBoundary, _retired,
            _pending.Count == 0 ? null : Array.AsReadOnly(_pending.ToArray()),
            _data.Count == 0 ? null : Array.AsReadOnly(_data.Values.Select(value => value.Capture()).ToArray()));
        Validate(saved, Source, Stack); return saved;
    }
    internal void Retire()
    {
        if (_current is not null) { _fault++; throw new InvalidOperationException("Entered reference change map cannot retire."); }
        _retired = true;
    }
    internal static void Validate(FalloutReferenceChangeSnapshot saved, FalloutReferencePositionSource source, string stack)
    {
        ArgumentNullException.ThrowIfNull(saved); source.Validate();
        if (saved.Schema != Schema || saved.Source != source || saved.Stack != stack || saved.Process == Guid.Empty ||
            saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty || saved.Sequence < 0 || saved.Publications is not { Count: > 0 } ||
            saved.Entries is null || saved.Calls is null || saved.Buckets is not { Count: FalloutReferencePositionSource.ChangeBucketCount } ||
            saved.WriterBoundary is { Length: 0 })
            throw new InvalidDataException("Reference change map changed its selected source, shape or schema.");
        var publications = new Dictionary<Guid, (Guid Factory, int Ordinal)>(); var factories = new HashSet<Guid>();
        var constructors = new HashSet<Guid>(); FalloutReferenceChangePublication? previous = null;
        foreach (var publication in saved.Publications)
        {
            if (publication.Process == Guid.Empty || publication.Factory == Guid.Empty || publication.Constructor == Guid.Empty ||
                !factories.Add(publication.Factory) || !constructors.Add(publication.Constructor) ||
                !publications.TryAdd(publication.Process, (publication.Factory, publications.Count)) ||
                (previous is null ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != previous.Process || publication.PreviousFactory != previous.Factory))
                throw new InvalidDataException("Reference map reused or replaced a shared constructor lifetime.");
            previous = publication;
        }
        if (previous!.Process != saved.Process || previous.Factory != saved.Factory || previous.Constructor != saved.Constructor)
            throw new InvalidDataException("Reference map omitted its current fresh source owner.");
        var data = new Dictionary<Guid, FalloutReferenceChangeDataSnapshot>();
        foreach (var buffer in saved.Data ?? [])
        {
            if (buffer.Identity == Guid.Empty || string.IsNullOrWhiteSpace(buffer.Producer) || buffer.Bytes is not { Length: > 0 } ||
                !data.TryAdd(buffer.Identity, buffer)) throw new InvalidDataException("Change map replaced its real retained data storage.");
        }
        var entries = new Dictionary<uint, FalloutReferenceChangeEntry>(); var pending = new Dictionary<Guid, FalloutReferenceChangeEntry>();
        var buckets = Enumerable.Range(0, FalloutReferencePositionSource.ChangeBucketCount).Select(_ => new List<Guid>()).ToArray();
        var ids = new HashSet<Guid>(); var threads = new Dictionary<Guid, int>(); long sequence = 0;
        uint manager = FalloutReferencePositionSource.InitialChangeManagerFlags; string? boundary = null;
        var lastPublication = -1; var clearCount = 0;
        foreach (var call in saved.Calls)
        {
            var invocation = call.Invocation; invocation.Actor.Validate(); invocation.ValidatePackageCaller();
            if (call.Source != source.Identity || invocation.Call == Guid.Empty || invocation.ReferenceConstructor == Guid.Empty ||
                invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 || invocation.ThreadId < 1 ||
                !publications.TryGetValue(invocation.ReferenceProcess, out var publication) || publication.Factory != call.Factory ||
                publication.Ordinal < lastPublication || call.Entered != ++sequence || call.ManagerBefore != manager ||
                !Enum.IsDefined(call.Step) || call.Stores is null || call.Returned != (call.Failure is null) || call.Failure is { Length: 0 } ||
                call.Returned && call.Step != FalloutReferenceChangeStep.Returned ||
                threads.TryGetValue(invocation.ReferenceProcess, out var thread) && thread != invocation.ThreadId)
                throw new InvalidDataException("Change map lost its original caller, shared thread or failed prefix order.");
            if (call.Kind is not ("reference-change" or "startup-manager-flag-one-clear" or
                "serialized-change-data-publication" or "unowned-change-manager-writer"))
                throw new InvalidDataException("Change map contains an unknown source operation.");
            if (call.Kind == "startup-manager-flag-one-clear" && (++clearCount != 1 ||
                invocation.Ingress != FalloutReferencePositionIngress.ConfiguredStartup))
                throw new InvalidDataException("Change map replayed or replaced its startup flag writer.");
            if (call.ThreadFlags is { } flags && (flags.Source != source.Identity || flags.Process != invocation.ReferenceProcess ||
                flags.Invocation != invocation.Call || flags.Thread != invocation.ThreadId || flags.Factory == Guid.Empty ||
                flags.ConsumerFactory == Guid.Empty || flags.Sequence < 1 || flags.Initialized != 1))
                throw new InvalidDataException("Change map borrowed a foreign current-thread flag projection.");
            if (call.ReferenceFlags is { } reference && (reference.Source != FalloutReferenceScriptExtraInitializerSource.Reviewed(source.Fields).Identity ||
                reference.Stack != stack || reference.Actor != invocation.Actor || reference.Process != invocation.CommonProcess ||
                reference.Constructor != invocation.ReferenceConstructor || reference.Epoch != invocation.Epoch || reference.Factory == Guid.Empty ||
                reference.FieldsFactory == Guid.Empty || reference.Sequence < 0))
                throw new InvalidDataException("Change map borrowed another reference flag owner.");
            FalloutReferenceChangeEntry? before = null;
            if (call.SourceFormId is { } key) entries.TryGetValue(key, out before);
            if (call.Before is not null && call.Before != before)
                throw new InvalidDataException("Change map lookup replaced its real current record.");
            var lastStore = -1;
            foreach (var store in call.Stores)
            {
                if (store.Changed != ++sequence) throw new InvalidDataException("Change map reordered a committed store.");
                var rank = store.Step switch
                {
                    FalloutReferenceChangeStep.EntryConstructed => 0,
                    FalloutReferenceChangeStep.Inserted => 1,
                    FalloutReferenceChangeStep.MaskStore => 2,
                    FalloutReferenceChangeStep.ManagerFlagStore => 0,
                    FalloutReferenceChangeStep.DataStore => 0,
                    _ => throw new InvalidDataException("Change map invented a source store kind.")
                };
                if (rank <= lastStore) throw new InvalidDataException("Change map repeated or inverted a child store.");
                lastStore = rank;
                if (store.Step == FalloutReferenceChangeStep.ManagerFlagStore)
                {
                    if (call.Kind != "startup-manager-flag-one-clear" || store.Entry is not null || store.Data is not null ||
                        store.Bits != (manager & ~1u)) throw new InvalidDataException("Change manager altered unrelated flag bits.");
                    manager &= ~1u; continue;
                }
                if (call.SourceFormId is not { } formId || formId == 0 || store.Entry is not { } identity || identity == Guid.Empty)
                    throw new InvalidDataException("Change map store omitted its actual source key/record.");
                if (store.Step == FalloutReferenceChangeStep.EntryConstructed)
                {
                    if (call.Kind != "reference-change" || call.Suppressed != false || before is not null || store.Bits != 0 || store.Data is not null ||
                        !ids.Add(identity)) throw new InvalidDataException("Change map fabricated or replayed an entry constructor.");
                    pending.Add(identity, new(identity, formId, invocation.Actor.Reference, 0, null));
                }
                else if (store.Step == FalloutReferenceChangeStep.Inserted)
                {
                    if (!pending.Remove(identity, out var constructed) || constructed.SourceFormId != formId || entries.ContainsKey(formId) ||
                        store.Bits != checked((uint)entries.Count + 1) || store.Data is not null)
                        throw new InvalidDataException("Change map insertion lacks its real prior constructor.");
                    entries.Add(formId, constructed); buckets[Bucket(formId)].Insert(0, identity);
                }
                else if (store.Step == FalloutReferenceChangeStep.MaskStore)
                {
                    if (call.Kind != "reference-change" || call.Suppressed != false || !entries.TryGetValue(formId, out var current) ||
                        current.Identity != identity || current.SerializedData is not null || store.Data is not null || store.Bits != (current.Flags | call.Mask))
                        throw new InvalidDataException("Change map OR store replaced data-present behavior or lost old bits.");
                    entries[formId] = current with { Flags = store.Bits.Value };
                }
                else
                {
                    if (call.Kind != "serialized-change-data-publication" || !entries.TryGetValue(formId, out var current) ||
                        current.Identity != identity || current.SerializedData is not null || store.Bits is not null ||
                        store.Data is not { } actualData || !data.ContainsKey(actualData))
                        throw new InvalidDataException("Change map data publication lacks its actual retained buffer.");
                    entries[formId] = current with { SerializedData = actualData };
                }
            }
            if (call.SourceFormId is { } afterKey)
            {
                entries.TryGetValue(afterKey, out var after);
                after ??= pending.Values.SingleOrDefault(value => value.SourceFormId == afterKey);
                if (call.After is not null && call.After != after) throw new InvalidDataException("Change map lost its committed record prefix.");
            }
            else if (call.After is not null || call.Before is not null)
                throw new InvalidDataException("Change map retained records without a source key.");
            if (call.Kind == "reference-change" && call.Returned)
            {
                if (call.ThreadFlags is null || call.Suppressed is null) throw new InvalidDataException("Change return omitted its actual source predicates.");
                var early = !call.Force && (((call.ThreadFlags.Flags | call.ManagerBefore) & 1) != 0);
                var excluded = call.ReferenceFlags is { } field && (field.Flags & 0x4000) != 0;
                var dataHandler = call.ReferenceFlags is { } cached && (cached.Flags & 0x20) != 0 && call.DataHandlerSuppressed == true;
                if (call.Suppressed != (early || excluded || dataHandler) || early && call.ReferenceFlags is not null ||
                    !early && call.ReferenceFlags is null || call.Suppressed == true && call.Stores.Count != 0 ||
                    call.Suppressed == false && (call.After is null || call.SourceFormId is null) ||
                    !early && !excluded && call.ReferenceFlags is { } dynamic && (dynamic.Flags & 0x20) != 0 && call.DataHandlerSuppressed is null ||
                    call.Suppressed == false && call.After?.SerializedData is null && call.Stores.LastOrDefault()?.Step != FalloutReferenceChangeStep.MaskStore)
                    throw new InvalidDataException("Change return invented suppression or skipped its mandatory real map effect.");
            }
            if (call.UnownedWriter is { } observed)
            {
                if (call.Kind != "unowned-change-manager-writer" || string.IsNullOrWhiteSpace(observed) || call.Stores.Count != 0)
                    throw new InvalidDataException("Change map invalidation changed actual store state.");
                boundary = observed;
            }
            if (call.Changed != ++sequence || call.ManagerAfter != manager)
                throw new InvalidDataException("Change manager flags or sequence differ from original store history.");
            threads.TryAdd(invocation.ReferenceProcess, invocation.ThreadId); lastPublication = publication.Ordinal;
        }
        if (saved.ManagerFlags != manager || saved.Sequence != sequence || saved.WriterBoundary != boundary ||
            !saved.Entries.SequenceEqual(entries.Values) || !(saved.PendingEntries ?? []).SequenceEqual(pending.Values) ||
            saved.Buckets.Where((bucket, index) => !bucket.SequenceEqual(buckets[index])).Any() ||
            saved.Entries.Any(value => value.SerializedData is { } payload && !data.ContainsKey(payload)))
            throw new InvalidDataException("Cold change map replaced its current flags, entry identity, bucket order or data lifetime.");
    }
}
