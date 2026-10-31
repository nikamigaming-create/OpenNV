using System.Security.Cryptography;
using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

/// <summary>Original CREA parts share their original skeleton and a reference-owned KF clock.</summary>
internal sealed partial class RuntimeNativeCreature : CharacterBody3D
{
    internal FalloutCreatureAppearance Appearance { get; private set; } = null!;
    internal RuntimeNativeNifSkeleton Skeleton { get; private set; } = null!;
    internal IReadOnlyList<RuntimeNativeNifScene> Parts { get; private set; } = [];
    internal RuntimeNativeActorCombat? Combat { get; set; }
    internal FalloutActorActivityState Activity { get; } = new();
    private FalloutActorAnimationState _clock = null!;
    private RuntimeNativeNifAnimation _animation = null!;
    private FalloutNifTextKeyTimeline _textKeys = null!;
    private NativeOwnedAnimationSoundPlayer _sounds = null!;
    private readonly SortedSet<string> _unbound = new(StringComparer.Ordinal);
    private long _eventCount;
    private object? _lastEvent;
    private Func<Vector3>? _conversationTarget;
    private float _conversationTurnSpeed;
    private Vector3 _conversationScale;

    internal void BeginConversationFacing(Func<Vector3> target, float turnDegreesPerSecond)
    {
        if (!float.IsFinite(turnDegreesPerSecond) || turnDegreesPerSecond <= 0)
            throw new InvalidDataException("Creature dialogue turn speed must be positive.");
        _conversationTarget = target; _conversationTurnSpeed = Mathf.DegToRad(turnDegreesPerSecond);
        _conversationScale = GlobalBasis.Scale;
    }

    internal void EndConversationFacing() => _conversationTarget = null;

    private void AdvanceConversationFacing(float delta)
    {
        if (_conversationTarget is null) return;
        var direction = _conversationTarget() - GlobalPosition; direction.Y = 0;
        if (direction.LengthSquared() < .000001f) return;
        var current = GlobalBasis.Orthonormalized().GetRotationQuaternion();
        var target = Basis.LookingAt(direction.Normalized(), Vector3.Up).GetRotationQuaternion();
        var angle = current.AngleTo(target);
        if (angle > .00001f)
            GlobalBasis = new Basis(current.Slerp(target, Math.Min(1, delta * _conversationTurnSpeed / angle))).Scaled(_conversationScale);
    }
    internal string? Error { get; private set; }
    internal IReadOnlyCollection<string> Unbound => _unbound;
    internal float SourceSeconds => ResolveTime(_clock.ElapsedSeconds);
    internal object Observation => new
    {
        reference = Appearance.Reference?.ToString(),
        creature = Appearance.Creature.ToString(),
        modelOwner = Appearance.ModelOwner.ToString(),
        statsOwner = Appearance.StatsOwner.ToString(),
        Appearance.SkeletonPath,
        Appearance.Models,
        Appearance.BaseScale,
        // These retained fields describe the default clip, which can be dormant
        // while a package/combat/idle owner publishes the current skeleton pose.
        animationMetadataOwner = "default-idle",
        poseOwners = PoseOwners,
        sourceAnimation = _clock.Capture(),
        sequence = _animation.Sequence.Name,
        sourceSeconds = SourceSeconds,
        transformChannels = _animation.TransformChannelCount,
        eventCount = _eventCount,
        lastEvent = _lastEvent,
        absentTargets = _animation.AbsentSourceTargets,
        unbound = _unbound.ToArray(),
        error = Error,
        combat = Combat?.Observation,
        ai = AiState,
    };

    internal static RuntimeNativeCreature Create(FalloutPluginStack stack, RuntimeLiveContentSource content,
        FalloutPlacedReference reference, FalloutReferenceInstance state, float unitsToMetres, bool tracePreparation = false,
        PreparedGeometry? preparedGeometry = null)
    {
        var stepStarted = Stopwatch.GetTimestamp();
        void Mark(string stage)
        {
            if (!tracePreparation) return;
            var now = Stopwatch.GetTimestamp();
            var milliseconds = Stopwatch.GetElapsedTime(stepStarted, now).TotalMilliseconds;
            if (milliseconds > 16.667)
                GD.Print($"OPENNV_NATIVE_CREATURE_PREPARATION_SLOW reference={reference.FormKey} stage={stage} milliseconds={milliseconds:F3}");
            stepStarted = now;
        }
        if (state.Reference != reference.FormKey || state.Base != reference.Base)
            throw new InvalidDataException("Creature presentation is bound to a different reference state.");
        var appearance = FalloutCreatureAppearanceResolver.Resolve(stack, reference.Base, reference.FormKey, state.Templates);
        Mark("source-appearance");
        var actor = new RuntimeNativeCreature { Name = $"Reference_{reference.FormKey}", Appearance = appearance, _clock = state.Animation, _content = content };
        try
        {
            actor.PrepareSandboxAdmissionSource(state);
            actor.PreparePendingSandboxStartSource(stack, state);
            Mark("sandbox-source");
            actor.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
            actor.SetMeta("opennv_creature_form_key", reference.Base.ToString());
            actor.SetMeta("opennv_source_form", appearance.ModelOwner.ToString());
            actor.SetMeta("opennv_source_skeleton", appearance.SkeletonPath);
            if (preparedGeometry is null)
            {
                var preparedSource = PreparedSource.Read(appearance, content, unitsToMetres, CancellationToken.None);
                using var assembly = new GeometryAssembly(preparedSource, content, unitsToMetres);
                while (!assembly.Advance()) { }
                preparedGeometry = assembly.Take();
            }
            using (preparedGeometry)
            {
                var preparedAppearance = preparedGeometry.Source.Appearance;
                if (preparedGeometry.Source.UnitsToMetres != unitsToMetres || preparedAppearance.Creature != appearance.Creature || preparedAppearance.Reference != appearance.Reference ||
                    preparedAppearance.ModelOwner != appearance.ModelOwner || preparedAppearance.StatsOwner != appearance.StatsOwner ||
                    preparedAppearance.SkeletonPath != appearance.SkeletonPath || preparedAppearance.BaseScale != appearance.BaseScale ||
                    preparedAppearance.ModelFlags != appearance.ModelFlags || !preparedAppearance.Models.SequenceEqual(appearance.Models) ||
                    !preparedAppearance.AnimationFiles.SequenceEqual(appearance.AnimationFiles))
                    throw new InvalidDataException("Prepared creature appearance no longer matches its source selection.");
                actor.Skeleton = preparedGeometry.Skeleton;
                preparedGeometry.Adopt(actor);
                actor.Parts = preparedGeometry.Parts;
            }
            var parts = actor.Parts;
            var idle = preparedGeometry.Source.Idle;
            var bytes = preparedGeometry.Source.IdleBytes;
            var animation = preparedGeometry.Source.Animation;
            var sequence = preparedGeometry.Source.Sequence;
            Mark("prepared-geometry");
            if (parts.Count == 0 && actor.Skeleton.GeometryAttachments.Count == 0)
                throw new InvalidDataException("CREA has no source body geometry.");
            if ((appearance.ModelFlags & (1u << 19)) != 0)
                foreach (var mesh in actor.FindChildren("*", "", true, false).OfType<GeometryInstance3D>())
                    mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            var attachments = preparedGeometry.AttachmentTargets;
            var sourceTargets = attachments.Keys.ToHashSet(StringComparer.Ordinal);
            sourceTargets.UnionWith(sequence.ControlledBlocks.Select(link => link.NodeName).Where(name =>
                actor.Skeleton.HasSourceTarget(name) || actor.Skeleton.MaterialChannels.HasSourceTarget(name)));
            actor._animation = new(animation, sequence, actor.Skeleton, link => actor.BindAttachment(animation, link, attachments),
                externalObjectTargets: sourceTargets);
            actor._textKeys = new(actor._animation.TextKeys, sequence.StartTime, sequence.StopTime, sequence.CycleType, sequence.Frequency);
            actor._clock.Bind(idle, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            if (sequence.CycleType == 0)
                actor._clock.StartAmbientLoop(reference.FormKey, (sequence.StopTime - sequence.StartTime) / sequence.Frequency);
            actor.Skeleton.Node.SetBonePose(actor.Skeleton.BoneIndex(sequence.TargetName), Transform3D.Identity);
            actor._animation.ApplySourceTime(actor.SourceSeconds);
            Mark("animation-binding");
            actor.Skeleton.BindSoundSource(actor);
            actor._sounds = new(stack, content, actor, unitsToMetres, state.SoundRandom, state.AnimationSoundEvents);
            actor.AddChild(actor._sounds);
            Mark("sound-owner");
            actor._unbound.UnionWith(["creature-package-procedures-and-special-idle-selection", "combat-and-actor-controller-dynamics",
                "retail-animation-phase-and-blending", "creature-enum-impact-and-sound-events",
                "attachment-controller-and-particle-cold-state", "shaderless-material-lighting-parity"]);
            actor.SetMeta("opennv_selected_animation", idle);
            return actor;
        }
        catch { actor.Free(); throw; }

    }

    public override void _Process(double delta)
    {
        using var frameWork = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.CreatureAnimation);
        if (HasDetachedPendingSandboxStart || Combat?.Dead == true || Combat?.OwnsPose == true) return;
        if (Error is not null) return;
        if (_aiState?.ScriptError is not null) return;
        try
        {
            AdvanceConversationFacing((float)delta);
            if (AdvanceScriptIdle(delta)) return;
            if (AdvanceEventIdle(delta)) return;
            if (Combat?.PackageOwnsPose == true) return;
            AdvanceBaseSource(delta);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
        {
            Error = error.Message;
            GD.PushError($"OPENNV_NATIVE_CREATURE_ANIMATION_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private void AdvanceBaseSource(double delta)
    {
        var from = _clock.ElapsedSeconds;
        var include = _clock.StartPending;
        _clock.Advance(delta);
        foreach (var key in _textKeys.Crossed(from, _clock.ElapsedSeconds, include))
        {
            _animation.ApplySourceTime(key.SourceSeconds);
            var disposition = IsCreatureAuxiliarySound(key.Text) ? DispatchCreatureTextKey(key.Text) : _sounds.Dispatch(key);
            if (disposition.Contains("unbound", StringComparison.Ordinal)) _unbound.Add("source-event:" + key.Text);
            _lastEvent = new { ordinal = ++_eventCount, key, disposition };
            RuntimeNifControllerPlayer.TextKeyObserver?.Invoke(new
            {
                owner = Appearance.Reference?.ToString(),
                controller = "creature-source-KF",
                observation = _lastEvent
            });
        }
        Skeleton.ResetMorphPublication();
        _animation.ApplySourceTime(SourceSeconds);
    }

    private float ResolveTime(double elapsed)
    {
        var sequence = _animation.Sequence;
        var duration = (double)sequence.StopTime - sequence.StartTime;
        return sequence.StartTime + (float)(sequence.CycleType == 0
            ? elapsed * sequence.Frequency % duration : Math.Min(elapsed * sequence.Frequency, duration));
    }

    // Binding is synchronous and does not mutate the source tree. Build a
    // fresh index for each animation so later attachments cannot go stale.
    private Dictionary<string, Node3D?> IndexAttachmentTargets() => IndexAttachmentTargets(this, includeRoot: false);

    private static Dictionary<string, Node3D?> IndexAttachmentTargets(Node3D root, bool includeRoot)
    {
        var targets = new Dictionary<string, Node3D?>(StringComparer.Ordinal);
        var descendants = root.FindChildren("*", "", true, false).OfType<Node3D>();
        foreach (var node in includeRoot ? descendants.Prepend(root) : descendants)
        {
            var name = node.GetMeta("opennv_nif_source_name", "").AsString();
            if (!targets.TryAdd(name, node)) targets[name] = null;
        }
        return targets;
    }

    private Action<float>? BindAttachment(FalloutNifFile source, FalloutNifControllerLink link,
        IReadOnlyDictionary<string, Node3D?> targets)
    {
        if (link.ControllerType is not ("NiTransformController" or "NiVisController") ||
            link.PropertyType.Length != 0 || link.Variable1.Length != 0 || link.Variable2.Length != 0) return null;
        if (!targets.TryGetValue(link.NodeName, out var target) || target is null) return null;
        if (link.ControllerType == "NiVisController")
        {
            var visibility = new FalloutNifBoolAnimation(source, link.Interpolator);
            return time => target.Visible = visibility.Sample(time);
        }
        var sampler = new FalloutNifAnimationSampler(source, link.Interpolator);
        var initial = target.Transform;
        return time =>
        {
            var sample = sampler.Sample(time);
            var basis = sample.Rotation is { } rotation ? new Basis(new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized())
                : initial.Basis.Orthonormalized();
            basis = basis.Scaled(sample.Scale is { } scale ? Vector3.One * scale : initial.Basis.Scale);
            var position = sample.Translation is { } translation
                ? GamebryoCoordinate.ConvertVector(new(translation.X, translation.Y, translation.Z)) * Skeleton.UnitsToMetres : initial.Origin;
            var pose = new Transform3D(basis, position);
            if (!pose.IsFinite()) throw new InvalidDataException("Creature attachment animation is nonfinite.");
            target.Transform = pose;
        };
    }
}
