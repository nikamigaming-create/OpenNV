using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    // Source field constructors attach to the actual common entry, never inferred from the
    // native package driver, gameplay snapshot or equipment query.
    private sealed class PackageLedger(FalloutProcessPackageSlots initial)
    {
        internal readonly List<FalloutProcessPackageWriterObservation> Current = initial.Current.Observations.ToList();
        internal readonly List<FalloutProcessPackageWriterObservation> Primary = initial.Primary?.Observations.ToList() ?? [];
        internal readonly HashSet<Guid> Ids = initial.Current.Observations.Concat(initial.Primary?.Observations ?? [])
            .Select(item => item.Observation).ToHashSet();
    }
    private readonly Dictionary<FalloutFormKey, Dictionary<long, PackageLedger>> _packageLedgers = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutProcessCommonPublication> _packagePublications = [];

    private FalloutProcessPackageSlots? ConstructSourcePackageSlots(FalloutCombatActorIdentity actor, long epoch,
        FalloutDetectionProcessLevel level, FalloutProcessPackageConstructionKind construction)
    {
        if (!FalloutSourceMainFamily.IsFallout3(_source.ExecutableSha256)) return null;
        if (construction == FalloutProcessPackageConstructionKind.InitialPlayerConstructor &&
                (!actor.EnginePlayer || epoch != 1 || level != FalloutDetectionProcessLevel.High) ||
            actor.EnginePlayer && epoch == 1 && construction != FalloutProcessPackageConstructionKind.InitialPlayerConstructor)
            throw new InvalidDataException("Package constructor proof did not match the actual initial Player constructor invocation.");
        return FalloutProcessPackageState.Construct(_source.ExecutableSha256, _process, epoch, level, construction);
    }

    internal FalloutProcessPackageRead ReadSourcePackageSlot(FalloutFormKey actor, long epoch, FalloutProcessPackageSlot slot)
    {
        RequireNotBusy();
        if (!Enum.IsDefined(slot)) throw new InvalidDataException("A source package getter requires its exact current/primary slot.");
        var current = Require(actor); var process = ReadSceneQueries(actor, epoch);
        // The selected reader verifies these two class tables. Independent
        // field construction does not establish any lower virtual's behavior.
        if (process.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh))
            throw new NotSupportedException("source-lower-process-package-virtual-getter-contract-unbound");
        var fields = RequireSourcePackageFields(current, process, slot);
        return new(process, slot, FalloutProcessPackageState.Capture(fields));
    }

    internal FalloutProcessPackageRead ReadSourcePackageSlotForIdle(FalloutProcessSceneQuery caller, FalloutProcessPackageSlot slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_busy || _idlePackageScope != caller || caller.Process != _process || caller.Stack != _stack ||
            caller.ExecutableSha256 != _source.ExecutableSha256 || !Enum.IsDefined(slot))
            throw new InvalidOperationException("Source package getter requires its exact entered idle process scope.");
        var current = Require(caller.Actor.Reference);
        if (current.Source != caller.Actor || current.Epoch != caller.Epoch || current.Level != caller.Level ||
            current.Gameplay?.Ownership != caller.GameplayOwnership || current.Gameplay is not { Retired: false } ||
            current.Boundary is not null || current.Phase is FalloutProcessCommonPhase.OldRetired or FalloutProcessCommonPhase.Retired)
            throw new NotSupportedException("source-idle-process-package-current-Actor-class-epoch-unavailable");
        var fields = RequireSourcePackageFields(current, caller, slot);
        return new(caller with { Changed = current.Changed }, slot, FalloutProcessPackageState.Capture(fields));
    }

    private FalloutProcessPackageFields RequireSourcePackageFields(FalloutProcessCommonEntry current,
        FalloutProcessSceneQuery process, FalloutProcessPackageSlot slot)
    {
        if (process.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh))
            throw new NotSupportedException("source-lower-process-package-virtual-getter-contract-unbound");
        var packages = current.Packages ?? throw new NotSupportedException("source-process-package-fields-unconstructed");
        if (packages.ConstructedEpoch != process.Epoch)
            throw new InvalidDataException("Current package getter lost its actual source construction/copy epoch.");
        var fields = slot == FalloutProcessPackageSlot.Current ? packages.Current : packages.Primary ??
            throw new NotSupportedException("source-lower-process-primary-package-getter-contract-unbound");
        if (fields.Publication.Process != _process || !fields.Available)
            throw new NotSupportedException(slot == FalloutProcessPackageSlot.Current
                ? FalloutProcessPackageState.CurrentWriterBoundary : FalloutProcessPackageState.PrimaryWriterBoundary);
        return fields;
    }

    internal void ObserveSourcePackageAssignment(FalloutFormKey actor, long epoch, Guid observation, string producer,
        FalloutFormKey? commandSource, FalloutFormKey? requestedPackage, bool remove, FalloutProcessPackageWriterTarget target)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        var current = Require(actor); _ = ReadSceneQueries(actor, epoch);
        var packages = current.Packages ?? throw new NotSupportedException("source-process-package-fields-unconstructed");
        if (!Enum.IsDefined(target) || (target & FalloutProcessPackageWriterTarget.Primary) != 0 && packages.Primary is null)
            throw new NotSupportedException("source-package-assignment-current-class-target-unreviewed");
        if (!_packageLedgers.TryGetValue(actor, out var epochs))
        {
            epochs = []; _packageLedgers.Add(actor, epochs);
        }
        if (!epochs.TryGetValue(epoch, out var ledger)) { ledger = new(packages); epochs.Add(epoch, ledger); }
        if (observation == Guid.Empty || !ledger.Ids.Add(observation)) throw new InvalidOperationException("Source package assignment replayed its actual ingress observation.");
        var changed = Next();
        var receipt = new FalloutProcessPackageWriterObservation(observation, producer, commandSource, requestedPackage,
            remove, target, _process, epoch, changed);
        if ((target & FalloutProcessPackageWriterTarget.Current) != 0) ledger.Current.Add(receipt);
        if ((target & FalloutProcessPackageWriterTarget.Primary) != 0) ledger.Primary.Add(receipt);
        var updated = packages with
        {
            Current = (target & FalloutProcessPackageWriterTarget.Current) != 0
                ? FalloutProcessPackageState.Invalidate(packages.Current, receipt, ledger.Current) : packages.Current,
            Primary = (target & FalloutProcessPackageWriterTarget.Primary) != 0
                ? FalloutProcessPackageState.Invalidate(packages.Primary!, receipt, ledger.Primary) : packages.Primary
        };
        _current[actor] = current with { Packages = updated, Changed = changed };
    }

    private string? SourcePackageSaveBlocker => _current.Values.FirstOrDefault(entry => entry.Packages is
    { Current.Available: false } or { Primary.Available: false }) is { } failed
        ? "source-process-package-observed-assignment-unbound:" + failed.Source.Reference : null;

    private static void ValidatePackageFields(FalloutProcessCommonEntry entry, string executable, Guid process, long sequence, bool historical,
        IReadOnlyList<FalloutProcessCommonPublication>? publications)
    {
        if (FalloutSourceMainFamily.IsFallout3(executable))
        {
            var packages = entry.Packages ?? throw new InvalidDataException("Common source process omitted its full package field owners.");
            if (!historical && packages.ConstructedEpoch != entry.Epoch)
                throw new InvalidDataException("Current common package fields lost their source construction/copy epoch.");
            FalloutProcessPackageState.Validate(packages, executable, historical ? packages.Current.Publication.Process : process,
                entry.Level, entry.Source.EnginePlayer, entry.Epoch, sequence, publications, historical);
        }
        else if (entry.Packages is not null) throw new InvalidDataException("Unreviewed source family acquired FO3 package fields.");
    }

    internal static IReadOnlyList<FalloutProcessCommonPublication>? ValidatePackagePublications(FalloutProcessCommonSnapshot saved)
    {
        if (!FalloutSourceMainFamily.IsFallout3(saved.ExecutableSha256))
        {
            if (saved.Publications is not null) throw new InvalidDataException("An unreviewed source family acquired FO3 package process lineage.");
            return null;
        }
        var publications = saved.Publications ?? throw new InvalidDataException("Common omitted its actual package process publication lineage.");
        if (publications.Count == 0 || publications[^1] is not { } latest || latest.Process != saved.CapturedProcess)
            throw new InvalidDataException("Common package process lineage lost its latest current publication.");
        var processes = new HashSet<Guid>();
        for (var index = 0; index < publications.Count; ++index)
        {
            var publication = publications[index] ?? throw new InvalidDataException("Common package process lineage contains a null publication.");
            if (publication.Process == Guid.Empty || !processes.Add(publication.Process) || publication.Sequence > saved.Sequence ||
                (index == 0 ? publication.PreviousProcess is not null || publication.Sequence != 0 :
                    publication.PreviousProcess != publications[index - 1].Process || publication.Sequence <= publications[index - 1].Sequence))
                throw new InvalidDataException("Common package process lineage changed its constructor or ordered actual cold handoffs.");
        }
        if (publications.Count == 1 ? saved.ColdHandoff is not null : saved.ColdHandoff is not { } cold ||
            cold.PreviousProcess != publications[^2].Process || cold.CurrentProcess != latest.Process || cold.Sequence != latest.Sequence)
            throw new InvalidDataException("Common package process lineage differs from its exact current cold receipt.");
        return publications;
    }

    private FalloutProcessCommonEntry RepublishPackageFields(FalloutProcessCommonEntry entry) => entry.Packages is { } fields
        ? entry with { Packages = FalloutProcessPackageState.Republish(fields, _process) } : entry;

    private void TransferLowPackageFields(FalloutProcessCommonEntry old, FalloutProcessCommonEntry next)
    {
        if (!FalloutSourceMainFamily.IsFallout3(_source.ExecutableSha256)) return;
        if (old.Level != FalloutDetectionProcessLevel.Low || next.Level != FalloutDetectionProcessLevel.High ||
            old.Packages is not { } source || next.Packages is not { } destination || _pending != next ||
            destination.ConstructionKind != FalloutProcessPackageConstructionKind.ReplacementProcessConstructor)
            throw new NotSupportedException("source-process-package-copy-current-tier-and-factory-unbound");
        var move = FalloutProcessPackageState.MoveCurrent(source.Current, destination.Current);
        _current[old.Source.Reference] = old with { Packages = source with { Current = move.Source }, Changed = Next() };
        _pending = next with { Packages = destination with { Current = move.Destination }, Changed = Next() };
    }

    private static FalloutProcessCommonEntry CapturePackageHistory(FalloutProcessCommonEntry entry) => entry.Packages is { } packages
        ? entry with
        {
            Packages = packages with
            {
                Current = FalloutProcessPackageState.Capture(packages.Current),
                Primary = packages.Primary is { } primary ? FalloutProcessPackageState.Capture(primary) : null,
                RetiredInitialLow = packages.RetiredInitialLow is { } low ? FalloutProcessPackageState.Capture(low) : null
            }
        } : entry;
}
