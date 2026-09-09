using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

/// <summary>Original CREA parts share their original skeleton and a reference-owned KF clock.</summary>
internal sealed partial class RuntimeNativeCreature : Node3D
{
    internal FalloutCreatureAppearance Appearance { get; private set; } = null!;
    internal RuntimeNativeNifSkeleton Skeleton { get; private set; } = null!;
    internal IReadOnlyList<RuntimeNativeNifScene> Parts { get; private set; } = [];
    internal RuntimeNativeActorCombat? Combat { get; set; }
    private FalloutActorAnimationState _clock = null!;
    private RuntimeNativeNifAnimation _animation = null!;
    private FalloutNifTextKeyTimeline _textKeys = null!;
    private NativeOwnedAnimationSoundPlayer _sounds = null!;
    private readonly SortedSet<string> _unbound = new(StringComparer.Ordinal);
    private long _eventCount;
    private object? _lastEvent;
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
    };

    internal static RuntimeNativeCreature Create(FalloutPluginStack stack, RuntimeLiveContentSource content,
        FalloutPlacedReference reference, FalloutReferenceInstance state, float unitsToMetres)
    {
        if (state.Reference != reference.FormKey || state.Base != reference.Base)
            throw new InvalidDataException("Creature presentation is bound to a different reference state.");
        var appearance = FalloutCreatureAppearanceResolver.Resolve(stack, reference.Base, reference.FormKey);
        var actor = new RuntimeNativeCreature { Name = $"Reference_{reference.FormKey}", Appearance = appearance, _clock = state.Animation };
        try
        {
            actor.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
            actor.SetMeta("opennv_creature_form_key", reference.Base.ToString());
            actor.SetMeta("opennv_source_form", appearance.ModelOwner.ToString());
            actor.SetMeta("opennv_source_skeleton", appearance.SkeletonPath);
            actor.Skeleton = NativeNifMeshBuilder.BuildActorSkeleton(Read(appearance.SkeletonPath), unitsToMetres);
            actor.Skeleton.Node.SetMeta("opennv_source_model", appearance.SkeletonPath);
            actor.Skeleton.Node.Scale = Vector3.One * appearance.BaseScale;
            actor.AddChild(actor.Skeleton.Node);
            var directory = appearance.SkeletonPath[..appearance.SkeletonPath.LastIndexOf('/')];
            var idle = FalloutCreatureAppearanceResolver.SelectIdle(appearance,
                new[] { directory + "/mtidle.kf", directory + "/locomotion/mtidle.kf" }
                    .Where(path => content.TryResolve(path, null, out _)));
            var bytes = Read(idle);
            var animation = FalloutNifFile.Read(bytes);
            var sequences = animation.Roots.Select(animation.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
            if (sequences.Length != 1) throw new NotSupportedException("CREA movement idle requires a unique source KF sequence.");
            var sequence = sequences[0];
            var controlledNodes = sequence.ControlledBlocks.Where(link => link.ControllerType is "NiTransformController" or "NiVisController")
                .Select(link => link.NodeName).ToHashSet(StringComparer.Ordinal);
            var parts = new List<RuntimeNativeNifScene>();
            foreach (var path in appearance.Models)
            {
                try
                {
                    var part = NativeNifMeshBuilder.AddActorPart(Read(path), actor.Skeleton,
                        externalTransformTargets: controlledNodes, contentSource: content);
                    part.Root.SetMeta("opennv_source_model", path);
                    part.Root.SetMeta("opennv_source_form", appearance.ModelOwner.ToString());
                    parts.Add(part);
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
                { throw new NotSupportedException($"CREA {appearance.Creature} part {path}: {error.Message}", error); }
            }
            actor.Parts = parts;
            if (parts.Count == 0 && actor.Skeleton.GeometryAttachments.Count == 0)
                throw new InvalidDataException("CREA has no source body geometry.");
            if ((appearance.ModelFlags & (1u << 19)) != 0)
                foreach (var mesh in actor.FindChildren("*", "", true, false).OfType<GeometryInstance3D>())
                    mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            actor._animation = new(animation, sequence, actor.Skeleton, link => actor.BindAttachment(animation, link));
            actor._textKeys = new(actor._animation.TextKeys, sequence.StartTime, sequence.StopTime, sequence.CycleType, sequence.Frequency);
            actor._clock.Bind(idle, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            actor.Skeleton.Node.SetBonePose(actor.Skeleton.BoneIndex(sequence.TargetName), Transform3D.Identity);
            actor._animation.ApplySourceTime(actor.SourceSeconds);
            actor._sounds = new(stack, content, actor, unitsToMetres, state.SoundRandom);
            actor.AddChild(actor._sounds);
            actor._unbound.UnionWith(["creature-package-procedures-and-special-idle-selection", "combat-and-actor-controller-dynamics",
                "retail-animation-phase-and-blending", "creature-enum-impact-and-sound-events",
                "attachment-controller-and-particle-cold-state", "shaderless-material-lighting-parity"]);
            actor.SetMeta("opennv_selected_animation", idle);
            return actor;
        }
        catch { actor.Free(); throw; }

        byte[] Read(string path) => content.TryRead(path, null, out var bytes, out _)
            ? bytes : throw new FileNotFoundException("Owned creature resource is absent: " + path);
    }

    public override void _Process(double delta)
    {
        if (Combat?.Dead == true) return;
        if (Error is not null) return;
        try
        {
            var from = _clock.ElapsedSeconds;
            var include = _clock.StartPending;
            _clock.Advance(delta);
            foreach (var key in _textKeys.Crossed(from, _clock.ElapsedSeconds, include))
            {
                _animation.ApplySourceTime(key.SourceSeconds);
                var disposition = _sounds.Dispatch(key);
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
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
        {
            Error = error.Message;
            GD.PushError($"OPENNV_NATIVE_CREATURE_ANIMATION_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private float ResolveTime(double elapsed)
    {
        var sequence = _animation.Sequence;
        var duration = (double)sequence.StopTime - sequence.StartTime;
        return sequence.StartTime + (float)(sequence.CycleType == 0
            ? elapsed * sequence.Frequency % duration : Math.Min(elapsed * sequence.Frequency, duration));
    }

    private Action<float>? BindAttachment(FalloutNifFile source, FalloutNifControllerLink link)
    {
        if (link.ControllerType is not ("NiTransformController" or "NiVisController") ||
            link.PropertyType.Length != 0 || link.Variable1.Length != 0 || link.Variable2.Length != 0) return null;
        var targets = FindChildren("*", "", true, false).OfType<Node3D>().Where(node =>
            node.GetMeta("opennv_nif_source_name", "").AsString() == link.NodeName).ToArray();
        if (targets.Length != 1) return null;
        var target = targets[0];
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
