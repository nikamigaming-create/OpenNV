using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutProcessPackageSlot { Current, Primary }
internal enum FalloutProcessPackageConstructionKind { IndependentProcessConstructor, InitialPlayerConstructor, ReplacementProcessConstructor }
[Flags]
internal enum FalloutProcessPackageWriterTarget { Current = 1, Primary = 2, Both = 3 }
internal sealed record FalloutProcessPackageWords(FalloutFormKey? Package, Guid? Procedure, uint Extra,
    int Index, uint TimeBits, uint Flags)
{
    internal static FalloutProcessPackageWords Constructed => new(null, null, 0, -1,
        FalloutPlayerFollowingContinuationSource.PackageUnsetTimeBits, 0);
}
internal sealed record FalloutProcessPackageConstruction(Guid Owner, Guid Process, string Source,
    string Producer, FalloutProcessPackageSlot Slot, FalloutProcessPackageWords Fields);
internal sealed record FalloutProcessPackageMove(Guid SourceFactory, Guid DestinationFactory,
    FalloutProcessPackageWords SourceBefore, FalloutProcessPackageWords DestinationBefore,
    FalloutProcessPackageWords SourceAfter, FalloutProcessPackageWords DestinationAfter, int DestinationStores, int SourceStores);
internal sealed record FalloutProcessPackageWriterObservation(Guid Observation, string Producer,
    FalloutFormKey? CommandSource, FalloutFormKey? RequestedPackage, bool Remove,
    FalloutProcessPackageWriterTarget Target, Guid Process, long Epoch, long Changed);
internal sealed record FalloutProcessPackagePublication(Guid Process, Guid Factory,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutProcessPackageFields(string Source, FalloutProcessPackageSlot Slot,
    FalloutProcessPackageConstruction Constructor, FalloutProcessPackagePublication Publication,
    FalloutProcessPackageWords Fields, bool Available, long Changed,
    IReadOnlyList<FalloutProcessPackageWriterObservation> Observations,
    FalloutProcessPackageMove? InitialMove = null,
    IReadOnlyList<FalloutProcessPackagePublication>? Publications = null);
internal sealed record FalloutProcessPackageSlots(FalloutProcessPackageFields Current,
    FalloutProcessPackageFields? Primary, long ConstructedEpoch, FalloutProcessPackageConstructionKind ConstructionKind,
    FalloutProcessPackageFields? RetiredInitialLow = null);
internal sealed record FalloutProcessPackageRead(FalloutProcessSceneQuery Process, FalloutProcessPackageSlot Slot,
    FalloutProcessPackageFields Fields);

// Full package state has six independent source fields. Constructor-null
// pointer getters do not initialize a procedure, borrow a driver, run clear,
// adjust a refcount or read time. Real assignment remains unavailable until
// those source writers and reached effects have owners.
internal static class FalloutProcessPackageState
{
    internal const string CurrentWriterBoundary = "source-current-process-package-assignment-writer-unbound";
    internal const string PrimaryWriterBoundary = "source-primary-process-package-assignment-writer-unbound";
    internal static string Source(string executable)
    {
        if (executable != FalloutSourceMainFamily.Fallout3) throw new NotSupportedException("Original process package fields are unreviewed for this image.");
        return FalloutAdvancementRuntimeReceipt.Hash(executable + "\0source-package-state/v2;" +
            "Base-current-MiddleHigh-primary-independent-six-field-construction;" +
            "null-package-null-procedure-zero-extra-minus-one-index-minus-one-time-zero-flags;" +
            "Low-copy-package-procedure-index-time-move-and-old-clear-with-extra-flags-untouched;" +
            "observed-current-primary-writers-invalidate-before-current-getter;" +
            "complete-field-factory-lineage-borrows-actual-Common-process-publications;no-cold-effect-replay");
    }

    internal static FalloutProcessPackageSlots Construct(string executable, Guid process, long epoch,
        FalloutDetectionProcessLevel level, FalloutProcessPackageConstructionKind construction)
    {
        var source = Source(executable);
        if (process == Guid.Empty || epoch < 1 || !Enum.IsDefined(level) || !Enum.IsDefined(construction) ||
            construction == FalloutProcessPackageConstructionKind.InitialPlayerConstructor &&
                (epoch != 1 || level != FalloutDetectionProcessLevel.High) ||
            construction == FalloutProcessPackageConstructionKind.ReplacementProcessConstructor && epoch == 1)
            throw new InvalidDataException("Package fields lost their real process constructor/class/epoch or initial/replacement source selection.");
        var current = Constructor(source, process, FalloutProcessPackageSlot.Current, "actual-selected-Base-package-state-constructor");
        var primary = level is FalloutDetectionProcessLevel.MiddleHigh or FalloutDetectionProcessLevel.High
            ? Constructor(source, process, FalloutProcessPackageSlot.Primary, "actual-selected-MiddleHigh-package-state-constructor") : null;
        if (construction != FalloutProcessPackageConstructionKind.InitialPlayerConstructor)
            return new(current, primary, epoch, construction);
        // This is the original Low field lifetime inside Player construction,
        // not a proxy actor or a semantic package instance. The full source
        // state is constructed before the selected High common-copy transfers
        // its four moved fields; all three field factories remain independent.
        var initialLow = Constructor(source, process, FalloutProcessPackageSlot.Current, "actual-selected-inherited-Low-Base-package-state-constructor");
        var moved = MoveCurrent(initialLow, current);
        return new(moved.Destination, primary, epoch, construction, moved.Source);
    }

    internal static (FalloutProcessPackageFields Source, FalloutProcessPackageFields Destination) MoveCurrent(
        FalloutProcessPackageFields source, FalloutProcessPackageFields destination)
    {
        if (source.Slot != FalloutProcessPackageSlot.Current || destination.Slot != FalloutProcessPackageSlot.Current ||
            source.Source != destination.Source || source.Publication.Process != destination.Publication.Process ||
            source.Publication.Factory == destination.Publication.Factory || !source.Available || !destination.Available)
            throw new NotSupportedException("source-process-package-move-lost-current-field-lifetimes-or-unowned-writer");
        var sourceAfter = source.Fields with
        {
            Package = null,
            Procedure = null,
            Index = -1,
            TimeBits = FalloutPlayerFollowingContinuationSource.PackageUnsetTimeBits
        };
        var destinationAfter = destination.Fields with
        {
            Package = source.Fields.Package,
            Procedure = source.Fields.Procedure,
            Index = source.Fields.Index,
            TimeBits = source.Fields.TimeBits
        };
        var move = new FalloutProcessPackageMove(source.Publication.Factory, destination.Publication.Factory,
            source.Fields, destination.Fields, sourceAfter, destinationAfter, 4, 4);
        return (source with { Fields = sourceAfter, InitialMove = move }, destination with { Fields = destinationAfter, InitialMove = move });
    }

    internal static FalloutProcessPackageFields Invalidate(FalloutProcessPackageFields fields,
        FalloutProcessPackageWriterObservation observation, IReadOnlyList<FalloutProcessPackageWriterObservation> history) => fields with
        {
            Available = false,
            Changed = observation.Changed,
            Observations = history
        };

    internal static FalloutProcessPackageSlots Republish(FalloutProcessPackageSlots saved, Guid process) =>
        saved with { Current = Republish(saved.Current, process), Primary = saved.Primary is { } primary ? Republish(primary, process) : null };

    private static FalloutProcessPackageFields Republish(FalloutProcessPackageFields saved, Guid process)
    {
        if (process == Guid.Empty || saved.Publication.Process == process)
            throw new InvalidDataException("Cold package publication reused its old source process.");
        var history = saved.Publications ?? throw new InvalidDataException("Cold package field omitted its complete constructor publication history.");
        var publication = new FalloutProcessPackagePublication(process, Guid.NewGuid(), saved.Publication.Process, saved.Publication.Factory);
        return saved with { Publication = publication, Publications = Array.AsReadOnly(history.Append(publication).ToArray()) };
    }
    private static FalloutProcessPackageFields Constructor(string source, Guid process, FalloutProcessPackageSlot slot, string producer)
    {
        var fields = FalloutProcessPackageWords.Constructed; var factory = Guid.NewGuid();
        var publication = new FalloutProcessPackagePublication(process, factory);
        return new(source, slot, new(factory, process, source, producer, slot, fields),
            publication, fields, true, 0, [], Publications: Array.AsReadOnly(new[] { publication }));
    }

    internal static FalloutProcessPackageFields Capture(FalloutProcessPackageFields fields) => fields with
    {
        Observations = Array.AsReadOnly(fields.Observations.ToArray()),
        Publications = fields.Publications is { } publications ? Array.AsReadOnly(publications.ToArray()) : null
    };

    internal static void Validate(FalloutProcessPackageSlots saved, string executable, Guid process,
        FalloutDetectionProcessLevel level, bool actualPlayer, long epoch, long sequence,
        IReadOnlyList<FalloutProcessCommonPublication>? commonPublications = null, bool historical = false)
    {
        ArgumentNullException.ThrowIfNull(saved); var source = Source(executable);
        var primaryExpected = level is FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh;
        var initialPlayer = saved.ConstructionKind == FalloutProcessPackageConstructionKind.InitialPlayerConstructor;
        if (saved.Current is null || primaryExpected != (saved.Primary is not null) || !Enum.IsDefined(saved.ConstructionKind) ||
            saved.ConstructedEpoch < 1 || saved.ConstructedEpoch > epoch ||
            initialPlayer != (saved.RetiredInitialLow is not null) || initialPlayer &&
                (!actualPlayer || saved.ConstructedEpoch != 1 || level != FalloutDetectionProcessLevel.High) ||
            actualPlayer && epoch == 1 && !initialPlayer ||
            saved.ConstructionKind == FalloutProcessPackageConstructionKind.ReplacementProcessConstructor && saved.ConstructedEpoch == 1)
            throw new InvalidDataException("Package slots lost their separate Base/MiddleHigh/initial Low constructor owners.");
        var common = CommonIndices(commonPublications);
        var factories = new HashSet<Guid>();
        foreach (var fields in new[] { saved.Current, saved.Primary, saved.RetiredInitialLow }.OfType<FalloutProcessPackageFields>())
        {
            var retiredLow = ReferenceEquals(fields, saved.RetiredInitialLow);
            ValidateFields(fields, source, retiredLow ? null : process, saved.ConstructedEpoch, sequence,
                commonPublications, common, historical || retiredLow, retiredLow);
            foreach (var publication in fields.Publications!)
                if (!factories.Add(publication.Factory))
                    throw new InvalidDataException("Independent package slots aliased a current or historical field factory.");
        }
        if (saved.Current.Slot != FalloutProcessPackageSlot.Current || saved.Primary is { Slot: not FalloutProcessPackageSlot.Primary } ||
            saved.RetiredInitialLow is { Slot: not FalloutProcessPackageSlot.Current })
            throw new InvalidDataException("Current and primary process package slots were aliased or exchanged.");
        if (saved.RetiredInitialLow is { } initial)
        {
            var move = initial.InitialMove ?? throw new InvalidDataException("Initial Low package state lost its actual source move.");
            if (saved.Current.InitialMove != move || move.SourceFactory == Guid.Empty || move.DestinationFactory == Guid.Empty ||
                move.SourceFactory != initial.Constructor.Owner || move.DestinationFactory != saved.Current.Constructor.Owner ||
                move.SourceFactory == move.DestinationFactory || move.DestinationStores != 4 || move.SourceStores != 4 ||
                move.SourceBefore != FalloutProcessPackageWords.Constructed || move.DestinationBefore != FalloutProcessPackageWords.Constructed ||
                move.SourceAfter != initial.Fields || move.DestinationAfter != saved.Current.Fields)
                throw new InvalidDataException("Player initial package field transfer lost its exact four-word move/clear.");
            if (commonPublications is { Count: > 0 } &&
                (initial.Constructor.Process != commonPublications[0].Process || saved.Current.Constructor.Process != initial.Constructor.Process ||
                    saved.Primary?.Constructor.Process != initial.Constructor.Process))
                throw new InvalidDataException("Initial Player package constructors are foreign to the actual original Common process.");
        }
    }

    // A reader validates its actual slot directly, without manufacturing a
    // sibling state to satisfy aggregate validation. The optional parent adds
    // authority for historical process/factory identities beyond local shape.
    internal static void ValidateRead(FalloutProcessPackageRead read, string executable, string stack,
        FalloutProcessCommonSnapshot? common = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (read.Process is not { } process || process.Actor is null || read.Fields is not { } fields ||
            process.Process == Guid.Empty || process.Stack != stack || process.ExecutableSha256 != executable || process.Epoch < 1 ||
            process.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh) ||
            !Enum.IsDefined(read.Slot) || fields.Slot != read.Slot || !fields.Available)
            throw new InvalidDataException("Package getter lost its actual current source/class/slot/Actor identity.");
        process.Actor.Validate();
        IReadOnlyList<FalloutProcessCommonPublication>? publications = null;
        if (common is not null)
        {
            FalloutActorProcessCommonState.Validate(common);
            if (common.Stack != stack || common.ExecutableSha256 != executable)
                throw new InvalidDataException("Package getter selected a foreign actual Common parent.");
            publications = common.Publications;
        }
        var commonIndices = CommonIndices(publications);
        ValidateFields(fields, Source(executable), process.Process, process.Epoch, process.Changed,
            publications, commonIndices, true, false);
        if (common is null) return;
        var entry = common.Current.Concat(common.Retired).Concat(common.Pending is null ? [] : [common.Pending])
            .FirstOrDefault(candidate => candidate.Source == process.Actor && candidate.Epoch == process.Epoch &&
                candidate.Packages is { } slots && (read.Slot == FalloutProcessPackageSlot.Current ? slots.Current : slots.Primary) is { } owner &&
                owner.Constructor == fields.Constructor) ??
            throw new InvalidDataException("Package getter has no exact actual or historical Common slot owner.");
        var package = entry.Packages ?? throw new InvalidDataException("Package getter omitted its actual Common package slots.");
        var actual = read.Slot == FalloutProcessPackageSlot.Current ? package.Current :
            package.Primary ?? throw new InvalidDataException("Package getter invented a primary Common field owner.");
        var actualPublications = actual.Publications ?? throw new InvalidDataException("Actual package getter owner omitted its publication lineage.");
        var lease = entry.Gameplay ?? throw new InvalidDataException("Package getter lost its actual borrowed Common gameplay lease.");
        var processIndex = -1;
        if (commonIndices is not null && !commonIndices.TryGetValue(process.Process, out processIndex))
            throw new InvalidDataException("Package getter lost its actual Common process interval.");
        if (entry.Level != process.Level || lease.Retired || lease.Ownership != process.GameplayOwnership || lease.Source != process.Actor ||
            lease.ProcessEpoch != process.Epoch || process.Changed < lease.Changed || process.Changed > entry.Changed ||
            publications is not null && processIndex + 1 < publications.Count && process.Changed >= publications[processIndex + 1].Sequence ||
            fields.Source != actual.Source || fields.Fields != actual.Fields || fields.InitialMove != actual.InitialMove ||
            fields.Publications!.Count > actualPublications.Count || !fields.Publications.SequenceEqual(actualPublications.Take(fields.Publications.Count)) ||
            !fields.Observations.SequenceEqual(actual.Observations.TakeWhile(item => item.Changed <= process.Changed)))
            throw new InvalidDataException("Package getter changed its actual constructor, full fields, move, factory or historical writer prefix.");
    }

    private static Dictionary<Guid, int>? CommonIndices(IReadOnlyList<FalloutProcessCommonPublication>? publications)
    {
        if (publications is null) return null;
        var result = new Dictionary<Guid, int>(publications.Count);
        for (var index = 0; index < publications.Count; ++index)
            if (publications[index] is not { } publication || publication.Process == Guid.Empty || !result.TryAdd(publication.Process, index))
                throw new InvalidDataException("Package fields received an invalid actual Common process lineage.");
        return result;
    }

    private static void ValidateFields(FalloutProcessPackageFields fields, string source, Guid? process, long epoch, long sequence,
        IReadOnlyList<FalloutProcessCommonPublication>? common, Dictionary<Guid, int>? commonIndices, bool historical, bool retiredLow)
    {
        var producer = retiredLow ? "actual-selected-inherited-Low-Base-package-state-constructor" :
            fields.Slot == FalloutProcessPackageSlot.Current ? "actual-selected-Base-package-state-constructor" :
                "actual-selected-MiddleHigh-package-state-constructor";
        if (fields.Source != source || fields.Constructor is not { } constructor || fields.Publication is not { } latest || fields.Fields is null ||
            !Enum.IsDefined(fields.Slot) || constructor.Owner == Guid.Empty || constructor.Process == Guid.Empty || constructor.Source != source ||
            constructor.Slot != fields.Slot || constructor.Producer != producer || constructor.Fields != FalloutProcessPackageWords.Constructed ||
            process is { } current && latest.Process != current || fields.Changed < 0 || fields.Changed > sequence || fields.Observations is null ||
            fields.Available != (fields.Observations.Count == 0) || fields.Fields != FalloutProcessPackageWords.Constructed ||
            fields.Publications is not { Count: > 0 } publications || latest != publications[^1] ||
            publications[0] != new FalloutProcessPackagePublication(constructor.Process, constructor.Owner) || retiredLow && publications.Count != 1)
            throw new InvalidDataException("Package field lost its complete selected constructor, values or current publication.");
        var processes = new Dictionary<Guid, int>(publications.Count); var factories = new HashSet<Guid>();
        var commonStart = -1;
        if (commonIndices is not null && !commonIndices.TryGetValue(constructor.Process, out commonStart))
            throw new InvalidDataException("Package constructor belongs to a foreign actual Common process.");
        for (var index = 0; index < publications.Count; ++index)
        {
            var publication = publications[index] ?? throw new InvalidDataException("Package field contains a null publication.");
            if (publication.Process == Guid.Empty || publication.Factory == Guid.Empty || !processes.TryAdd(publication.Process, index) ||
                !factories.Add(publication.Factory) || (index == 0 ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != publications[index - 1].Process || publication.PreviousFactory != publications[index - 1].Factory))
                throw new InvalidDataException("Package field changed or duplicated an exact constructor/cold factory handoff.");
            if (common is not null && (commonStart + index >= common.Count || publication.Process != common[commonStart + index].Process))
                throw new InvalidDataException("Package field authored a foreign or skipped actual Common process publication.");
        }
        if (common is not null && !historical && commonStart + publications.Count != common.Count)
            throw new InvalidDataException("Living package field omitted a reached actual Common cold publication.");
        var ids = new HashSet<Guid>(); long changed = 0; var previousProcess = -1;
        foreach (var observation in fields.Observations)
        {
            if (observation is null || observation.Observation == Guid.Empty || !ids.Add(observation.Observation) ||
                !processes.TryGetValue(observation.Process, out var publicationIndex) || publicationIndex < previousProcess ||
                observation.Epoch != epoch || string.IsNullOrWhiteSpace(observation.Producer) || observation.Changed <= changed ||
                observation.Changed > fields.Changed || !Enum.IsDefined(observation.Target) ||
                (observation.Target & (fields.Slot == FalloutProcessPackageSlot.Current ? FalloutProcessPackageWriterTarget.Current : FalloutProcessPackageWriterTarget.Primary)) == 0)
                throw new InvalidDataException("Package field lost its ordered actual writer/process/epoch publication.");
            if (common is not null && commonIndices is not null)
            {
                var at = commonIndices[observation.Process];
                if (observation.Changed <= common[at].Sequence || at + 1 < common.Count && observation.Changed >= common[at + 1].Sequence)
                    throw new InvalidDataException("Package writer belongs to another actual Common process interval.");
            }
            previousProcess = publicationIndex; changed = observation.Changed;
        }
        if (changed != fields.Changed)
            throw new InvalidDataException("Package field omitted its last actual writer revision.");
        if (fields.InitialMove is { } move && (move.SourceFactory == Guid.Empty || move.DestinationFactory == Guid.Empty ||
            move.SourceFactory == move.DestinationFactory || !factories.Contains(move.SourceFactory) && !factories.Contains(move.DestinationFactory) ||
            move.DestinationStores != 4 || move.SourceStores != 4 || move.SourceBefore != FalloutProcessPackageWords.Constructed ||
            move.DestinationBefore != FalloutProcessPackageWords.Constructed || move.SourceAfter != FalloutProcessPackageWords.Constructed ||
            move.DestinationAfter != FalloutProcessPackageWords.Constructed))
            throw new InvalidDataException("Package field admitted an unowned or foreign four-word source move.");
    }
}
