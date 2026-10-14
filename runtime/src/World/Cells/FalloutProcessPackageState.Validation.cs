using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static partial class FalloutProcessPackageState
{
    private static void ValidateMutations(FalloutProcessPackageFields fields, string source, long epoch, long sequence,
        IReadOnlyList<FalloutProcessCommonPublication>? common, Dictionary<Guid, int>? commonIndices,
        IReadOnlyList<FalloutProcessPackagePublication> publications)
    {
        var factories = publications.ToDictionary(item => item.Process, item => item.Factory);
        var commands = new HashSet<(Guid, FalloutProcessPackageSlot, FalloutPackageStateOperation)>();
        var values = FalloutProcessPackageWords.Constructed; var available = true; long changed = 0;
        var observations = fields.Observations; var mutations = fields.Mutations!; var oi = 0; var mi = 0;
        while (oi < observations.Count || mi < mutations.Count)
        {
            if (oi < observations.Count && (mi == mutations.Count || observations[oi].Changed < mutations[mi].Entered))
            {
                var observation = observations[oi++];
                if (observation.Changed <= changed) throw new InvalidDataException("Package writer histories overlap or reorder.");
                available = false; changed = observation.Changed; continue;
            }
            var call = mutations[mi++] ?? throw new InvalidDataException("Package mutation history contains a null call.");
            if (call.Invocation is null || !available || call.Before != values || call.Entered <= changed || call.Changed > sequence ||
                !commands.Add((call.Invocation.SourceCommand, call.Slot, call.Operation)))
                throw new InvalidDataException("Package mutation repaired an unavailable writer, replayed a command or lost its actual before fields.");
            ValidateMutation(call, fields.Slot, source, epoch, factories);
            if (common is not null && commonIndices is not null)
            {
                if (!commonIndices.TryGetValue(call.Invocation.Process, out var at) || call.Entered <= common[at].Sequence ||
                    at + 1 < common.Count && call.Changed >= common[at + 1].Sequence)
                    throw new InvalidDataException("Package setter belongs to another actual Common cold interval.");
            }
            values = call.After; available = call.Step == FalloutPackageStateStep.Returned; changed = call.Changed;
        }
        if (values != fields.Fields || available != fields.Available || changed != fields.Changed)
            throw new InvalidDataException("Package fields differ from their ordered constructor/writer/failure prefix.");
        ValidateWords(values);
    }

    internal static void ValidateMutation(FalloutPackageStateMutation call, FalloutProcessPackageSlot slot,
        string source, long epoch, IReadOnlyDictionary<Guid, Guid> factories)
    {
        if (call is null || call.Invocation is not { } invocation || invocation.Actor is null ||
            invocation.SourceCommand == Guid.Empty || invocation.Process == Guid.Empty || invocation.Epoch != epoch || invocation.ThreadId < 1 ||
            call.Process is not { } process || process.Actor != invocation.Actor || process.Process != invocation.Process || process.Epoch != epoch ||
            process.GameplayOwnership == Guid.Empty || process.Changed < 1 || process.Changed >= call.Entered ||
            string.IsNullOrWhiteSpace(process.Stack) || Source(process.ExecutableSha256) != source ||
            process.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh) ||
            !factories.TryGetValue(invocation.Process, out var factory) || call.Factory != factory || call.Slot != slot ||
            !Enum.IsDefined(call.Operation) || call.Entered < 1 || call.Changed <= call.Entered || call.Before is null || call.After is null || call.Stores is null ||
            call.Step is not (FalloutPackageStateStep.Returned or FalloutPackageStateStep.Failed) ||
            (call.Step == FalloutPackageStateStep.Returned ? call.FailedAt is not null || call.Failure is not null :
                call.FailedAt is not { } failed || !Enum.IsDefined(failed) || failed is FalloutPackageStateStep.Failed or FalloutPackageStateStep.Returned || string.IsNullOrWhiteSpace(call.Failure)) ||
            call.Operation == FalloutPackageStateOperation.Clear && (call.RequestedPackage is not null || call.DerivedExtra is not null))
            throw new InvalidDataException("Package setter lost its actual command, mutable field factory, process or returned/failed boundary.");
        invocation.Actor.Validate(); ValidateWords(call.Before); ValidateWords(call.After); call.DerivedExtra?.Validate();
        if (call.RequestedPackage is { } requested && (requested.ObjectId == 0 || string.IsNullOrWhiteSpace(requested.OwnerPlugin)))
            throw new InvalidDataException("Package setter has no typed requested PACK identity.");
        if (call.Hour is { } hour && (string.IsNullOrWhiteSpace(hour.Producer) || hour.Global is null && hour.Bits != FalloutPackageRuntimeRegistry.NullGameHourBits ||
            hour.Global is { } global && (global.ObjectId == 0 || string.IsNullOrWhiteSpace(global.OwnerPlugin))))
            throw new InvalidDataException("Package setter omitted its command-time retained GameHour read.");
        if (call.Equal)
        {
            if (call.Operation != FalloutPackageStateOperation.Set || call.RequestedPackage != call.Before.Package || call.Stores.Count != 0 ||
                call.After != call.Before || call.Hour is not null || call.ReleasedCount is not null || call.RetainedCount is not null ||
                call.Step == FalloutPackageStateStep.Failed && call.FailedAt != FalloutPackageStateStep.Equality)
                throw new InvalidDataException("Pointer-equal package setter ran a clear/count/time/store child.");
            return;
        }
        var expected = new List<FalloutPackageStateStep>(10);
        var temporary = call.Stores.Count > 0 && call.Stores[0].Step == FalloutPackageStateStep.TemporaryPackagePointerClear;
        if (call.Before.Package is not null) expected.Add(temporary ? FalloutPackageStateStep.TemporaryPackagePointerClear : FalloutPackageStateStep.PackagePointerClear);
        expected.AddRange([FalloutPackageStateStep.TimeReset, FalloutPackageStateStep.IndexReset, FalloutPackageStateStep.FlagsReset, FalloutPackageStateStep.ExtraReset]);
        if (call.Operation == FalloutPackageStateOperation.Set && call.RequestedPackage is not null)
            expected.AddRange([FalloutPackageStateStep.TimeStore, FalloutPackageStateStep.PackageStore, FalloutPackageStateStep.ContextStore,
                FalloutPackageStateStep.IndexStore, FalloutPackageStateStep.ExtraStore]);
        if (call.Stores.Count > expected.Count || call.Step == FalloutPackageStateStep.Returned &&
            (call.Stores.Count != expected.Count || temporary || call.Operation == FalloutPackageStateOperation.Set && call.RequestedPackage == call.Before.Package))
            throw new InvalidDataException("Package setter returned without its complete source stores or bypassed pointer equality.");
        var values = call.Before; var changed = call.Entered;
        for (var index = 0; index < call.Stores.Count; ++index)
        {
            var store = call.Stores[index];
            if (store is null || store.Step != expected[index] || store.Before != values || store.Changed <= changed || store.Changed >= call.Changed)
                throw new InvalidDataException("Package setter reordered or omitted a committed field store.");
            var after = store.Step switch
            {
                FalloutPackageStateStep.PackagePointerClear or FalloutPackageStateStep.TemporaryPackagePointerClear => values with { Package = null },
                FalloutPackageStateStep.TimeReset => values with { TimeBits = FalloutPlayerFollowingContinuationSource.PackageUnsetTimeBits },
                FalloutPackageStateStep.IndexReset => values with { Index = -1 },
                FalloutPackageStateStep.FlagsReset => values with { Flags = 0 },
                FalloutPackageStateStep.ExtraReset => values with { Extra = null },
                FalloutPackageStateStep.TimeStore when call.Hour is not null => values with { TimeBits = call.Hour.Bits },
                FalloutPackageStateStep.PackageStore => values with { Package = call.RequestedPackage },
                FalloutPackageStateStep.ContextStore => values with { Context = null },
                FalloutPackageStateStep.IndexStore => values with { Index = 0 },
                FalloutPackageStateStep.ExtraStore => values with { Extra = call.DerivedExtra },
                _ => throw new InvalidDataException("Package state store has no owned original field producer.")
            };
            if (store.After != after) throw new InvalidDataException("Package state changed a word outside its actual source store.");
            values = after; changed = store.Changed;
        }
        if (values != call.After || call.Before.Package is null && call.ReleasedCount is not null ||
            call.Operation == FalloutPackageStateOperation.Clear && call.RetainedCount is not null || call.RequestedPackage is null && call.RetainedCount is not null ||
            temporary && (call.Step != FalloutPackageStateStep.Failed || call.FailedAt != FalloutPackageStateStep.TemporaryDeletion ||
                call.Stores.Count != 1 || call.ReleasedCount != 0) ||
            call.Stores.Any(store => store.Step == FalloutPackageStateStep.TimeStore) && call.RetainedCount is null ||
            call.Step == FalloutPackageStateStep.Returned && call.Before.Package is not null && call.ReleasedCount is null ||
            call.Step == FalloutPackageStateStep.Returned && call.Operation == FalloutPackageStateOperation.Set && call.RequestedPackage is not null &&
                (call.RetainedCount is null || call.Hour is null) || call.Hour is not null && call.RetainedCount is null ||
            call.RetainedCount is not null && !call.Stores.Any(store => store.Step == FalloutPackageStateStep.ExtraReset) ||
            call.Before.Package is not null && call.Stores.Any(store => store.Step == FalloutPackageStateStep.TimeReset) && call.ReleasedCount is null)
            throw new InvalidDataException("Package fields/count/clock differ from their committed setter prefix.");
    }

    private static void ValidateWords(FalloutProcessPackageWords words)
    {
        if (words is null || words.Context is not null || words.Index is not (-1 or 0) || words.Flags != 0 ||
            words.Package is { } form && (form.ObjectId == 0 || string.IsNullOrWhiteSpace(form.OwnerPlugin)))
            throw new InvalidDataException("Package six-field state has an unowned context/progress/flag writer.");
        words.Extra?.Validate();
    }
    private static FalloutProcessPackageWords FieldsAt(FalloutProcessPackageFields fields, long changed) =>
        (fields.Mutations ?? []).LastOrDefault(call => call.Changed <= changed)?.After ?? FalloutProcessPackageWords.Constructed;

    private static bool MutationPrefixEquals(IReadOnlyList<FalloutPackageStateMutation>? captured, IEnumerable<FalloutPackageStateMutation> actual)
    {
        if (captured is null) return false;
        using var expected = actual.GetEnumerator();
        foreach (var call in captured)
            if (!expected.MoveNext() || !MutationEquivalent(call, expected.Current)) return false;
        return !expected.MoveNext();
    }
    internal static bool MutationEquivalent(FalloutPackageStateMutation first, FalloutPackageStateMutation second) =>
        first with { Stores = second.Stores } == second && first.Stores.SequenceEqual(second.Stores);
}
