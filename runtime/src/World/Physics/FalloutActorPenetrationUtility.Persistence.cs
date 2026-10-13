namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutPenetrationFailure(string Operation, string Phase, string ExceptionType, string Message);
internal sealed record FalloutPenetrationSystemSnapshot(string Root, IReadOnlyList<string> Bodies,
    IReadOnlyList<string> Constraints, IReadOnlyList<int> Parents, IReadOnlyList<FalloutPenetrationBone> Bones,
    IReadOnlyList<int> BoneToBodyIndices, ushort CollisionGroup, FalloutPenetrationBounds ConstructionBounds,
    uint PhantomFilter, FalloutPenetrationQuerySnapshot Query, FalloutPenetrationDetectorSnapshot Detector,
    bool Penetrating, bool Retired);
internal sealed record FalloutActorPenetrationSnapshot(int Version, ushort? CollisionGroup,
    long RegistrationCounterTicks, long DetectionDurationTicks, int RetryCount, bool RegistrationRetry, bool Deferred,
    bool Registered, string? Model, string Phase, FalloutPenetrationFailure? Failure,
    IReadOnlyList<FalloutPenetrationSystemSnapshot> Systems, IReadOnlyList<FalloutPenetrationSystemSnapshot> PendingSystems);

internal sealed partial class FalloutActorPenetrationUtility
{
    internal const int CurrentVersion = 1;

    internal FalloutActorPenetrationSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Actor penetration snapshot cannot interrupt a current physical callback.");
        if (_failedColdState is not null) return _failedColdState;
        return new(CurrentVersion, _group, _registrationCounterTicks, _detectionDurationTicks, _retryCount,
            _retry, _deferred, _registered, _modelKey, _phase, _failure,
            Array.AsReadOnly(_systems.Select(CaptureSystem).ToArray()),
            Array.AsReadOnly(_pendingSystems.Select(CaptureSystem).ToArray()));
    }

    internal static FalloutActorPenetrationUtility Restore(FalloutActorPenetrationSnapshot saved,
        Func<IFalloutPenetrationModel?> readCurrentModel, Func<uint?> readCurrentControllerFilter,
        Func<FalloutPenetrationEffectiveSettings> readSettings, IFalloutPenetrationClock clock,
        IFalloutPenetrationBackend backend, Action<string?> validateColdModel,
        Func<string, IFalloutPenetrationBody> bindBody, Func<string, IFalloutPenetrationConstraint> bindConstraint)
    {
        Validate(saved);
        ArgumentNullException.ThrowIfNull(validateColdModel); ArgumentNullException.ThrowIfNull(bindBody);
        ArgumentNullException.ThrowIfNull(bindConstraint);
        var utility = new FalloutActorPenetrationUtility(readCurrentModel, readCurrentControllerFilter, readSettings, clock, backend)
        {
            _group = saved.CollisionGroup,
            _registrationCounterTicks = saved.RegistrationCounterTicks,
            _detectionDurationTicks = saved.DetectionDurationTicks,
            _retryCount = saved.RetryCount,
            _retry = saved.RegistrationRetry,
            _deferred = saved.Deferred,
            _registered = saved.Registered,
            _modelKey = saved.Model,
            _phase = saved.Phase,
            _failure = saved.Failure,
        };
        validateColdModel(saved.Model);
        // A diagnostic failed prefix is retained without replaying constructors,
        // queries, impulses or retired native identities. It cannot resume play.
        if (saved.Failure is not null) { utility._failedColdState = saved; return utility; }
        try
        {
            foreach (var state in saved.Systems)
            {
                var root = bindBody(state.Root);
                var bodies = state.Bodies.Select(bindBody).ToArray();
                var constraints = state.Constraints.Select(bindConstraint).ToArray();
                if (root.Key != state.Root || !bodies.Select(body => body.Key).SequenceEqual(state.Bodies) ||
                    !constraints.Select(constraint => constraint.Key).SequenceEqual(state.Constraints))
                    throw new InvalidDataException("Cold physical binding changed a saved source object identity.");
                var connected = FalloutPenetrationGraph.Validate(bodies, constraints) ??
                    throw new InvalidDataException("Cold connected physical graph changed its retained structural count.");
                var ordered = new FalloutPenetrationOrderedGraph(Array.AsReadOnly(bodies), Array.AsReadOnly(constraints), state.Parents);
                FalloutPenetrationGraph.ValidateOrder(connected, ordered);
                var skeleton = FalloutPenetrationSkeleton.Restore(state.Bones, bodies.Length);
                var construction = new FalloutPenetrationSystemConstruction(root, ordered, skeleton, state.CollisionGroup,
                    state.ConstructionBounds, state.PhantomFilter);
                var instance = backend.Construct(construction);
                var entry = new SystemEntry(construction, instance) { Penetrating = state.Penetrating };
                utility._systems.Add(entry);
                ValidateInstance(entry);
                if (!instance.BoneToBodyIndices.SequenceEqual(state.BoneToBodyIndices))
                    throw new InvalidDataException("Cold physics instance changed its retained bone-to-body mapping.");
                instance.Query.Restore(state.Query);
                entry.Detector.Restore(state.Detector);
            }
            return utility;
        }
        catch
        {
            utility.Dispose(); throw;
        }
    }

    internal static void Validate(FalloutActorPenetrationSnapshot saved)
    {
        if (saved is null || saved.Version != CurrentVersion || saved.RetryCount < 0 || saved.RetryCount >= RegistrationRetryAttempts ||
            string.IsNullOrEmpty(saved.Phase) || saved.Systems is null || saved.PendingSystems is null ||
            (saved.Registered && saved.CollisionGroup is null) ||
            (saved.RegistrationRetry && (saved.RetryCount == 0 || saved.Systems.Count != 0 || saved.Deferred)) ||
            (!saved.RegistrationRetry && saved.RetryCount != 0) ||
            (saved.Failure is null && (saved.PendingSystems.Count != 0 || saved.Systems.Any(system => system.Retired))))
            throw new InvalidDataException("Saved Actor penetration lifetime/retry/publication state is invalid.");
        if (saved.Failure is { } failure && (string.IsNullOrEmpty(failure.Operation) || string.IsNullOrEmpty(failure.Phase) ||
            string.IsNullOrEmpty(failure.ExceptionType) || failure.Message is null))
            throw new InvalidDataException("Saved Actor penetration failure lost its committed callback prefix.");
        foreach (var system in saved.Systems.Concat(saved.PendingSystems))
        {
            if (system is null || string.IsNullOrEmpty(system.Root) || system.Bodies is not { Count: > 0 } ||
                system.Constraints is null || system.Constraints.Count != system.Bodies.Count - 1 ||
                system.Parents is null || system.Parents.Count != system.Bodies.Count || system.Bones is null ||
                system.BoneToBodyIndices is null || system.Query is null || system.Detector is null ||
                system.Bodies.Any(string.IsNullOrEmpty) || system.Constraints.Any(string.IsNullOrEmpty) ||
                system.Bodies.Distinct(StringComparer.Ordinal).Count() != system.Bodies.Count ||
                system.Constraints.Distinct(StringComparer.Ordinal).Count() != system.Constraints.Count ||
                system.PhantomFilter != ((uint)system.CollisionGroup << 16 | PhantomFilterLayer) ||
                system.Query.CollisionFilter != system.PhantomFilter)
                throw new InvalidDataException("Saved Actor penetration system changed its source graph or query filter.");
            var skeleton = FalloutPenetrationSkeleton.Restore(system.Bones, system.Bodies.Count);
            for (var index = 0; index < system.Parents.Count; index++)
                if (skeleton.Bones[index].Parent != system.Parents[index])
                    throw new InvalidDataException("Saved physical skeleton changed its retained structural parent mapping.");
            if (system.BoneToBodyIndices.Count != skeleton.Bones.Count ||
                system.BoneToBodyIndices.Any(index => index < -1 || index >= system.Bodies.Count))
                throw new InvalidDataException("Saved Actor penetration system changed its actual bone-to-body map.");
            new FalloutPenetrationDetector(skeleton).Restore(system.Detector);
        }
    }

    private static FalloutPenetrationSystemSnapshot CaptureSystem(SystemEntry system)
    {
        if (system.Retired) return system.RetiredSnapshot ??
            throw new InvalidOperationException("Retired Actor penetration system lost its historical cold receipt.");
        return new(system.Root.Key, Array.AsReadOnly(system.Graph.Bodies.Select(body => body.Key).ToArray()),
            Array.AsReadOnly(system.Graph.Constraints.Select(constraint => constraint.Key).ToArray()),
            Array.AsReadOnly(system.Graph.Parents.ToArray()), Array.AsReadOnly(system.Skeleton.Bones.ToArray()),
            Array.AsReadOnly(system.Instance.BoneToBodyIndices.ToArray()), system.Construction.CollisionGroup,
            system.Construction.Bounds, system.Construction.PhantomFilter, system.Instance.Query.Capture(),
            system.Detector.Capture(), system.Penetrating, false);
    }
}
