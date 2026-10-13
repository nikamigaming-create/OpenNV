namespace OpenNV.Runtime.World.Physics;

// Actor owns this utility independently of movement, its controller and death
// ragdolls. Every nonempty effect goes back to the same current physical graph.
internal sealed partial class FalloutActorPenetrationUtility : IDisposable
{
    internal const uint PhantomFilterLayer = 30, PenetrationFilterFlag = 0x4000;
    private const byte KeyframedMotion = 6, FixedMotion = 7;
    private const int RegistrationRetryAttempts = 6;
    private readonly Func<IFalloutPenetrationModel?> _readCurrentModel;
    private readonly Func<uint?> _readCurrentControllerFilter;
    private readonly Func<FalloutPenetrationEffectiveSettings> _readSettings;
    private readonly IFalloutPenetrationClock _clock;
    private readonly IFalloutPenetrationBackend _backend;
    private readonly List<SystemEntry> _systems = [];
    private readonly List<SystemEntry> _pendingSystems = [];
    private ushort? _group;
    private long _registrationCounterTicks, _detectionDurationTicks;
    private int _retryCount;
    private bool _retry, _deferred, _registered, _busy, _disposed;
    private string? _modelKey;
    private string _phase = "constructed";
    private FalloutPenetrationFailure? _failure;
    private FalloutActorPenetrationSnapshot? _failedColdState;

    internal FalloutActorPenetrationUtility(Func<IFalloutPenetrationModel?> readCurrentModel,
        Func<uint?> readCurrentControllerFilter, Func<FalloutPenetrationEffectiveSettings> readSettings,
        IFalloutPenetrationClock clock, IFalloutPenetrationBackend backend)
    {
        ArgumentNullException.ThrowIfNull(readCurrentModel); ArgumentNullException.ThrowIfNull(readCurrentControllerFilter);
        ArgumentNullException.ThrowIfNull(readSettings); ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(backend);
        _readCurrentModel = readCurrentModel; _readCurrentControllerFilter = readCurrentControllerFilter;
        _readSettings = readSettings; _clock = clock; _backend = backend;
    }

    internal bool Registered => _registered;
    internal bool RegistrationRetry => _retry;
    internal int SystemCount => _failedColdState?.Systems.Count ?? _systems.Count;
    internal ushort? CollisionGroup => _group;
    internal string? SaveBlocker => _busy ? "actor-penetration-invocation-active" :
        _failure is not null ? "actor-penetration-" + _failure.Phase :
        _pendingSystems.Count != 0 ? "actor-penetration-system-publication-incomplete" : null;

    internal void Register()
    {
        Enter("registration");
        try
        {
            _phase = "current-model-read";
            var model = _readCurrentModel();
            _modelKey = model?.Key;
            _phase = "current-controller-filter-read";
            var filter = _readCurrentControllerFilter();
            _group = (ushort)((filter ?? 0) >> 16);
            _deferred = false;
            if (model is not null)
                foreach (var scene in FalloutPenetrationGraph.Walk(model.Roots))
                {
                    if (scene.Body is not { } root || !FalloutPenetrationGraph.IsCandidateRoot(root)) continue;
                    _phase = "connected-body-collection";
                    var collected = FalloutPenetrationGraph.Collect(scene);
                    if (collected.Constraints.Count == 0) continue;
                    // Source reuse checks only root identity and retained count.
                    // It deliberately does not compare each current body identity.
                    var existing = _systems.FirstOrDefault(system => ReferenceEquals(system.Root, root) &&
                        system.Instance.Bodies.Count == collected.Bodies.Count);
                    if (existing is not null) { _pendingSystems.Add(existing); continue; }
                    _phase = "connected-graph-validation";
                    var connected = FalloutPenetrationGraph.Validate(collected.Bodies, collected.Constraints);
                    if (connected is null) { _deferred = true; continue; }
                    _phase = "source-depth-ordering";
                    var ordered = _backend.OrderBySourceDepth(connected);
                    FalloutPenetrationGraph.ValidateOrder(connected, ordered);
                    _phase = "physical-skeleton-construction";
                    var skeleton = FalloutPenetrationSkeleton.Construct(ordered, _backend);
                    var construction = new FalloutPenetrationSystemConstruction(root, ordered, skeleton, _group.Value,
                        Cover(ordered.Bodies), ((uint)_group.Value << 16) | PhantomFilterLayer);
                    _phase = "physical-system-construction";
                    var instance = _backend.Construct(construction);
                    var system = new SystemEntry(construction, instance);
                    _pendingSystems.Add(system);
                    ValidateInstance(system);
                    _phase = "detection-setting-read";
                    var settings = _readSettings();
                    var milliseconds = FalloutPhysicalFloat32.Multiply(
                        BitConverter.UInt32BitsToSingle(settings.DetectionSecondsBits),
                        FalloutPenetrationClockSource.MillisecondsPerSecond);
                    _phase = "registration-counter-read";
                    _registrationCounterTicks = _clock.ReadCounterTicks();
                    _phase = "detection-scale-read";
                    var durationTicks = FalloutPhysicalFloat32.Multiply(milliseconds,
                        BitConverter.UInt32BitsToSingle(_clock.ReadTicksPerMillisecondBits()));
                    _phase = "detection-duration-conversion";
                    _detectionDurationTicks = _clock.ConvertDurationTicks(BitConverter.SingleToUInt32Bits(durationTicks));
                }
            _phase = "unmatched-system-retirement";
            var retained = new HashSet<SystemEntry>(_pendingSystems, ReferenceEqualityComparer.Instance);
            foreach (var previous in _systems)
                if (!retained.Contains(previous)) previous.Dispose();
            _systems.Clear(); _systems.AddRange(_pendingSystems); _pendingSystems.Clear();
            _registered = true;
            if (_systems.Count == 0 && !_deferred)
            {
                _retry = true;
                if (++_retryCount == RegistrationRetryAttempts) { _retry = false; _retryCount = 0; }
            }
            else { _retry = false; _retryCount = 0; }
            _phase = "registration-completed";
        }
        catch (Exception error) { Fail("registration", error); throw; }
        finally { _busy = false; }
    }

    internal void Update()
    {
        Enter("update");
        try
        {
            if (_systems.Count == 0) { _phase = "zero-system-update-completed"; return; }
            var group = _group ?? throw new InvalidOperationException("Registered penetration utility has an unwritten collision group.");
            for (var index = 0; index < _systems.Count; index++)
            {
                var system = _systems[index];
                var bodies = system.Instance.Bodies;
                _phase = "system-motion-read";
                var excluded = bodies.Any(body => body.Motion is KeyframedMotion or FixedMotion);
                _phase = "system-world-read";
                var world = bodies[0].WorldKey;
                var skip = excluded || world is null;
                if (!skip && !system.Penetrating)
                {
                    _phase = "system-first-clock-read";
                    skip = unchecked(_clock.ReadCounterTicks() - _registrationCounterTicks) > _detectionDurationTicks;
                }
                if (skip)
                {
                    if (world is not null) { _phase = "skipped-system-detector-reset"; system.Detector.Reset(); }
                    continue;
                }
                _phase = "query-world-binding";
                if (system.Instance.Query.WorldKey != world) system.Instance.Query.BindWorld(world);
                _phase = "system-second-clock-read";
                var elapsed = unchecked(_clock.ReadCounterTicks() - _registrationCounterTicks);
                if (elapsed <= _detectionDurationTicks || system.Penetrating) system.Detector.Reset();
                _phase = "physical-pose-sampling";
                var pose = system.Instance.SampleBonePositions(system.Skeleton);
                var previous = system.Penetrating;
                _phase = "penetration-detection";
                system.Penetrating = system.Detector.Detect(pose, system.Instance.Query, group);
                for (var bone = 0; bone < system.Detector.States.Count; bone++)
                {
                    var bodyIndex = system.Instance.BoneToBodyIndices[bone];
                    if (bodyIndex == -1) continue;
                    var body = bodies[bodyIndex];
                    var state = system.Detector.States[bone];
                    _phase = "body-filter-publication";
                    SetFilter(body, state != 0);
                    if (state != FalloutPenetrationDetector.Affected) continue;
                    _phase = "body-point-impulse";
                    FalloutPenetrationImpulse.Apply(body, bone, system.Detector.Contacts, _readSettings(), _backend);
                }
                if (previous && !system.Penetrating)
                {
                    _phase = "resolved-penetration-detector-reset";
                    system.Detector.Reset();
                    foreach (var bodyIndex in system.Instance.BoneToBodyIndices)
                        if (bodyIndex != -1) { _phase = "resolved-penetration-filter-clear"; SetFilter(bodies[bodyIndex], false); }
                }
            }
            _phase = "update-completed";
        }
        catch (Exception error) { Fail("update", error); throw; }
        finally { _busy = false; }
    }

    private void Enter(string operation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Actor penetration utility cannot reenter an active physical callback.");
        if (_failure is not null) throw new InvalidOperationException("Actor penetration utility retains a failed " + _failure.Phase + " prefix.");
        if (_pendingSystems.Count != 0) throw new InvalidOperationException("Actor penetration utility retains incomplete system publication.");
        _busy = true; _phase = operation;
    }

    private void Fail(string operation, Exception error) =>
        _failure ??= new(operation, _phase, error.GetType().FullName ?? error.GetType().Name, error.Message);

    private static void SetFilter(IFalloutPenetrationBody body, bool enabled)
    {
        var previous = body.CollisionFilter;
        var next = enabled ? previous | PenetrationFilterFlag : previous & ~PenetrationFilterFlag;
        if (next != previous) body.PublishCollisionFilter(next);
    }

    private static void ValidateInstance(SystemEntry system)
    {
        if (system.Instance is null || system.Instance.Bodies.Count != system.Graph.Bodies.Count ||
            system.Instance.BoneToBodyIndices.Count != system.Skeleton.Bones.Count || system.Instance.Query is null)
            throw new InvalidDataException("Constructed penetration system has no complete physical body/query/bone mapping.");
        for (var index = 0; index < system.Graph.Bodies.Count; index++)
            if (!ReferenceEquals(system.Instance.Bodies[index], system.Graph.Bodies[index]))
                throw new InvalidDataException("Constructed penetration system changed its actual ordered current bodies.");
        foreach (var index in system.Instance.BoneToBodyIndices)
            if (index < -1 || index >= system.Instance.Bodies.Count)
                throw new InvalidDataException("Constructed penetration system has an invalid actual bone-to-body map.");
    }

    private static FalloutPenetrationBounds Cover(IReadOnlyList<IFalloutPenetrationBody> bodies)
    {
        if (bodies.Count == 0) throw new InvalidDataException("Physical system query has no current body bounds.");
        var bounds = bodies[0].ReadBounds();
        foreach (var body in bodies.Skip(1))
        {
            var next = body.ReadBounds();
            bounds = new(FalloutPhysicalVector3.FromValues(MathF.Min(bounds.Minimum.XValue, next.Minimum.XValue),
                MathF.Min(bounds.Minimum.YValue, next.Minimum.YValue), MathF.Min(bounds.Minimum.ZValue, next.Minimum.ZValue)),
                FalloutPhysicalVector3.FromValues(MathF.Max(bounds.Maximum.XValue, next.Maximum.XValue),
                    MathF.Max(bounds.Maximum.YValue, next.Maximum.YValue), MathF.Max(bounds.Maximum.ZValue, next.Maximum.ZValue)));
        }
        return bounds;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_busy) throw new InvalidOperationException("Actor penetration retirement cannot interrupt a current physical callback.");
        _busy = true;
        try
        {
            var visited = new HashSet<SystemEntry>(ReferenceEqualityComparer.Instance);
            foreach (var system in _systems.Concat(_pendingSystems))
                if (visited.Add(system)) { _phase = "system-retirement"; system.Dispose(); }
            _systems.Clear(); _pendingSystems.Clear(); _disposed = true;
        }
        catch (Exception error) { Fail("retirement", error); throw; }
        finally { _busy = false; }
    }

    private sealed class SystemEntry(FalloutPenetrationSystemConstruction construction,
        IFalloutPenetrationSystem instance) : IDisposable
    {
        internal FalloutPenetrationSystemConstruction Construction { get; } = construction;
        internal IFalloutPenetrationBody Root => Construction.Root;
        internal FalloutPenetrationOrderedGraph Graph => Construction.Graph;
        internal FalloutPenetrationSkeleton Skeleton => Construction.Skeleton;
        internal IFalloutPenetrationSystem Instance { get; } = instance;
        internal FalloutPenetrationDetector Detector { get; } = new(construction.Skeleton);
        internal bool Penetrating { get; set; }
        internal bool Retired { get; private set; }
        internal FalloutPenetrationSystemSnapshot? RetiredSnapshot { get; private set; }
        public void Dispose()
        {
            if (Retired) return;
            RetiredSnapshot = CaptureSystem(this) with { Retired = true };
            Instance.Dispose(); Retired = true;
        }
    }
}
