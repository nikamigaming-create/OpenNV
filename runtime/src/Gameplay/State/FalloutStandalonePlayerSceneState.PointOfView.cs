using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal const string PointOfViewCameraBoundary = "source-Player-POV-current-camera-transform-child-unbound";
    // These are separate source fields. _view selects the model; this byte
    // requests a camera mode. The scalar is independently reset by the real
    // Player constructor and by the flag setter's returned null-model arm.
    private byte _desiredCameraView;
    private uint _sharedCameraScalar;
    private long _desiredCameraViewStores, _sharedCameraScalarStores;
    private readonly List<FalloutPlayerPointOfViewCall> _pointOfViewCalls = [];
    private readonly HashSet<Guid> _pointOfViewIds = [];
    private readonly List<FalloutPlayerPointOfViewLifetime> _pointOfViewLifetimes = [];
    private readonly FalloutPlayerPointOfViewSource _pointOfViewSource;

    internal FalloutPlayerPointOfViewCall DisableControlPointOfView(Guid observation)
    {
        RequireControlPointOfViewCommand(observation);
        if (!_pointOfViewIds.Add(observation)) throw new InvalidOperationException("Player POV replayed its actual command observation.");
        var fault = _reentry; var entered = Next();
        var call = new FalloutPlayerPointOfViewCall(observation, _process, _constructor, entered, entered,
            FalloutPlayerPointOfViewStep.FirstPersonField, false, null, _desiredCameraView, _sharedCameraScalar, null, null, false, null);
        try
        {
            FalloutSourceSceneNode? current = null;
            if (_first is { Released: null } first)
            {
                if (first.Publication.Process != _process || _livingFirst is null || !ReadLiving(_livingFirst))
                    throw new NotSupportedException("source-Player-POV-current-first-person-factory-rebind-unbound");
                current = new(_process, first.Publication.Factory, first.Publication.NativeRoot);
            }
            Check(); call = call with { GetterReturned = true, FirstPerson = current, Changed = Next() };
            _desiredCameraView = 0; _desiredCameraViewStores = checked(_desiredCameraViewStores + 1);
            var desired = Next();
            call = call with { DesiredViewStored = desired, Step = FalloutPlayerPointOfViewStep.DesiredViewStore, Changed = desired };
            if (current is not null)
            {
                call = call with { Step = FalloutPlayerPointOfViewStep.CameraChild, Changed = Next() };
                throw new NotSupportedException(PointOfViewCameraBoundary);
            }
            _sharedCameraScalar = 0; _sharedCameraScalarStores = checked(_sharedCameraScalarStores + 1);
            var scalar = Next();
            call = call with { SharedScalarStored = scalar, Step = FalloutPlayerPointOfViewStep.SharedScalarStore, Changed = scalar };
            Check(); call = call with { Step = FalloutPlayerPointOfViewStep.Returned, Returned = true, Changed = Next() };
            return call;
        }
        catch (Exception error) { call = call with { Failure = error.Message, Changed = Next() }; throw; }
        finally { _pointOfViewCalls.Add(call); }
        void Check()
        {
            RequireControlPointOfViewCommand(observation);
            if (_reentry != fault) throw new InvalidOperationException("Player POV swallowed source field/lifetime reentry.");
        }
    }

    private void RequireControlPointOfViewCommand(Guid observation)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        if (!_writing || _thread is null || (_controlFields.ReadForEffects(observation) & 0x10) == 0)
            throw new InvalidOperationException("Player POV requires the exact entered source flag writer and current input thread.");
    }

    internal FalloutPlayerPointOfViewCall? CaptureControlPointOfViewCall(Guid observation)
    {
        RequireThread();
        var call = _pointOfViewCalls.LastOrDefault();
        return call?.Observation == observation ? call : null;
    }

    private FalloutPlayerPointOfViewSnapshot CaptureControlPointOfView()
    {
        RequireThread();
        return new(_pointOfViewSource,
            _stack, _process, _constructor, _desiredCameraView, _sharedCameraScalar,
            _desiredCameraViewStores, _sharedCameraScalarStores, _pointOfViewCalls.ToArray(), _pointOfViewLifetimes.ToArray());
    }

    private void RestoreControlPointOfView(FalloutPlayerPointOfViewSnapshot saved)
    {
        _desiredCameraView = saved.DesiredView; _sharedCameraScalar = saved.SharedScalarBits;
        _desiredCameraViewStores = saved.DesiredViewStores; _sharedCameraScalarStores = saved.SharedScalarStores;
        _pointOfViewCalls.AddRange(saved.Calls); _pointOfViewIds.UnionWith(saved.Calls.Select(call => call.Observation));
        _pointOfViewLifetimes.AddRange(saved.Lifetimes);
    }

    internal static void ValidatePointOfViewCall(FalloutPlayerPointOfViewCall call)
    {
        if (call.Observation == Guid.Empty || call.Process == Guid.Empty || call.Constructor == Guid.Empty ||
            call.Entered < 1 || call.Changed < call.Entered || !Enum.IsDefined(call.Step) ||
            call.BeforeDesiredView != 0 || call.BeforeSharedScalarBits != 0 ||
            !call.GetterReturned && (call.FirstPerson is not null || call.DesiredViewStored is not null) ||
            call.FirstPerson is { } first && (first.Process != call.Process || first.Factory == Guid.Empty || first.NativeRoot == 0) ||
            call.DesiredViewStored is { } desired && (desired <= call.Entered || desired >= call.Changed) ||
            call.SharedScalarStored is { } scalar && (call.DesiredViewStored is not { } byteStore || scalar <= byteStore || scalar >= call.Changed || call.FirstPerson is not null) ||
            call.Returned != (call.Failure is null) || call.Failure is { Length: 0 } ||
            call.Returned && (!call.GetterReturned || call.FirstPerson is not null || call.DesiredViewStored is null ||
                call.SharedScalarStored is null || call.Step != FalloutPlayerPointOfViewStep.Returned) ||
            !call.Returned && call.Step == FalloutPlayerPointOfViewStep.Returned ||
            !call.GetterReturned && call.Step != FalloutPlayerPointOfViewStep.FirstPersonField ||
            call.DesiredViewStored is not null && !call.GetterReturned ||
            call.Step == FalloutPlayerPointOfViewStep.CameraChild && (!call.GetterReturned || call.FirstPerson is null ||
                call.DesiredViewStored is null || call.SharedScalarStored is not null || call.Failure != PointOfViewCameraBoundary))
            throw new InvalidDataException("Player POV lost its genuine current field, ordered stores or failed camera prefix.");
    }

    private static void ValidateControlPointOfView(FalloutStandalonePlayerSceneSnapshot saved)
    {
        var fields = saved.PointOfView ?? throw new InvalidDataException("Player scene omitted its independent POV constructor fields.");
        fields.Source.Validate();
        if (fields.Source.Controls.Main != saved.Source.Main || fields.Stack != saved.Stack || fields.Process != saved.CapturedProcess ||
            fields.Constructor != saved.Constructor || fields.DesiredView != 0 || fields.SharedScalarBits != 0 || fields.Calls is null ||
            fields.Lifetimes is not { Count: > 0 } ||
            fields.DesiredViewStores < 0 || fields.SharedScalarStores < 0 ||
            fields.Calls.Select(call => call.Observation).Distinct().Count() != fields.Calls.Count)
            throw new InvalidDataException("POV fields changed their actual constructor, process or command lifetime.");
        long last = 0, desiredStores = 0, scalarStores = 0;
        var lifetimes = new Dictionary<Guid, (FalloutPlayerPointOfViewLifetime Current, long? Next)>();
        var constructors = new HashSet<Guid>();
        for (var index = 0; index < fields.Lifetimes.Count; index++)
        {
            var lifetime = fields.Lifetimes[index];
            if (lifetime.Process == Guid.Empty || lifetime.Constructor == Guid.Empty ||
                lifetime.Published < 0 || lifetime.Published > saved.Sequence ||
                (index == 0 ? lifetime.Published != 0 : lifetime.Published <= fields.Lifetimes[index - 1].Published) ||
                !constructors.Add(lifetime.Constructor) ||
                !lifetimes.TryAdd(lifetime.Process, (lifetime, index + 1 < fields.Lifetimes.Count ? fields.Lifetimes[index + 1].Published : null)))
                throw new InvalidDataException("POV fields lost their original constructor or ordered cold process lifetimes.");
        }
        var currentLifetime = fields.Lifetimes[^1];
        if (currentLifetime.Process != saved.CapturedProcess || currentLifetime.Constructor != saved.Constructor ||
            fields.Lifetimes.Count > 1 && (saved.ColdHandoff is not { } transfer || transfer.Changed != currentLifetime.Published ||
                fields.Lifetimes[^2].Process != transfer.PreviousProcess || fields.Lifetimes[^2].Constructor != transfer.PreviousConstructor))
            throw new InvalidDataException("POV current fields omitted their exact fresh cold constructor publication.");
        var commands = saved.ControlObservations!.ToDictionary(receipt => receipt.Command.Observation);
        var firstPersonHistory = saved.FirstPersonHistory.OrderBy(owner => owner.Stored).ToArray();
        var firstIndex = 0; FalloutStandaloneFirstPersonOwner? currentFirst = null;
        foreach (var call in fields.Calls)
        {
            ValidatePointOfViewCall(call);
            if (call.Entered <= last || call.Changed > saved.Sequence ||
                !commands.TryGetValue(call.Observation, out var command) || !command.WriterEntered || command.Writer is not { } writer ||
                command.Process != call.Process || command.Constructor != call.Constructor || command.Retained >= call.Entered ||
                (writer.After & 0x10) == 0 || writer.Effects?.PointOfView != call)
                throw new InvalidDataException("POV prefix changed its actual entered source command or field publication order.");
            if (!lifetimes.TryGetValue(call.Process, out var lifetime) || lifetime.Current.Constructor != call.Constructor ||
                call.Entered <= lifetime.Current.Published || lifetime.Next is { } next && call.Changed >= next)
                throw new InvalidDataException("POV retained stores crossed an unowned actual process/constructor handoff.");
            while (firstIndex < firstPersonHistory.Length && firstPersonHistory[firstIndex].Stored < call.Entered)
                currentFirst = firstPersonHistory[firstIndex++];
            var first = currentFirst is { } owner && (owner.Released is null || owner.Released > call.Entered) ? owner : null;
            if (call.GetterReturned)
            {
                var expected = first is not null ? new FalloutSourceSceneNode(first.Publication.Process, first.Publication.Factory, first.Publication.NativeRoot) : null;
                if (call.FirstPerson != expected || expected is not null && expected.Process != call.Process)
                    throw new InvalidDataException("POV null/non-null branch changed the genuine retained current first-person field.");
            }
            if (call.DesiredViewStored is not null) desiredStores++;
            if (call.SharedScalarStored is not null) scalarStores++;
            last = call.Changed;
        }
        if (fields.DesiredViewStores != desiredStores || fields.SharedScalarStores != scalarStores)
            throw new InvalidDataException("POV constructor/store counts differ from actual source writer history.");
    }
}
