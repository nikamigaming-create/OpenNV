using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal FalloutStandalonePlayerSceneSnapshot Capture()
    {
        RequireIdle(allowFailure: true);
        if (_first is { Released: null, Publication: { } publication } && publication.Process == _process &&
            _livingFirst is not null && _failure is null && _boundary is null && !ReadLiving(_livingFirst))
            throw new InvalidOperationException("Captured current first-person source ownership lost its native body.");
        var saved = new FalloutStandalonePlayerSceneSnapshot(Schema, Source, _stack, _process, _constructor, _sequence,
            _scene, _hold, _view, _clockKind, _numerator, _denominator, _bracket, _cameraPosition, _cameraAngles,
            _first, _history.ToArray(), _clock, _camera, _cold, _boundary, _failure, _retired,
            _viewRequest, _viewRequestArgument, _countdownEnabled, _countdownBits, _referenceScratch, _prelude);
        Validate(saved); return saved;
    }
    private void Restore(FalloutStandalonePlayerSceneSnapshot saved)
    {
        Validate(saved);
        if (saved.Source != Source || saved.Stack != _stack || saved.CapturedProcess == _process || saved.Retired)
            throw new InvalidDataException("Cold Player scene changed exact source/stack/process or resurrected a retired owner.");
        _sequence = saved.Sequence; _scene = saved.SceneMode; _hold = saved.MainHold; _view = saved.PlayerView;
        _clockKind = saved.ClockKind; _numerator = saved.Numerator; _denominator = saved.Denominator; _bracket = saved.SceneBracket;
        _cameraPosition = saved.CameraPosition; _cameraAngles = saved.CameraAngles; _first = saved.FirstPerson;
        _history.AddRange(saved.FirstPersonHistory); _clock = saved.Clock; _camera = saved.Camera;
        _boundary = saved.Boundary; _failure = saved.Failure;
        _viewRequest = saved.ViewRequest; _viewRequestArgument = saved.ViewRequestArgument;
        _countdownEnabled = saved.CountdownEnabled; _countdownBits = saved.CountdownBits;
        _referenceScratch = saved.ReferenceScratch; _prelude = saved.Prelude;
        _cold = new(saved.CapturedProcess, _process, saved.Constructor, _constructor, Next());
        // The old native roots are historical declarations only. A fresh
        // source-role factory must publish on this actual process/thread.
    }
    internal static void Validate(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Source is null || string.IsNullOrWhiteSpace(saved.Stack) ||
            saved.CapturedProcess == Guid.Empty || saved.Constructor == Guid.Empty || saved.Sequence < 0 ||
            saved.CameraPosition is null || saved.CameraAngles is null || saved.FirstPersonHistory is null ||
            saved.FirstPersonHistory.Select(owner => owner.Identity).Distinct().Count() != saved.FirstPersonHistory.Count ||
            saved.FirstPersonHistory.Select(owner => owner.Publication.Factory).Distinct().Count() != saved.FirstPersonHistory.Count ||
            saved.SceneMode > 1 || saved.MainHold > 1 || saved.PlayerView != 0 || saved.MainHold != 0 && saved.SceneMode == 0 ||
            saved.ViewRequest != 0 || saved.ViewRequestArgument != 0 || saved.CountdownEnabled != 0 ||
            saved.CountdownBits != 0x40a00000 || saved.ReferenceScratch != 0 ||
            saved.ClockKind != 0 || saved.Numerator != FalloutStandalonePlayerSceneSource.One ||
            saved.Denominator != FalloutStandalonePlayerSceneSource.One ||
            saved.Boundary is not null && string.IsNullOrWhiteSpace(saved.Boundary) ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure))
            throw new InvalidDataException("Standalone scene omitted or invented its current source constructor/writer/ownership fields.");
        saved.Source.Validate();
        foreach (var owner in saved.FirstPersonHistory)
        {
            var native = owner.Publication;
            if (owner.Identity == Guid.Empty || owner.Stored < 1 || owner.Stored > saved.Sequence ||
                owner.Released is { } end && (end <= owner.Stored || end > saved.Sequence) ||
                native is null || native.Factory == Guid.Empty || native.Process == Guid.Empty || native.Source != saved.Source.Identity ||
                native.Stack != saved.Stack || native.NativeRoot == 0 || string.IsNullOrWhiteSpace(native.Owner) ||
                string.IsNullOrWhiteSpace(native.Resource) || !native.Resource.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) ||
                !FalloutAdvancementRuntimeReceipt.Digest(native.ResourceSha256))
                throw new InvalidDataException("Player scene first-person field lost a source/native factory or ordered release.");
        }
        if (saved.FirstPerson is { } current && !saved.FirstPersonHistory.Any(owner => owner == current))
            throw new InvalidDataException("Player scene current field is not its exact retained factory history.");
        if (saved.FirstPerson is { Released: null } first && first.Publication.Process != saved.CapturedProcess &&
            (saved.ColdHandoff is not { } cold || cold.PreviousProcess != first.Publication.Process))
            throw new InvalidDataException("A foreign native root was promoted without its explicit historical cold handoff.");
        if (saved.ColdHandoff is { } handoff && (handoff.PreviousProcess == Guid.Empty || handoff.CurrentProcess != saved.CapturedProcess ||
            handoff.PreviousProcess == handoff.CurrentProcess || handoff.PreviousConstructor == Guid.Empty ||
            handoff.CurrentConstructor != saved.Constructor || handoff.PreviousConstructor == handoff.CurrentConstructor ||
            handoff.Changed < 1 || handoff.Changed > saved.Sequence))
            throw new InvalidDataException("Cold scene reconstruction omitted its genuine new process/factory epoch.");
        if (saved.Clock is { } clock)
        {
            if (clock.Main == Guid.Empty || clock.MainOrdinal < 1 || clock.Entered < 1 || clock.Changed < clock.Entered ||
                clock.Changed > saved.Sequence || !Enum.IsDefined(clock.Step) || clock.BeforeBracket != 0 ||
                clock.Bracket != unchecked((byte)(clock.BeforeBracket + 1)) || saved.SceneBracket != clock.Bracket ||
                clock.ArgumentBits is { } argument && argument != clock.DeliveredBits ||
                (clock.Step is FalloutStandaloneSceneClockStep.ArgumentStored or FalloutStandaloneSceneClockStep.VirtualEntered) && clock.ArgumentBits is null ||
                clock.Step == FalloutStandaloneSceneClockStep.Returned ||
                (clock.Step == FalloutStandaloneSceneClockStep.Failed) != (clock.Failure is not null) ||
                clock.Failure is not null && (string.IsNullOrWhiteSpace(clock.Failure) || saved.Failure != clock.Failure))
                throw new InvalidDataException("Scene clock invented a returned original virtual or lost its incremented failed prefix.");
        }
        else if (saved.SceneBracket != 0) throw new InvalidDataException("Scene bracket lost its original loader or actual increment writer.");
        if (saved.Camera is { } camera)
        {
            var (scene, hold) = FalloutStandalonePlayerSceneSource.Toggle(camera.BeforeScene, camera.Argument);
            if (camera.Invocation == Guid.Empty || camera.BeforeScene > 1 || camera.BeforeHold > 1 || camera.Entered < 1 ||
                camera.Changed < camera.Entered || camera.Changed > saved.Sequence || string.IsNullOrWhiteSpace(camera.Owner) ||
                !Enum.IsDefined(camera.Step) || saved.SceneMode != scene || saved.MainHold != hold ||
                (camera.Step == FalloutStandaloneCameraStep.Failed) != (camera.Failure is not null) ||
                (camera.Step == FalloutStandaloneCameraStep.Failed) != (camera.FailedAt is not null) ||
                camera.FailedAt is { } at && (!Enum.IsDefined(at) || at is FalloutStandaloneCameraStep.Failed or FalloutStandaloneCameraStep.Returned) ||
                camera.Failure is not null && (string.IsNullOrWhiteSpace(camera.Failure) || saved.Failure != camera.Failure))
                throw new InvalidDataException("Free-camera transition changed its original byte stores, child order or retained failure.");
        }
        else if (saved.SceneMode != 0 || saved.MainHold != 0 || saved.CameraPosition != new FalloutStandaloneCameraPosition(0, 0, 0) ||
            saved.CameraAngles != new FalloutStandaloneCameraAngles(0, 0))
            throw new InvalidDataException("Scene/camera fields lack their actual constructor or admitted source writer.");
        if (saved.Prelude is { } prelude && (saved.Clock is not { Step: FalloutStandaloneSceneClockStep.Failed } prefixClock ||
            prelude.Main != prefixClock.Main || prelude.MainOrdinal != prefixClock.MainOrdinal || prelude.Entered < prefixClock.Entered ||
            prelude.Changed < prelude.Entered || prelude.Changed > saved.Sequence || prelude.Step != FalloutStandaloneScenePreludeStep.Failed ||
            prelude.FailedAt is null || !Enum.IsDefined(prelude.FailedAt.Value) || prelude.FailedAt == FalloutStandaloneScenePreludeStep.Failed ||
            string.IsNullOrWhiteSpace(prelude.Failure) || prelude.Failure != saved.Failure))
            throw new InvalidDataException("Original Player virtual prefix lost its exact entered source child/failure.");
    }
    internal static void RequirePlayable(FalloutStandalonePlayerSceneSnapshot saved)
    {
        Validate(saved);
        if (saved.Retired || saved.Failure is not null || saved.Boundary is not null || saved.Clock is not null ||
            saved.Camera is { Step: not FalloutStandaloneCameraStep.Returned } ||
            saved.FirstPerson is { Released: null, Publication: { } native } && native.Process != saved.CapturedProcess)
            throw new NotSupportedException("Current standalone Player scene retains a real failed/unowned child or unbound cold native field.");
    }
}
