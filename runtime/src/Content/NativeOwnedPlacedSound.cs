using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// A real source REFR/SOUN emitter: no model or editor-marker geometry is needed.
internal sealed partial class NativeOwnedPlacedSound : Node3D, IGamebryoReferenceEnableLifecycle
{
    private readonly FalloutReferenceWorld _world;
    private readonly FalloutFormKey _reference;
    private readonly FalloutSoundRecord _source;
    private readonly FalloutAnimationSoundEvents _events;
    private readonly NativeOwnedAnimationSoundPlayer _sounds;
    private readonly Action<string> _failure;
    private bool _started;
    private bool _sourceRetired;
    private string? _retirementError;
    private long? _generation;
    private string? _error;
    internal NativeOwnedAnimationSoundPlayer PlaybackOwner => _sounds;
    internal object State => new
    {
        reference = _reference.ToString(),
        sound = _source.FormKey.ToString(),
        sourceLoop = _source.IsLooping,
        started = _started,
        generation = _generation,
        error = _error,
        retirementError = _retirementError
    };

    internal NativeOwnedPlacedSound(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutReferenceWorld world, FalloutPlacedReference reference, float unitsToMetres, Action<string> failure)
    {
        _world = world; _reference = reference.FormKey; _failure = failure;
        if (records.GetEffective(reference.Base).Signature != "SOUN")
            throw new InvalidDataException("Placed sound has no actual SOUN base.");
        if (records.GetEffective(reference.FormKey).ReadSubrecords().Any(field => field.Signature == "XPRM"))
            throw new NotSupportedException("Placed SOUN primitive-volume positioning needs its source volume owner.");
        _source = FalloutSoundRecordReader.Read(records, reference.Base);
        if (_source.RandomChancePercent != 0)
            throw new NotSupportedException("Placed SOUN autonomous chance scheduling needs its native producer.");
        var state = world.Get(_reference); _events = state.AnimationSoundEvents;
        Name = $"Reference_{_reference}";
        SetMeta("opennv_reference_form_key", _reference.ToString());
        SetMeta("opennv_source_form", reference.Base.ToString());
        _sounds = new(records, content, this, unitsToMetres, state.SoundRandom, _events, referenceLoopOwner: true)
        { Name = "OwnedPlacedSounds" };
        AddChild(_sounds);
    }

    public override void _Ready()
    {
        // Only this parent Ready boundary may add restored voice siblings.
        _sounds.RestoreReferencePcm();
    }

    public override void _Process(double delta)
    {
        if (_sourceRetired || _error is not null) return;
        try
        {
            _sounds.RequirePcmRestored();
            var eligible = _world.IsEnabled(_reference) && !_world.Get(_reference).Destroyed;
            if (!eligible) { StopSource(); return; }
            if (_started && _generation is { } generation)
            {
                var previous = _events.RequireEvent(generation);
                if (previous.End is FalloutAnimationSoundEnd.Faulted or FalloutAnimationSoundEnd.Cancelled)
                    throw new InvalidDataException("Placed SOUN retained a failed voice continuation: " + previous.Error);
                if (previous.End == FalloutAnimationSoundEnd.SourceUnloaded)
                {
                    // A staged replacement may have joined an old still-live
                    // owner. Its committed source unload begins a new epoch.
                    _started = false; _generation = null;
                }
            }
            if (_started) return;
            // Cold PCM voices restore in the shared child owner. A source loop
            // must join that actual generation, not dispatch a second startup.
            var active = _events.ActiveEvents.ToArray();
            if (active.Length != 0)
            {
                if (active.Length != 1 || active[0].Sound != _source.FormKey ||
                    active[0].TextKey != "SOUN:" + _source.FormKey || !_events.HasLivePlayback(active[0].Generation))
                    throw new InvalidDataException("Placed SOUN has an ambiguous or unavailable retained source voice.");
                _generation = active[0].Generation;
            }
            else if (_source.IsLooping)
            {
                // Native ordinary reference startup only creates a voice for
                // Loop; a finite SOUN declaration is not an ambient scheduler.
                if (_sounds.DispatchSound(_source.FormKey, this) == "unbound-source-sound")
                    throw new NotSupportedException("Placed SOUN playback retained an explicit source failure.");
                _generation = _events.Events.Last().Generation;
            }
            _started = true;
        }
        catch (Exception error)
        {
            _error = error.Message; _events.Fail(null, error.Message, "placed-SOUN:" + _source.FormKey);
            _failure(error.Message);
        }
    }

    public void BeforeReferenceDisabled()
    {
        // Presentation staging/culling does not change gameplay authority.
        if (!_world.IsEnabled(_reference) || _world.Get(_reference).Destroyed) StopSource();
    }
    private void StopSource()
    {
        _sounds.StopEmitterLoops(this); _started = false; _generation = null;
    }
    internal void RetireSourceBindings()
    {
        if (_sourceRetired)
        {
            if (_retirementError is not null) throw new InvalidOperationException(_retirementError);
            return;
        }
        _sourceRetired = true;
        // The caller owns actual source unload/replacement, before any native
        // descendant exits. Raw unexpected tree loss must still cancel.
        try { NativeOwnedAnimationSoundPlayer.UnloadSourceLoops(this); }
        catch (Exception error)
        {
            _retirementError = error.Message;
            _events.Fail(null, error.Message, "placed-SOUN-retirement:" + _source.FormKey);
            throw;
        }
    }

    internal static void RetireSubtree(Node root)
    {
        Exception? failure = null;
        foreach (var sound in OpenNV.Runtime.SceneGraph.NodeTraversal.SelfAndDescendants<NativeOwnedPlacedSound>(root))
        {
            try { sound.RetireSourceBindings(); }
            catch (Exception error) { failure ??= error; }
        }
        if (failure is not null) throw new InvalidOperationException("Placed SOUN retirement lost a source continuation.", failure);
    }
}
