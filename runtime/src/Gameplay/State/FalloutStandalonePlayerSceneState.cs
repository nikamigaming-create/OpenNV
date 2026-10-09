using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal const string Schema = "opennv-standalone-Player-scene/v1";
    internal FalloutStandalonePlayerSceneSource Source { get; }
    internal Guid Process => _process;
    internal string Stack => _stack;
    private readonly string _stack;
    private readonly Guid _process, _constructor = Guid.NewGuid();
    private long _sequence, _reentry;
    private int? _thread;
    private bool _retired, _reading, _writing;
    private byte _scene, _hold, _view, _bracket;
    private uint _clockKind, _numerator = FalloutStandalonePlayerSceneSource.One, _denominator = FalloutStandalonePlayerSceneSource.One;
    private FalloutStandaloneCameraPosition _cameraPosition = new(0, 0, 0);
    private FalloutStandaloneCameraAngles _cameraAngles = new(0, 0);
    private FalloutStandaloneFirstPersonOwner? _first;
    private readonly List<FalloutStandaloneFirstPersonOwner> _history = [];
    private Func<bool>? _livingFirst;
    private FalloutStandaloneSceneClockCall? _clock;
    private FalloutStandaloneCameraCall? _camera;
    private FalloutStandaloneSceneColdHandoff? _cold;
    private string? _boundary, _failure;
    internal string? SaveBlocker => _retired ? "source-Player-scene-retired" : _failure ?? _boundary ??
        (_clock is { Step: not FalloutStandaloneSceneClockStep.Returned } ? "source-Player-scene-clock-entered" :
        _camera is { Step: not FalloutStandaloneCameraStep.Returned } ? "source-Player-free-camera-entered" :
        _first is not null && _livingFirst is null ? "source-Player-first-person-cold-publication-unbound" : null);
    internal object State => new
    {
        source = Source.Identity,
        _process,
        _constructor,
        _sequence,
        scene = _scene,
        mainHold = _hold,
        view = _view,
        clockKind = _clockKind,
        numerator = _numerator,
        denominator = _denominator,
        bracket = _bracket,
        cameraPosition = _cameraPosition,
        cameraAngles = _cameraAngles,
        firstPerson = _first,
        history = _history.ToArray(),
        clock = _clock,
        camera = _camera,
        cold = _cold,
        boundary = _boundary,
        failure = _failure,
        retired = _retired,
        saveBlocker = SaveBlocker
    };
    internal FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource source, string stack, Guid process,
        FalloutStandalonePlayerSceneSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new ArgumentException("Player scene requires its real current Main process.", nameof(process));
        Source = source; _stack = stack; _process = process;
        if (saved is not null) Restore(saved);
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireThread()
    {
        if (_thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player scene changed its actual native presentation/Main thread.");
    }
    private void RequireIdle(bool allowFailure = false)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        if (_reading || _writing)
        {
            _reentry = checked(_reentry + 1);
            throw new InvalidOperationException("Player scene native child cannot reenter capture, publication or source mutation.");
        }
        if (!allowFailure && (_failure ?? _boundary) is { } failure) throw new NotSupportedException(failure);
    }
    private bool ReadLiving(Func<bool> read)
    {
        if (_reading) throw new InvalidOperationException("Player first-person lifetime observation cannot recurse.");
        _reading = true; var before = _reentry;
        try
        {
            var result = read();
            if (_reentry != before) throw new InvalidOperationException("Player lifetime observation swallowed forbidden source reentry.");
            return result;
        }
        catch (Exception error) { RetainFailure(error); throw; }
        finally { _reading = false; }
    }
    internal FalloutStandaloneFirstPersonOwner PublishFirstPerson(FalloutStandaloneFirstPersonPublication publication, Func<bool> living)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(living); ValidatePublication(publication);
        if (_first is { Released: null } && _livingFirst is not null)
            throw new InvalidOperationException("The old Player first-person owner must release before the returned child is stored.");
        if (_history.Any(owner => owner.Publication.Factory == publication.Factory) || !ReadLiving(living))
            throw new InvalidOperationException("Player first-person publication is not a fresh living returned source factory.");
        if (_first is { Released: null } previous && _livingFirst is null &&
            (previous.Publication.Process == _process || previous.Publication.Resource != publication.Resource ||
            previous.Publication.ResourceSha256 != publication.ResourceSha256))
            throw new InvalidDataException("Cold Player first-person factory changed its captured source resource or process epoch.");
        _thread ??= Environment.CurrentManagedThreadId;
        _first = new(Guid.NewGuid(), Next(), publication, null); _history.Add(_first); _livingFirst = living;
        return _first;
    }
    internal void ReleaseFirstPerson(FalloutStandaloneFirstPersonOwner owner)
    {
        RequireIdle(allowFailure: true);
        if (_first is null || owner.Identity != _first.Identity || owner.Publication != _first.Publication ||
            owner.Publication.Process != _process)
            throw new InvalidOperationException("Player first-person release does not own the actual current source child.");
        if (_first.Released is not null) return;
        // This releases this field's ownership. Other native/viewport references
        // can still retain the object; it is not a claim of global destruction.
        _first = _first with { Released = Next() };
        var at = _history.FindIndex(value => value.Identity == _first.Identity);
        if (at < 0) throw new InvalidDataException("Player first-person owner lost its current factory history.");
        _history[at] = _first; _livingFirst = null;
    }
    internal bool ReadSceneMode() { RequireIdle(); return _scene != 0; }
    internal bool ReadMainHold() { RequireIdle(); return _hold != 0; }
    internal bool ReadFirstPersonSelected()
    {
        RequireIdle();
        if (_view == 0 && _first is { Released: null } first)
        {
            if (first.Publication.Process != _process || _livingFirst is null || !ReadLiving(_livingFirst))
                throw new NotSupportedException("Actual current Player first-person pointer has no living native factory rebind.");
            return true;
        }
        EnterUnowned("source-Player-scene-original-TLS-initializer-and-actor-match-or-loaded-data-arm-unowned");
        return false; // EnterUnowned always throws. No absence is inferred here.
    }
    internal void RetainUnownedViewTransition(string owner) => EnterUnowned(owner);
    internal void EnterUnowned(string owner)
    {
        RequireIdle(allowFailure: true); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        _boundary ??= owner; Next(); throw new NotSupportedException(_boundary);
    }
    private void RetainFailure(Exception error)
    {
        _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().FullName ?? error.GetType().Name : error.Message;
        Next();
    }
    private void ValidatePublication(FalloutStandaloneFirstPersonPublication publication)
    {
        if (publication is null || publication.Factory == Guid.Empty || publication.Process != _process ||
            publication.Source != Source.Identity || publication.Stack != _stack || publication.NativeRoot == 0 ||
            string.IsNullOrWhiteSpace(publication.Owner) || !FalloutAdvancementRuntimeReceipt.Digest(publication.ResourceSha256) ||
            string.IsNullOrWhiteSpace(publication.Resource) || !publication.Resource.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Player scene omitted its exact source-role/native factory/resource publication.");
    }
    internal bool ToggleFreeCamera(byte argument, IFalloutStandaloneFreeCameraConsumers consumers)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(consumers); ArgumentException.ThrowIfNullOrWhiteSpace(consumers.Owner);
        if (_clock is { Step: not FalloutStandaloneSceneClockStep.Returned })
            throw new InvalidOperationException("A free-camera request cannot overtake an entered original Player scene update.");
        var before = _scene; var holdBefore = _hold; var fault = _reentry;
        _writing = true;
        _camera = new(Guid.NewGuid(), argument, before, holdBefore, Next(), _sequence,
            FalloutStandaloneCameraStep.SceneStore, consumers.Owner, null, null);
        try
        {
            (_scene, _hold) = FalloutStandalonePlayerSceneSource.Toggle(before, argument);
            Changed(FalloutStandaloneCameraStep.HoldStore);
            Changed(FalloutStandaloneCameraStep.InputManagerChild); consumers.StoreInputManagerCameraByte(_scene); Check();
            if (before == 0)
            {
                Changed(FalloutStandaloneCameraStep.Position); _cameraPosition = consumers.ReadPosition(); Check(); Next();
                Changed(FalloutStandaloneCameraStep.Height); var height = consumers.ReadScaledCameraHeightBits(); Check();
                Changed(FalloutStandaloneCameraStep.Angles); _cameraAngles = consumers.ReadAngles(); Check(); Next();
                // The original height child has already returned its Float32
                // store. This source add is an SSE Float32 add/store, not a
                // scaled Godot camera distance or a widened vector operation.
                _cameraPosition = _cameraPosition with
                {
                    Z = BitConverter.SingleToUInt32Bits(
                    BitConverter.UInt32BitsToSingle(_cameraPosition.Z) + BitConverter.UInt32BitsToSingle(height))
                };
                Next();
            }
            Changed(FalloutStandaloneCameraStep.Returned); return _scene != 0;
        }
        catch (Exception error)
        {
            RetainFailure(error); _camera = _camera! with
            {
                Step = FalloutStandaloneCameraStep.Failed,
                FailedAt = _camera!.Step,
                Changed = _sequence,
                Failure = _failure
            }; throw;
        }
        finally { _writing = false; }
        void Changed(FalloutStandaloneCameraStep step) => _camera = _camera! with { Step = step, Changed = Next() };
        void Check()
        {
            RequireThread();
            if (fault != _reentry) throw new InvalidOperationException("Free-camera child swallowed forbidden source ownership reentry.");
        }
    }
    internal void Retire()
    {
        RequireIdle(allowFailure: true);
        if (_first is { Released: null } && _livingFirst is not null)
            throw new InvalidOperationException("Actual first-person source field must release before Main scene retirement.");
        _retired = true; Next();
        // Entered clock/free-camera prefixes and failures remain captured. A
        // session retirement never emits a fabricated virtual-child return.
    }
}
