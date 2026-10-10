using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World;

// One campaign lease of the actual engine simulation/audio properties. Audio
// playback speed is global: existing and subsequently constructed native
// voices retain their independent SOUN pitch and actual playback lifetime.
internal sealed partial class RuntimeNativeGlobalTime : Node
{
    private static RuntimeNativeGlobalTime? _active;
    private FalloutGlobalTimeState? _owner;
    private IDisposable? _binding;
    private int _thread;
    private bool _publicationEntered, _simulationEntered, _audioEntered;
    private double _priorSimulation, _expectedSimulation;
    private float _priorAudio, _expectedAudio;
    private string? _frameFailure;
    private long _simulationWrites, _audioWrites;

    internal static RuntimeNativeGlobalTime Create(FalloutGlobalTimeState owner)
    {
        ArgumentNullException.ThrowIfNull(owner); owner.Source.Validate();
        RuntimeNativeGlobalTime? node = null;
        try
        {
            node = new RuntimeNativeGlobalTime();
            node._owner = owner; node._thread = System.Environment.CurrentManagedThreadId;
            node.Name = "SourceGlobalTime";
            node.ProcessMode = ProcessModeEnum.Always;
            node.ProcessPriority = int.MaxValue;
            node.SetProcess(false);
            return node;
        }
        catch (Exception original)
        {
            if (node is null) throw;
            try { node.Free(); }
            catch (Exception cleanup) { throw new AggregateException("Global time node factory retains its original and native cleanup failures.", original, cleanup); }
            throw;
        }
    }

    internal void PublishNative()
    {
        RequireThread();
        if (!IsInsideTree()) throw new InvalidOperationException("Global time has no actual attached native campaign owner.");
        if (_publicationEntered || _binding is not null)
            throw new InvalidOperationException("Global time native publication cannot replay its entered factory.");
        if (_active is not null)
            throw new InvalidOperationException("The engine simulation/audio properties already have an actual campaign owner.");
        var owner = _owner ?? throw new InvalidOperationException("Global time native factory omitted its source owner.");
        _publicationEntered = true; _active = this;
        _priorSimulation = _expectedSimulation = Engine.TimeScale;
        _priorAudio = _expectedAudio = AudioServer.PlaybackSpeedScale;
        if (!double.IsFinite(_priorSimulation) || !float.IsFinite(_priorAudio) || _priorAudio <= 0)
        {
            _active = null;
            throw new NotSupportedException("Native global rate has an unsupported pre-existing simulation/audio property.");
        }
        try
        {
            _binding = owner.BindPresentation(new(this, "godot-global-rate:" + GetInstanceId(), ReadNative,
                PublishRates, RetireNativeRates));
            SetProcess(true);
        }
        catch (Exception original)
        {
            // The C# source owner tries its genuine independent retirement
            // after a failed publish. A still-active lease remains reachable.
            if (ReferenceEquals(_active, this))
                try { RetireNativeRates(); }
                catch (Exception cleanup) { throw new AggregateException("Native global rate retained failed publication and independent restoration.", original, cleanup); }
            throw;
        }
    }

    internal FalloutGlobalTimeState SourceOwner => _owner ?? throw new InvalidOperationException("Native global time lost its source owner.");
    internal bool NativeRetirementComplete => _binding is null && !ReferenceEquals(_active, this) &&
        !_simulationEntered && !_audioEntered;

    internal object State => new
    {
        source = _owner?.State,
        publicationEntered = _publicationEntered,
        ownsEngineProperties = ReferenceEquals(_active, this),
        simulationWrites = _simulationWrites,
        audioWrites = _audioWrites,
        simulationEntered = _simulationEntered,
        audioEntered = _audioEntered,
        simulationRate = Engine.TimeScale,
        audioRate = AudioServer.PlaybackSpeedScale,
        frameFailure = _frameFailure,
        perVoicePitch = "unchanged-source-base-pitch;native-global-audio-rate-consumer",
        finalAudioParity = "unmatched-source-per-voice-eligibility-and-audio-worker-phase",
        reverseGameplayClock = "separate-consumer-owner;SDK-rate-storage-is-not-reverse-gameplay-acceptance",
    };

    private FalloutGlobalTimeNativeValues ReadNative()
    {
        RequireThread();
        if (!ReferenceEquals(_active, this)) throw new InvalidOperationException("Native rate read changed its actual campaign lease.");
        return new(Engine.TimeScale, AudioServer.PlaybackSpeedScale);
    }

    private void PublishRates(FalloutGlobalTimePublication publication)
    {
        RequireThread();
        if (!ReferenceEquals(_active, this) || !IsInsideTree())
            throw new InvalidOperationException("Source rate publication lost its living native campaign owner.");
        if (!float.IsFinite(publication.CurrentRate) ||
            !float.IsFinite(publication.AudioRate) || publication.AudioRate <= 0)
            throw new InvalidDataException("Native global rate received invalid source values.");
        if (Engine.TimeScale != _expectedSimulation || AudioServer.PlaybackSpeedScale != _expectedAudio)
            throw new InvalidOperationException("Another owner changed leased simulation/audio properties.");
        // Publish the attempted target before each actual SDK call so an
        // exception cannot lose the identity of a partly entered write.
        _expectedSimulation = publication.CurrentRate; _simulationEntered = true;
        Engine.TimeScale = _expectedSimulation;
        if (Engine.TimeScale != _expectedSimulation)
            throw new InvalidDataException("Engine simulation rate did not return the source property write.");
        _simulationWrites = checked(_simulationWrites + 1);
        _expectedAudio = publication.AudioRate; _audioEntered = true;
        AudioServer.PlaybackSpeedScale = _expectedAudio;
        if (AudioServer.PlaybackSpeedScale != _expectedAudio)
            throw new InvalidDataException("Native audio rate did not return the source property write.");
        _audioWrites = checked(_audioWrites + 1);
    }

    public override void _Process(double delta)
    {
        if (_binding is null || _frameFailure is not null) return;
        try { (_owner ?? throw new InvalidOperationException("Native global time lost its source owner.")).RefreshPresentation(); }
        catch (Exception error)
        {
            _frameFailure = error.ToString(); SetProcess(false);
            GD.PushError("OPENNV_SOURCE_GLOBAL_TIME_FAILURE " + error);
        }
    }

    internal void RetirePresentation()
    {
        RequireThread(); SetProcess(false);
        if (_binding is { } lease) { lease.Dispose(); _binding = null; }
        else
        {
            _owner?.RetirePresentation(this);
            if (ReferenceEquals(_active, this)) RetireNativeRates();
        }
    }

    private void RetireNativeRates()
    {
        RequireThread();
        if (!ReferenceEquals(_active, this))
        {
            if (_active is null && !_simulationEntered && !_audioEntered) return;
            throw new InvalidOperationException("Global rate retirement changed the actual native lease.");
        }
        var failures = new List<Exception>();
        if (_simulationEntered)
        {
            try
            {
                var actual = Engine.TimeScale;
                if (actual != _expectedSimulation && actual != _priorSimulation)
                    throw new InvalidOperationException("Global rate cannot restore a simulation property written by another owner.");
                if (actual != _priorSimulation) Engine.TimeScale = _priorSimulation;
                if (Engine.TimeScale != _priorSimulation)
                    throw new InvalidDataException("Native simulation-rate restoration did not return.");
                _simulationEntered = false;
            }
            catch (Exception error) { failures.Add(error); }
        }
        if (_audioEntered)
        {
            try
            {
                var actual = AudioServer.PlaybackSpeedScale;
                if (actual != _expectedAudio && actual != _priorAudio)
                    throw new InvalidOperationException("Global rate cannot restore an audio property written by another owner.");
                if (actual != _priorAudio) AudioServer.PlaybackSpeedScale = _priorAudio;
                if (AudioServer.PlaybackSpeedScale != _priorAudio)
                    throw new InvalidDataException("Native audio-rate restoration did not return.");
                _audioEntered = false;
            }
            catch (Exception error) { failures.Add(error); }
        }
        if (!_simulationEntered && !_audioEntered) _active = null;
        if (failures.Count != 0)
            throw new AggregateException("Native global rate retains independent simulation/audio retirement failures.", failures);
    }

    private void RequireThread()
    {
        if (System.Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Native global time changed its actual constructed thread.");
    }
    public override void _ExitTree() => RetirePresentation();
}
