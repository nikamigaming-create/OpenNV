using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutHitReactionSource(FalloutFormKey Form, string Sha256);
internal sealed record FalloutHitReactionPredicateRead(FalloutHitReactionSource Source, int Condition,
    float Value, ulong RandomBefore, ulong RandomAfter);
internal sealed record FalloutHitReactionReadFault(long Attempt, byte Part, int HitLocation,
    FalloutHitReactionSource Source, int Condition, ushort Function, string Error,
    ulong RandomBefore, ulong RandomAfter, IReadOnlyList<FalloutHitReactionSource> Visited,
    IReadOnlyList<FalloutHitReactionPredicateRead> ConsumedReads)
{
    internal FalloutHitReactionReadFault Copy() => this with
    { Visited = Visited.ToArray(), ConsumedReads = ConsumedReads.ToArray() };
}
internal sealed record FalloutHitReactionFaultsSnapshot(FalloutFormKey Reference, long Attempts,
    string? CurrentError, IReadOnlyList<FalloutHitReactionReadFault> Faults)
{
    internal void Validate()
    {
        if (Attempts < 0 || Faults is null || string.IsNullOrWhiteSpace(Reference.OwnerPlugin) ||
            Reference.ObjectId is 0 or > FalloutFormKey.ObjectIdMask || CurrentError is not null &&
            (Faults.Count == 0 || Faults[^1].Error != CurrentError))
            throw new InvalidDataException("Saved hit-reaction fault history has invalid ownership or current fault.");
        long previous = 0;
        foreach (var fault in Faults)
        {
            if (fault is null || fault.Attempt <= previous || fault.Attempt > Attempts || fault.HitLocation < -1 ||
                fault.Condition < 0 || string.IsNullOrWhiteSpace(fault.Error) || fault.Visited is not { Count: > 0 } ||
                fault.ConsumedReads is null || fault.ConsumedReads.Any(read => read is null) || !fault.Visited.Contains(fault.Source))
                throw new InvalidDataException("Saved hit-reaction read fault lacks its exact stopped source site.");
            previous = fault.Attempt;
            foreach (var source in fault.Visited.Append(fault.Source).Concat(fault.ConsumedReads.Select(read => read.Source)))
                if (source is null || !FalloutAnimationSoundEventsSnapshot.Hash(source.Sha256) ||
                    string.IsNullOrWhiteSpace(source.Form.OwnerPlugin) || source.Form.ObjectId is 0 or > FalloutFormKey.ObjectIdMask)
                    throw new InvalidDataException("Saved hit-reaction source binding is invalid.");
            var random = fault.RandomBefore;
            foreach (var read in fault.ConsumedReads)
            {
                if (read.Condition < 0 || !float.IsFinite(read.Value) || read.RandomBefore != random || !fault.Visited.Contains(read.Source))
                    throw new InvalidDataException("Saved hit-reaction predicate prefix is invalid.");
                random = read.RandomAfter;
            }
            // The delegated read is observational. Its failure does not consume
            // a random draw; random predicates already have consumed receipts.
            if (random != fault.RandomAfter)
                throw new InvalidDataException("Saved failed hit-reaction read has an unowned random suffix.");
        }
    }
}

// A stopped, pure CTDA read is history, not an active reaction or a resumable
// cursor. Cold restore never reevaluates consumed mutable predicates.
internal sealed class FalloutHitReactionFaults(FalloutFormKey reference)
{
    private readonly List<FalloutHitReactionReadFault> _faults = [];
    private long _attempts;
    internal string? CurrentError { get; private set; }
    internal long BeginAttempt() => _attempts = checked(_attempts + 1);
    internal IReadOnlyList<FalloutHitReactionReadFault> Faults => _faults.AsReadOnly();
    internal void ClearCurrentError() => CurrentError = null;

    internal void Record(FalloutHitReactionReadFault fault, FalloutPluginStack records)
    {
        var snapshot = new FalloutHitReactionFaultsSnapshot(reference, _attempts, fault.Error, _faults.Append(fault).ToArray());
        ValidateSource(snapshot, records, reference);
        _faults.Add(fault.Copy()); CurrentError = fault.Error;
    }

    internal FalloutHitReactionFaultsSnapshot Capture()
    {
        var snapshot = new FalloutHitReactionFaultsSnapshot(reference, _attempts, CurrentError, _faults.Select(fault => fault.Copy()).ToArray());
        snapshot.Validate(); return snapshot;
    }

    internal void Restore(FalloutHitReactionFaultsSnapshot snapshot, FalloutPluginStack records)
    {
        if (_attempts != 0 || _faults.Count != 0 || CurrentError is not null)
            throw new InvalidDataException("Cannot replace existing hit-reaction history.");
        ValidateSource(snapshot, records, reference);
        _faults.AddRange(snapshot.Faults.Select(fault => fault.Copy())); _attempts = snapshot.Attempts; CurrentError = snapshot.CurrentError;
    }

    internal static FalloutHitReactionSource Source(FalloutPluginRecord record) =>
        new(record.FormKey, Convert.ToHexString(SHA256.HashData(record.ReadData())));

    internal static int Ordinal(FalloutCondition condition)
    {
        var sites = FalloutCondition.Read(condition.Owner).Select((value, index) => (value, index))
            .Where(row => row.value == condition).Select(row => row.index).ToArray();
        return sites.Length == 1 ? sites[0] : throw new NotSupportedException("Hit-reaction condition has no unique source ordinal.");
    }

    internal static void ValidateSource(FalloutHitReactionFaultsSnapshot snapshot, FalloutPluginStack records, FalloutFormKey reference)
    {
        snapshot.Validate();
        if (snapshot.Reference != reference) throw new InvalidDataException("Hit-reaction fault belongs to another reference.");
        foreach (var fault in snapshot.Faults)
        {
            foreach (var source in fault.Visited.Append(fault.Source).Concat(fault.ConsumedReads.Select(read => read.Source)).Distinct())
            {
                var record = records.GetEffective(source.Form);
                if (record.Signature != "IDLE" || !Source(record).Sha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Saved hit-reaction predicate differs from winning IDLE data.");
            }
            var failed = FalloutCondition.Read(records.GetEffective(fault.Source.Form));
            if (fault.Condition >= failed.Count || failed[fault.Condition].Function != fault.Function || fault.Function == 77 ||
                (failed[fault.Condition].Flags & 0x1e) != 0 || failed[fault.Condition].RunOn != 0)
                throw new InvalidDataException("Saved hit-reaction fault has no pure matching failed query.");
            foreach (var read in fault.ConsumedReads)
                if (read.Condition >= FalloutCondition.Read(records.GetEffective(read.Source.Form)).Count)
                    throw new InvalidDataException("Saved hit-reaction prefix site is absent.");
        }
    }
}
