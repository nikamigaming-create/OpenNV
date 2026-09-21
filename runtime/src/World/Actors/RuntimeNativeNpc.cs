using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

/// <summary>One source NPC reference and its directly decoded, shared skeleton.</summary>
internal partial class RuntimeNativeNpc : CharacterBody3D
{
    internal FalloutNpcAppearance Appearance { get; private set; } = null!;
    internal RuntimeNativeNifSkeleton Skeleton { get; private set; } = null!;
    internal IReadOnlyList<RuntimeNativeNifScene> Parts { get; private set; } = [];
    internal RuntimeNativeActorCombat? Combat { get; set; }
    private RuntimeNativeNifAnimation? _animation;
    private RuntimeNativeNifAnimation? _baseAnimation;
    private float _baseAnimationSeconds;
    private float _animationSeconds;
    private FalloutFormKey? _idleForm;
    private string? _idleOwner;
    private bool _responseIdleActive;
    private long _idleRevision;
    private FalloutIdleAnimationData? _idleData;
    private FalloutIdleAnimationPlayback? _idlePlayback;
    private readonly FalloutIdleReplayState _idleReplays = new();
    private readonly List<object> _idleTextKeyEvents = [];
    private readonly List<Node3D> _animationObjects = [];
    private NativeOwnedAnimationSoundPlayer? _animationSounds;
    internal event Action? PosePublished;
    internal float BaseSourceSeconds => _baseAnimationSeconds;
    internal string? AnimationError { get; private set; }
    internal FalloutFormKey? ActiveIdle => _idleForm;
    internal string? ActiveIdleOwner => _idleOwner;
    internal object AnimationState => new
    {
        sequence = _animation?.Sequence.Name,
        idle = _idleForm?.ToString(),
        idleOwner = _idleOwner,
        idleRevision = _idleRevision,
        responseIdleActive = _responseIdleActive,
        baseSequence = _baseAnimation?.Sequence.Name,
        baseSourceSeconds = _baseAnimationSeconds,
        ai = AiState,
        face = FaceState,
        sourceSeconds = _animationSeconds,
        idleTiming = _idleData is null ? null : new
        {
            source = _idleData,
            additionalLoops = _idlePlayback?.AdditionalLoops,
            completedRepeats = _idlePlayback?.CompletedRepeats,
            loopStart = _idlePlayback?.LoopStart,
            loopEnd = _idlePlayback?.LoopEnd,
        },
        replayCooldowns = _idleReplays.Remaining.Select(value => new { idle = value.Key.ToString(), seconds = value.Value }).ToArray(),
        textKeyCrossings = _idleTextKeyEvents.ToArray(),
        animationSounds = _animationSounds?.State,
        sourceFloatProperties = Skeleton.FloatExtraData.Values.Select(value => new
        {
            node = value.Node,
            name = value.Name,
            value = value.Value,
        }).ToArray(),
        headTracking = HeadTrackingState,
        visualChannels = Skeleton.VisualChannelState,
        absentBaseTargets = _baseAnimation?.AbsentSourceTargets.Select(link => new { node = link.NodeName, controller = link.ControllerType }).ToArray(),
        absentIdleTargets = _animation?.AbsentSourceTargets.Select(link => new { node = link.NodeName, controller = link.ControllerType }).ToArray(),
        error = AnimationError,
        combat = Combat?.Observation,
    };

    internal void PlayBaseSequence(FalloutNifFile source, FalloutNifControllerSequence sequence, string owner)
    {
        RestoreAuthoredHeadPose();
        var animation = new RuntimeNativeNifAnimation(source, sequence, Skeleton);
        if (sequence.ControlledBlocks.Any(link => link.NodeName == sequence.TargetName && link.ControllerType == "NiTransformController"))
            throw new NotSupportedException("Base animation accumulation requires a root-motion owner.");
        Skeleton.Node.SetBonePose(Skeleton.BoneIndex(sequence.TargetName), Transform3D.Identity);
        animation.ApplySourceTime(sequence.StartTime);
        _baseAnimation = animation; _baseAnimationSeconds = sequence.StartTime;
        _baseElapsedSeconds = 0;
        SetMeta("opennv_base_animation_source", owner);
    }

    internal void PlayIdle(FalloutPluginStack stack, string editorId)
        => PlayIdle(stack, FalloutDialogueTopic.Find(stack, "IDLE", editorId).FormKey, "script");

    internal void BeginResponseAnimation(FalloutPluginStack stack, FalloutFormKey? animation)
    {
        _responseIdleActive = animation is not null;
        if (animation is not { } idle) return;
        // A result script can start the same IDLE before its response begins.
        // Keep that live instance and its authored phase instead of replaying
        // its first frame at the voice boundary.
        if (_idleForm != idle || _animation is null) PlayIdle(stack, idle, "dialogue-response");
        else _idleOwner = "dialogue-response";
    }

    internal void EndResponseAnimation() => _responseIdleActive = false;

    private void PlayIdle(FalloutPluginStack stack, FalloutFormKey form, string owner)
    {
        RestoreAuthoredHeadPose();
        var record = stack.GetEffective(form);
        var idle = FalloutActorIdleSource.Resolve(stack, record);
        var timing = FalloutIdleAnimationData.Read(record);
        var path = idle.AnimationPath;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is absent.");
        if (!content.TryRead(path, null, out var bytes, out var identity))
            throw new FileNotFoundException($"Source IDLE animation is absent: {path}");
        var source = FalloutNifFile.Read(bytes);
        var sequences = source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        if (sequences.Length != 1)
            throw new NotSupportedException($"IDLE {idle.Form} requires one source KF sequence, found {sequences.Length}.");
        if (!float.IsFinite(sequences[0].Frequency) || sequences[0].Frequency <= 0 ||
            !float.IsFinite(sequences[0].StartTime) || !float.IsFinite(sequences[0].StopTime) ||
            sequences[0].StopTime <= sequences[0].StartTime)
            throw new InvalidDataException($"IDLE {idle.Form} has an invalid source clock.");
        var created = new List<Node3D>();
        var controlledNodes = sequences[0].ControlledBlocks
            .Where(link => link.ControllerType is "NiTransformController" or "NiVisController" && link.PropertyType.Length == 0 &&
                link.Variable1.Length == 0 && link.Variable2.Length == 0)
            .Select(link => link.NodeName).ToHashSet(StringComparer.Ordinal);
        try
        {
            foreach (var item in idle.Objects)
            {
                if (!content.TryRead(item.ModelPath, null, out var model, out _))
                    throw new FileNotFoundException($"Source ANIO model is absent: {item.ModelPath}");
                var part = NativeNifMeshBuilder.AddActorPart(model, Skeleton, externalTransformTargets: controlledNodes);
                part.Root.Visible = false;
                part.Root.SetMeta("opennv_source_model", item.ModelPath);
                part.Root.SetMeta("opennv_source_form", item.Form.ToString());
                part.Root.SetMeta("opennv_animation_object_form", item.Form.ToString());
                created.Add(part.Root);
            }
            var weapon = Combat?.AnimationWeapon;
            var objectNodes = created.SelectMany(root => root.FindChildren("*", "", true, false).OfType<Node3D>().Prepend(root)).ToArray();
            var externalTargets = weapon?.Targets.ToHashSet(StringComparer.Ordinal);
            if (externalTargets is not null)
                externalTargets.UnionWith(objectNodes.Select(node => node.Name.ToString()));
            Action<float>? BindObject(FalloutNifControllerLink link)
            {
                if (link.ControllerType is not ("NiTransformController" or "NiVisController") || link.PropertyType.Length != 0 ||
                    link.Variable1.Length != 0 || link.Variable2.Length != 0) return null;
                var matches = objectNodes
                    .Where(node => node.Name.ToString().Equals(link.NodeName, StringComparison.Ordinal)).ToArray();
                if (matches.Length == 0) return weapon?.Bind(source, link);
                if (matches.Length != 1) return null;
                if (link.ControllerType == "NiVisController")
                {
                    var visibility = new FalloutNifBoolAnimation(source, link.Interpolator);
                    return time => matches[0].Visible = visibility.Sample(time);
                }
                var sampler = new FalloutNifAnimationSampler(source, link.Interpolator);
                return time =>
                {
                    var sample = sampler.Sample(time);
                    if (sample.Translation is { } translation)
                        matches[0].Position = GamebryoCoordinate.ConvertVector(new(translation.X, translation.Y, translation.Z)) * Skeleton.UnitsToMetres;
                    if (sample.Rotation is { } rotation)
                        matches[0].Quaternion = new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized();
                    if (sample.Scale is { } scale) matches[0].Scale = Vector3.One * scale;
                };
            }
            var selected = new RuntimeNativeNifAnimation(source, sequences[0], Skeleton, BindObject,
                externalObjectTargets: externalTargets);
            if (selected.UnboundChannels.Count != 0)
                throw new NotSupportedException($"IDLE {idle.Form} has unbound source channels: " +
                    string.Join("; ", selected.UnboundChannels.Select(channel => channel.Source.NodeName + "/" + channel.Reason)));
            var clock = new FalloutIdleAnimationPlayback(selected.Sequence.StartTime, selected.Sequence.StopTime,
                selected.Sequence.Frequency, selected.Sequence.CycleType,
                selected.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), timing.SelectAdditionalLoops(_aiRandom.NextBounded));
            if (_baseAnimation is null) selected.ApplySourceTime(selected.Sequence.StartTime);
            else RuntimeNativeNifAnimation.ApplyLayers((_baseAnimation, _baseAnimationSeconds), (selected, selected.Sequence.StartTime));
            foreach (var old in _animationObjects) old.Free();
            _animationObjects.Clear();
            _animationObjects.AddRange(created);
            foreach (var item in created) item.Visible = true;
            _animation = selected;
            _idleData = timing;
            _idlePlayback = clock;
            _animationSeconds = selected.Sequence.StartTime;
            _idleForm = idle.Form;
            _idleOwner = owner;
            _idleRevision++;
            _idleReplays.Started(idle.Form, timing.ReplayDelaySeconds);
            if (_animationSounds is null)
            {
                _animationSounds = new(stack, content, this, Skeleton.UnitsToMetres, _aiRandom);
                AddChild(_animationSounds);
            }
            AnimationError = null;
            SetMeta("opennv_selected_idle", idle.Form.ToString());
            SetMeta("opennv_selected_animation", identity);
        }
        catch
        {
            foreach (var item in created) item.Free();
            throw;
        }
    }

    public override void _Process(double delta)
    {
        if (Combat?.Dead == true || Combat?.OwnsPose == true || Combat?.Restrained == true) return;
        RestoreAuthoredHeadPose();
        if (_conversationTarget is null) AdvanceAi();
        AdvanceConversationFacing((float)delta);
        if ((_animation is null && _baseAnimation is null && _headTargets is null) || AnimationError is not null) return;
        try
        {
            _idleTextKeyEvents.Clear();
            _idleReplays.Advance((float)delta);
            float Advance(RuntimeNativeNifAnimation animation, double elapsed)
            {
                var sequence = animation.Sequence;
                var duration = sequence.StopTime - sequence.StartTime;
                var next = elapsed * sequence.Frequency;
                return sequence.CycleType switch
                {
                    0 when duration > 0 => sequence.StartTime + (float)(next % duration),
                    2 => sequence.StartTime + (float)Math.Min(next, duration),
                    _ => throw new NotSupportedException($"Source animation cycle {sequence.CycleType} has no clock owner."),
                };
            }
            var wasTraveling = _travelActive;
            if (_baseAnimation is not null)
            {
                _baseElapsedSeconds += delta;
                _baseAnimationSeconds = Advance(_baseAnimation, _baseElapsedSeconds);
            }
            var idleDelta = PreparePackageIdle(delta);
            while (_animation is { } idle && idleDelta > 0)
            {
                var clock = _idlePlayback ?? throw new InvalidOperationException("The active IDLE has no source playback clock.");
                idleDelta = clock.Advance(idleDelta, interval => PublishIdleTextKeys(idle, interval));
                _animationSeconds = clock.SourceSeconds;
                if (!clock.Complete) break;
                idle.ApplySourceTime(_animationSeconds);
                FinishIdle();
                idleDelta = PreparePackageIdle(idleDelta);
            }
            Skeleton.ResetMorphPublication();
            if (Combat?.PackageOwnsPose == true) _animation?.ApplySourceTime(_animationSeconds);
            else if (_baseAnimation is not null && _animation is not null)
                RuntimeNativeNifAnimation.ApplyLayers((_baseAnimation, _baseAnimationSeconds), (_animation, _animationSeconds));
            else if (_baseAnimation is not null) _baseAnimation.ApplySourceTime(_baseAnimationSeconds);
            else _animation?.ApplySourceTime(_animationSeconds);
            if (_baseAnimation is { Sequence.CycleType: 2 } transition && _baseAnimationSeconds >= transition.Sequence.StopTime)
            {
                if (_sitting == 2) CompleteFurnitureEntry();
                else if (_sitting == 4) CompleteFurnitureExit();
            }
            if (wasTraveling && !_travelActive)
            {
                CompleteTravel();
            }
            AdvanceDialoguePackage();
            AdvanceHeadTracking((float)delta);
            AdvanceFaceAnimation(delta);
            PosePublished?.Invoke();
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            AnimationError = error.Message;
            GD.PushError($"OPENNV_NATIVE_ANIMATION_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private void FinishIdle()
    {
        if (_animation is null) return;
        var completed = _idleForm;
        var owner = _idleOwner;
        _animation = null;
        _idleData = null;
        _idlePlayback = null;
        _idleForm = null;
        _idleOwner = null;
        foreach (var item in _animationObjects) item.Free();
        _animationObjects.Clear();
        if (owner == "package-idle") _packageIdles!.Finish();
        // Finished one-shots release their bone and ANIO ownership. The base
        // procedure continues on its existing clock on the next publication.
        if (_baseAnimation is not null) _baseAnimation.ApplySourceTime(_baseAnimationSeconds);
        GD.Print($"OPENNV_NATIVE_IDLE_END reference={Appearance.Reference} idle={completed} owner={owner} sourceSeconds={_animationSeconds:R}");
    }

    private void CancelIdle()
    {
        _animation = null;
        _idleData = null;
        _idlePlayback = null;
        _idleForm = null;
        _idleOwner = null;
        foreach (var item in _animationObjects) item.Free();
        _animationObjects.Clear();
    }

    private void PublishIdleTextKeys(RuntimeNativeNifAnimation animation, FalloutIdleAnimationInterval interval)
    {
        foreach (var key in animation.TextKeys.Where(key => key.Time <= interval.To &&
            (key.Time > interval.From || interval.IncludeFrom && key.Time == interval.From)))
        {
            foreach (var value in key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
            {
                var structural = value.Equals("start", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("StartLoop", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("EndLoop", StringComparison.OrdinalIgnoreCase);
                _idleTextKeyEvents.Add(new
                {
                    idle = _idleForm?.ToString(),
                    sourceSeconds = key.Time,
                    key = value,
                    repeat = _idlePlayback!.CompletedRepeats,
                    disposition = structural ? "source-phase-owner" :
                        _animationSounds?.Dispatch(value) ?? "unbound-runtime-event",
                });
            }
        }
    }

    internal static RuntimeNativeNpc Create(
        FalloutPluginStack stack,
        RuntimeLiveContentSource source,
        FalloutPlacedReference reference,
        float unitsToMetres,
        Func<FalloutNpcAppearance, FalloutNpcAppearancePart, FalloutNifFile, FalloutNifGeometry, Material?>? materialOwner = null,
        IReadOnlyList<FalloutFormKey>? equippedArmor = null, FalloutActorTemplateSelection? selection = null)
    {
        var actor = Create(FalloutNpcAppearanceResolver.Resolve(stack, reference.Base, reference.FormKey, equippedArmor, selection: selection), source,
            unitsToMetres, materialOwner);
        try { actor.BindSourceBehavior(stack, selection); return actor; }
        catch { actor.Free(); throw; }
    }

    internal static RuntimeNativeNpc Create(FalloutNpcAppearance appearance, RuntimeLiveContentSource source,
        float unitsToMetres,
        Func<FalloutNpcAppearance, FalloutNpcAppearancePart, FalloutNifFile, FalloutNifGeometry, Material?>? materialOwner = null)
    {
        var prepared = FalloutNpcPreparedGeometry.Read(appearance, source);
        using var assembly = new Assembly(prepared, source, unitsToMetres, materialOwner);
        while (!assembly.Advance()) { }
        return assembly.Take();
    }
}
