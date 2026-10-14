using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutActorMoverState
{
    internal static void Validate(FalloutActorMoverSnapshot saved, FalloutActorMoverSource source, string stack,
        IReadOnlyList<FalloutPlayerPointOfViewLifetime>? sceneLineage = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.Schema != Schema || saved.Source != source || saved.Stack != stack || saved.Process == Guid.Empty ||
            saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty || saved.Values is null ||
            saved.Values.InheritedVector is null || saved.Values.SecondaryVector is null || saved.Values.Player is null ||
            saved.ConstructorStores is null || saved.Availability is null || saved.Publications is not { Count: > 0 } ||
            saved.Operations is null || !saved.ConstructorStores.SequenceEqual(InitialStores()))
            throw new InvalidDataException("Actor mover omitted its source constructor, cells or complete cold history.");
        if (saved.Actor is { } actor) { actor.Validate(); if (!actor.EnginePlayer) throw new InvalidDataException("Player mover borrowed another Actor class."); }
        var publications = new Dictionary<(Guid, Guid), FalloutActorMoverPublication>();
        var processes = new HashSet<Guid>(); var constructors = new HashSet<Guid>();
        var parentLifetimes = sceneLineage?.Select(parent => (parent.Process, parent.Constructor)).ToHashSet();
        for (var index = 0; index < saved.Publications.Count; ++index)
        {
            var publication = saved.Publications[index];
            if (publication is null || publication.Process == Guid.Empty || publication.Factory == Guid.Empty ||
                publication.Constructor == Guid.Empty || !processes.Add(publication.Process) || !constructors.Add(publication.Constructor) ||
                !publications.TryAdd((publication.Process, publication.Factory), publication) ||
                (index == 0 ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != saved.Publications[index - 1].Process ||
                    publication.PreviousFactory != saved.Publications[index - 1].Factory) ||
                parentLifetimes is not null && !parentLifetimes.Contains((publication.Process, publication.Constructor)))
                throw new InvalidDataException("Actor mover cold publication lost its actual complete scene lifetime lineage.");
        }
        if (saved.Publications[^1].Process != saved.Process || saved.Publications[^1].Factory != saved.Factory ||
            saved.Publications[^1].Constructor != saved.Constructor)
            throw new InvalidDataException("Actor mover current fields belong to another cold factory.");
        var values = ConstructedValues(); var sequence = InitialStores()[^1].Changed;
        var availability = Enum.GetValues<FalloutActorMoverLane>().ToDictionary(lane => lane, lane => new FalloutActorMoverAvailability(lane, true, null));
        var observations = new HashSet<(Guid, string, FalloutActorMoverLane?)>();
        foreach (var operation in saved.Operations)
        {
            if (operation is null || operation.Invocation is null || !publications.ContainsKey((operation.Process, operation.Factory)) ||
                saved.Actor is null || operation.Invocation.Actor != saved.Actor || operation.Invocation.SourceCommand == Guid.Empty ||
                operation.Invocation.Process == Guid.Empty || operation.Invocation.Epoch < 1 || operation.Invocation.ThreadId < 1 ||
                operation.Entered != sequence + 1 || operation.Stores is null ||
                !observations.Add((operation.Invocation.SourceCommand, operation.Kind, operation.InvalidatedLane)) ||
                operation.Returned != (operation.Failure is null) || operation.Failure is { } error && string.IsNullOrWhiteSpace(error))
                throw new InvalidDataException("Actor mover lost a genuine caller, returned prefix or ordered current writer.");
            sequence = operation.Entered;
            if (operation.Kind == PackageResetKind)
            {
                if (operation.InvalidatedLane is not null || operation.InvalidationBoundary is not null || operation.Stores.Count > 3 ||
                    operation.Returned && (operation.Join is null || operation.Stores.Count != 3) ||
                    operation.Stores.Count != 0 && (operation.Join is null || !availability[FalloutActorMoverLane.DefaultVector].Available))
                    throw new InvalidDataException("Actor mover reset admitted a missing predecessor/default producer or later store.");
                if (operation.Join is { } join) ValidateJoin(join, operation.Invocation, source, stack);
                FalloutActorMoverField[] order = [FalloutActorMoverField.InheritedVectorXY, FalloutActorMoverField.InheritedVectorZ, FalloutActorMoverField.ResetByte];
                for (var index = 0; index < operation.Stores.Count; ++index)
                {
                    var store = operation.Stores[index];
                    if (store is null || store.Field != order[index] || store.Changed != ++sequence || store.Bits != 0 ||
                        store.SecondBits != (index == 0 ? 0u : null))
                        throw new InvalidDataException("Actor mover reset changed source XY/Z/byte store order or values.");
                    values = Apply(values, store);
                    if (store.Field == FalloutActorMoverField.InheritedVectorZ) availability[FalloutActorMoverLane.InheritedVector] = new(FalloutActorMoverLane.InheritedVector, true, null);
                    if (store.Field == FalloutActorMoverField.ResetByte) availability[FalloutActorMoverLane.ResetByte] = new(FalloutActorMoverLane.ResetByte, true, null);
                }
            }
            else if (operation.Kind == UnownedWriterKind)
            {
                if (operation.Join is not null || operation.Stores.Count != 0 ||
                    operation.InvalidatedLane is { } lane && (!Enum.IsDefined(lane) || string.IsNullOrWhiteSpace(operation.InvalidationBoundary)) ||
                    operation.InvalidatedLane is null && (operation.Returned || operation.InvalidationBoundary is not null))
                    throw new InvalidDataException("Actor mover unknown writer changed cells or omitted its specific refusal.");
                if (operation.InvalidatedLane is { } invalidated) availability[invalidated] = new(invalidated, false, operation.InvalidationBoundary);
            }
            else throw new InvalidDataException("Actor mover history contains an unowned operation.");
            if (operation.Changed != ++sequence) throw new InvalidDataException("Actor mover operation omitted its returned/failed publication.");
        }
        if (saved.Sequence != sequence || saved.Values != values || saved.Availability.Count != availability.Count ||
            saved.Availability.Any(value => value is null || !availability.TryGetValue(value.Lane, out var expected) || value != expected) ||
            saved.Availability.Select(value => value.Lane).Distinct().Count() != availability.Count)
            throw new InvalidDataException("Actor mover current values or availability disagree with committed writer history.");
        ValidatePlayerFrame(saved, source, stack, publications);
    }

    private static void ValidateJoin(FalloutActorMoverResetJoin join, FalloutPackageCommandFieldInvocation invocation,
        FalloutActorMoverSource source, string stack)
    {
        if (join is null || join.PackageStore is not { } package || join.LinkedReference is not { } linked || join.HighReset is not { } high ||
            package.Invocation != invocation || package.Step != FalloutPackageStateStep.Returned || package.Failure is not null ||
            package.Operation != FalloutPackageStateOperation.Set || package.RequestedPackage is null || package.Factory == Guid.Empty || package.Stores is null ||
            !Enum.IsDefined(package.Slot) || linked.Child != FalloutActorMoverResetChild.LinkedReference ||
            high.Child != FalloutActorMoverResetChild.HighReset || linked.Identity == Guid.Empty || high.Identity == Guid.Empty ||
            linked.Identity == high.Identity || linked.Invocation != invocation || high.Invocation != invocation ||
            !linked.Returned || !high.Returned || linked.Failure is not null || high.Failure is not null ||
            package.Changed < package.Entered || linked.Entered < package.Changed || linked.Changed < linked.Entered ||
            high.Entered < linked.Changed || high.Changed < high.Entered ||
            !Current(package.Process) || !Current(linked.Process) || !Current(high.Process) ||
            linked.Process.GameplayOwnership != package.Process.GameplayOwnership || high.Process.GameplayOwnership != linked.Process.GameplayOwnership)
            throw new InvalidDataException("Actor mover reset lost its actual Current/Primary setter, linked-reference and High reset return order.");
        bool Current(FalloutProcessSceneQuery? process) => process is not null && process.Process == invocation.Process &&
            process.Actor == invocation.Actor && process.Epoch == invocation.Epoch && process.Level == FalloutDetectionProcessLevel.High &&
            process.Stack == stack && process.ExecutableSha256 == source.Controls.Main.EngineSha256 && process.GameplayOwnership != Guid.Empty;
    }
    private static FalloutActorMoverValues ConstructedValues() => new(FalloutActorMoverVector.Default, FalloutActorMoverVector.Default,
        FalloutActorMoverSource.InitialInheritedFlags, 0, FalloutPlayerMoverFields.Constructed);
    private static FalloutActorMoverStore[] InitialStores() =>
    [
        new(FalloutActorMoverField.InheritedVectorXY, 1, 0, 0), new(FalloutActorMoverField.InheritedVectorZ, 2, 0),
        new(FalloutActorMoverField.SecondaryVectorXY, 3, 0, 0), new(FalloutActorMoverField.SecondaryVectorZ, 4, 0),
        new(FalloutActorMoverField.InheritedFlags, 5, FalloutActorMoverSource.InitialInheritedFlags), new(FalloutActorMoverField.ResetWord, 6, 0),
        new(FalloutActorMoverField.PlayerVectorXY, 7, 0, 0), new(FalloutActorMoverField.PlayerVectorZ, 8, 0),
        new(FalloutActorMoverField.PlayerFlags, 9, 0), new(FalloutActorMoverField.PlayerTimer, 10, FalloutActorMoverSource.InitialPlayerTimerBits),
        new(FalloutActorMoverField.PlayerSecondaryFlags, 11, 0)
    ];
    private static FalloutActorMoverValues Apply(FalloutActorMoverValues values, FalloutActorMoverStore store) => store.Field switch
    {
        FalloutActorMoverField.InheritedVectorXY => values with { InheritedVector = values.InheritedVector with { X = store.Bits, Y = store.SecondBits!.Value } },
        FalloutActorMoverField.InheritedVectorZ => values with { InheritedVector = values.InheritedVector with { Z = store.Bits } },
        FalloutActorMoverField.ResetByte => values with { ResetByte = checked((byte)store.Bits) },
        FalloutActorMoverField.PlayerFlags => values with { Player = values.Player with { Flags = store.Bits } },
        FalloutActorMoverField.PlayerVectorXY => values with { Player = values.Player with { VectorX = store.Bits, VectorY = store.SecondBits!.Value } },
        FalloutActorMoverField.PlayerVectorZ => values with { Player = values.Player with { VectorZ = store.Bits } },
        _ => throw new InvalidDataException("Actor mover current writer has no owned field-store implementation.")
    };
    private static FalloutActorMoverResetJoin CloneJoin(FalloutActorMoverResetJoin join) => join with
    { PackageStore = join.PackageStore with { Stores = Array.AsReadOnly(join.PackageStore.Stores.ToArray()) } };
    private static FalloutActorMoverOperation Clone(FalloutActorMoverOperation operation) => operation with
    { Stores = Array.AsReadOnly(operation.Stores.ToArray()), Join = operation.Join is { } join ? CloneJoin(join) : null };
}
