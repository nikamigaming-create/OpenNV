using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Source creation ownership is separate from hearing/search/alert consumption.
internal enum FalloutDetectionProcessLevel { Base, Low, MiddleLow, MiddleHigh, High }

internal sealed record FalloutDetectionEventRequest(FalloutFormKey Owner, FalloutFormKey Location,
    FalloutFormKey Cell, IReadOnlyList<float> Position, int SoundLevel, int RequestedType = 3,
    float ObservedSeconds = 0)
{
    internal void Validate(Func<FalloutFormKey, bool> actor, Func<FalloutFormKey, bool> reference,
        Func<FalloutFormKey, bool> cell)
    {
        if (!actor(Owner) || !reference(Location) || !cell(Cell) ||
            Position is not { Count: 3 } || Position.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(ObservedSeconds) || ObservedSeconds < 0)
            throw new InvalidDataException("Detection event has an invalid source owner, location or position.");
    }
}

internal sealed record FalloutDetectionEventState(FalloutDetectionEventRequest Request,
    float CreatedSeconds, long Revision, string ConsumerBoundary, int InitialAuxiliaryValue = -1);

internal sealed record FalloutDetectionEventsSnapshot(float SimulationSeconds, long Revision,
    IReadOnlyList<FalloutDetectionEventState> Events);

internal sealed class FalloutDetectionEvents
{
    internal const string UnboundConsumer = "Source event hearing, detection and alert/search continuation have no runtime owner.";
    private readonly Dictionary<FalloutFormKey, FalloutDetectionEventState> _events = [];
    private readonly Func<FalloutFormKey, bool> _actor, _reference, _cell;
    private readonly int _sourceActorBound;
    private readonly Func<float> _expireSeconds;
    private float _simulationSeconds;
    private long _revision;
    private Func<FalloutFormKey, FalloutDetectionProcessLevel?>? _process;
    private Func<FalloutFormKey, FalloutReferencePlacement>? _location;
    internal float SimulationSeconds => _simulationSeconds;
    internal void ValidateRequest(FalloutDetectionEventRequest request) => request.Validate(_actor, _reference, _cell);
    internal bool CanCreate(FalloutDetectionEventRequest request) => _process?.Invoke(request.Owner) is { } level && Enum.IsDefined(level);
    internal object State => new
    {
        simulationSeconds = _simulationSeconds,
        revision = _revision,
        pending = Capture().Events,
        consumerBoundary = UnboundConsumer
    };

    internal void Bind(Func<FalloutFormKey, FalloutReferencePlacement> location,
        Func<FalloutFormKey, FalloutDetectionProcessLevel?>? process)
    { _location = location; _process = process; }

    internal FalloutDetectionEventRequest Prepare(FalloutFormKey owner, FalloutFormKey location,
        int soundLevel, int requestedType)
    {
        var placement = (_location ?? throw new NotSupportedException("Detection location has no authoritative spatial owner."))(location);
        placement.Validate();
        var result = new FalloutDetectionEventRequest(owner, location, placement.Cell,
            placement.Position.ToArray(), soundLevel, requestedType, _simulationSeconds);
        result.Validate(_actor, _reference, _cell);
        return result;
    }

    internal FalloutDetectionEventState? Create(FalloutDetectionEventRequest request, string command, int argumentCount)
    {
        if (_process is null)
            throw new NotSupportedException($"Reached native script command {command} ({argumentCount} arguments) has no owner.");
        var process = _process(request.Owner) ?? throw new NotSupportedException("Detection actor process level has no authoritative owner.");
        return Create(request, process);
    }

    // Validators must bind the winning selected records. The bound is the
    // complete selected actor-reference domain including the reserved player.
    internal FalloutDetectionEvents(float sourceExpireSeconds, int sourceActorBound,
        Func<FalloutFormKey, bool> actor, Func<FalloutFormKey, bool> reference,
        Func<FalloutFormKey, bool> cell, float initialSimulationSeconds = 0)
        : this(() => sourceExpireSeconds, sourceActorBound, actor, reference, cell, initialSimulationSeconds) { }

    internal FalloutDetectionEvents(Func<float> sourceExpireSeconds, int sourceActorBound,
        Func<FalloutFormKey, bool> actor, Func<FalloutFormKey, bool> reference,
        Func<FalloutFormKey, bool> cell, float initialSimulationSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(sourceExpireSeconds);
        if (!float.IsFinite(sourceExpireSeconds()) || sourceActorBound <= 0 ||
            !float.IsFinite(initialSimulationSeconds) || initialSimulationSeconds < 0)
            throw new InvalidDataException("Detection lifetime or source actor domain is invalid.");
        _expireSeconds = sourceExpireSeconds; _sourceActorBound = sourceActorBound;
        _actor = actor; _reference = reference; _cell = cell;
        _simulationSeconds = initialSimulationSeconds;
    }

    internal FalloutDetectionEventState? Create(FalloutDetectionEventRequest request,
        FalloutDetectionProcessLevel process)
    {
        request.Validate(_actor, _reference, _cell);
        if (request.ObservedSeconds > _simulationSeconds)
            throw new InvalidDataException("Detection request belongs to a future simulation clock.");
        if (!Enum.IsDefined(process)) throw new InvalidDataException("Detection process level is invalid.");
        // This distinction is proven source behavior. Never infer process level
        // merely from an existing reference or use it as an absent-native escape.
        if (process != FalloutDetectionProcessLevel.High) return null;
        if (!_events.ContainsKey(request.Owner) && _events.Count >= _sourceActorBound)
            throw new InvalidDataException("Detection event domain exceeds selected source actor references.");
        var created = new FalloutDetectionEventState(request with { Position = request.Position.ToArray() },
            request.ObservedSeconds, checked(_revision + 1), UnboundConsumer);
        _events[request.Owner] = created; _revision = created.Revision;
        return created;
    }

    internal void Advance(float simulationSeconds)
    {
        if (!float.IsFinite(simulationSeconds) || simulationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
        var next = _simulationSeconds + simulationSeconds;
        if (!float.IsFinite(next)) throw new InvalidDataException("Detection simulation clock overflowed.");
        var expireSeconds = _expireSeconds();
        if (!float.IsFinite(expireSeconds)) throw new InvalidDataException("Detection source lifetime is non-finite.");
        _simulationSeconds = next;
        foreach (var owner in _events.Where(pair =>
                     (double)(_simulationSeconds - pair.Value.CreatedSeconds) > expireSeconds)
                     .Select(pair => pair.Key).ToArray()) _events.Remove(owner);
        // Receiver divergence must also remain in the runtime divergence owner;
        // expiration of the source event never means its missing consumer ran.
    }

    internal void RequireConsumer(FalloutFormKey owner)
    {
        if (_events.TryGetValue(owner, out var value))
            throw new NotSupportedException(value.ConsumerBoundary);
    }

    internal FalloutDetectionEventsSnapshot Capture() => new(_simulationSeconds, _revision,
        _events.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => pair.Value with
            {
                Request = pair.Value.Request with
                { Position = pair.Value.Request.Position.ToArray() }
            }).ToArray());

    internal void Restore(FalloutDetectionEventsSnapshot snapshot)
    {
        if (!float.IsFinite(snapshot.SimulationSeconds) || snapshot.SimulationSeconds < 0 ||
            snapshot.Revision < 0 || snapshot.Events is null || snapshot.Events.Count > _sourceActorBound)
            throw new InvalidDataException("Saved detection clock or finite event domain is invalid.");
        var restored = new Dictionary<FalloutFormKey, FalloutDetectionEventState>();
        var revisions = new HashSet<long>();
        foreach (var value in snapshot.Events)
        {
            if (value is null || value.Request is null) throw new InvalidDataException("Saved detection event is absent.");
            value.Request.Validate(_actor, _reference, _cell);
            if (!float.IsFinite(value.CreatedSeconds) || value.CreatedSeconds < 0 ||
                value.CreatedSeconds > snapshot.SimulationSeconds || value.Revision <= 0 ||
                value.Revision > snapshot.Revision || !revisions.Add(value.Revision) ||
                value.Request.ObservedSeconds != value.CreatedSeconds || value.InitialAuxiliaryValue != -1 ||
                value.ConsumerBoundary != UnboundConsumer ||
                !restored.TryAdd(value.Request.Owner, value with
                { Request = value.Request with { Position = value.Request.Position.ToArray() } }))
                throw new InvalidDataException("Saved detection event is duplicated or has an invalid clock, receipt or consumer.");
        }
        // Source lifetime is evaluated on actual simulation advance, including
        // when a changed winning setting makes a restored pending event expire.
        _events.Clear(); foreach (var (owner, value) in restored) _events.Add(owner, value);
        _simulationSeconds = snapshot.SimulationSeconds; _revision = snapshot.Revision;
    }
}
