using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

/// <summary>One source NPC reference and its directly decoded, shared skeleton.</summary>
internal partial class RuntimeNativeNpc : Node3D
{
    internal FalloutNpcAppearance Appearance { get; private set; } = null!;
    internal RuntimeNativeNifSkeleton Skeleton { get; private set; } = null!;
    internal IReadOnlyList<RuntimeNativeNifScene> Parts { get; private set; } = [];
    private RuntimeNativeNifAnimation? _animation;
    private RuntimeNativeNifAnimation? _baseAnimation;
    private float _baseAnimationSeconds;
    private float _animationSeconds;
    private readonly List<Node3D> _animationObjects = [];
    internal event Action? PosePublished;
    internal float BaseSourceSeconds => _baseAnimationSeconds;
    internal string? AnimationError { get; private set; }
    internal object AnimationState => new
    {
        sequence = _animation?.Sequence.Name,
        baseSequence = _baseAnimation?.Sequence.Name,
        baseSourceSeconds = _baseAnimationSeconds,
        ai = AiState,
        face = FaceState,
        sourceSeconds = _animationSeconds,
        sourceFloatProperties = Skeleton.FloatExtraData.Values.Select(value => new
        {
            node = value.Node,
            name = value.Name,
            value = value.Value,
        }).ToArray(),
        lookAtOwner = "unbound",
        visualChannels = Skeleton.VisualChannelState,
        absentBaseTargets = _baseAnimation?.AbsentSourceTargets.Select(link => new { node = link.NodeName, controller = link.ControllerType }).ToArray(),
        absentIdleTargets = _animation?.AbsentSourceTargets.Select(link => new { node = link.NodeName, controller = link.ControllerType }).ToArray(),
        error = AnimationError,
    };

    internal void PlayBaseSequence(FalloutNifFile source, FalloutNifControllerSequence sequence, string owner)
    {
        var animation = new RuntimeNativeNifAnimation(source, sequence, Skeleton);
        if (sequence.ControlledBlocks.Any(link => link.NodeName == sequence.TargetName && link.ControllerType == "NiTransformController"))
            throw new NotSupportedException("Base animation accumulation requires a root-motion owner.");
        Skeleton.Node.SetBonePose(Skeleton.BoneIndex(sequence.TargetName), Transform3D.Identity);
        animation.ApplySourceTime(sequence.StartTime);
        _baseAnimation = animation; _baseAnimationSeconds = sequence.StartTime;
        SetMeta("opennv_base_animation_source", owner);
    }

    internal void PlayIdle(FalloutPluginStack stack, string editorId)
    {
        var idle = FalloutActorIdleSource.Resolve(stack, editorId);
        var path = idle.AnimationPath;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is absent.");
        if (!content.TryRead(path, null, out var bytes, out var identity))
            throw new FileNotFoundException($"Source IDLE animation is absent: {path}");
        var source = FalloutNifFile.Read(bytes);
        var sequences = source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        if (sequences.Length != 1)
            throw new NotSupportedException($"IDLE {idle.Form} requires one source KF sequence, found {sequences.Length}.");
        var created = new List<Node3D>();
        var controlledNodes = sequences[0].ControlledBlocks
            .Where(link => link.ControllerType == "NiTransformController" && link.PropertyType.Length == 0 &&
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
                part.Root.SetMeta("opennv_animation_object_form", item.Form.ToString());
                created.Add(part.Root);
            }
            Action<float>? BindObject(FalloutNifControllerLink link)
            {
                if (link.ControllerType != "NiTransformController" || link.PropertyType.Length != 0 ||
                    link.Variable1.Length != 0 || link.Variable2.Length != 0) return null;
                var matches = created.SelectMany(root => root.FindChildren("*", "", true, false).OfType<Node3D>().Prepend(root))
                    .Where(node => node.Name.ToString().Equals(link.NodeName, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1) return null;
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
            var selected = new RuntimeNativeNifAnimation(source, sequences[0], Skeleton, BindObject);
            if (_baseAnimation is null) selected.ApplySourceTime(selected.Sequence.StartTime);
            else RuntimeNativeNifAnimation.ApplyLayers((_baseAnimation, _baseAnimationSeconds), (selected, selected.Sequence.StartTime));
            foreach (var old in _animationObjects) old.Free();
            _animationObjects.Clear();
            _animationObjects.AddRange(created);
            foreach (var item in created) item.Visible = true;
            _animation = selected;
            _animationSeconds = selected.Sequence.StartTime;
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
        AdvanceAi();
        if ((_animation is null && _baseAnimation is null) || AnimationError is not null) return;
        try
        {
            float Advance(RuntimeNativeNifAnimation animation, float time)
            {
                var sequence = animation.Sequence;
                var duration = sequence.StopTime - sequence.StartTime;
                var next = time + (float)delta * sequence.Frequency;
                return sequence.CycleType switch
                {
                    0 when duration > 0 => sequence.StartTime + (next - sequence.StartTime) % duration,
                    2 => Math.Min(next, sequence.StopTime),
                    _ => throw new NotSupportedException($"Source animation cycle {sequence.CycleType} has no clock owner."),
                };
            }
            if (_baseAnimation is not null) _baseAnimationSeconds = Advance(_baseAnimation, _baseAnimationSeconds);
            if (_animation is not null) _animationSeconds = Advance(_animation, _animationSeconds);
            if (_baseAnimation is not null && _animation is not null)
                RuntimeNativeNifAnimation.ApplyLayers((_baseAnimation, _baseAnimationSeconds), (_animation, _animationSeconds));
            else if (_baseAnimation is not null) _baseAnimation.ApplySourceTime(_baseAnimationSeconds);
            else _animation!.ApplySourceTime(_animationSeconds);
            PosePublished?.Invoke();
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentOutOfRangeException)
        {
            AnimationError = error.Message;
            GD.PushError($"OPENNV_NATIVE_ANIMATION_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    internal static RuntimeNativeNpc Create(
        FalloutPluginStack stack,
        RuntimeLiveContentSource source,
        FalloutPlacedReference reference,
        float unitsToMetres,
        Func<FalloutNpcAppearance, FalloutNpcAppearancePart, FalloutNifFile, FalloutNifGeometry, Material?>? materialOwner = null)
        => Create(FalloutNpcAppearanceResolver.Resolve(stack, reference.Base, reference.FormKey), source,
            unitsToMetres, materialOwner);

    internal static RuntimeNativeNpc Create(FalloutNpcAppearance appearance, RuntimeLiveContentSource source,
        float unitsToMetres,
        Func<FalloutNpcAppearance, FalloutNpcAppearancePart, FalloutNifFile, FalloutNifGeometry, Material?>? materialOwner = null)
    {
        if (!appearance.CanConstruct)
            throw new NotSupportedException(string.Join("; ", appearance.Blockers));
        var actor = new RuntimeNativeNpc
        {
            Name = appearance.Reference is { } reference ? $"Reference_{reference}" : $"Actor_{appearance.Npc}",
            Appearance = appearance,
        };
        try
        {
            actor.Skeleton = NativeNifMeshBuilder.BuildActorSkeleton(Read(appearance.SkeletonPath), unitsToMetres);
            actor.AddChild(actor.Skeleton.Node);
            // The NPC NAM6/NAM7 fields are unused in this engine. In particular,
            // their legal zero values must not collapse the entire skeleton.
            actor.Skeleton.Node.Scale = Vector3.One * appearance.RaceHeight;
            var head = appearance.Models.SingleOrDefault(part => part.Role == "head");
            var headBinds = head?.ModelPath is { } headPath
                ? FalloutNpcFaceAttachment.ReadHeadBinds(FalloutNifFile.Read(Read(headPath))) : null;
            var parts = new List<RuntimeNativeNifScene>();
            foreach (var part in appearance.Models)
            {
                if (part.ModelPath is null)
                    throw new InvalidDataException($"NPC model {part.Source}/{part.Role} has no NIF path.");
                try
                {
                    var selectedShape = FalloutNpcAppearanceHairShape.Select(appearance, part);
                    var morphSource = FalloutNpcAppearanceMorph.Resolve(source, part, selectedShape);
                    var morph = morphSource is null ? null : new FalloutNpcFaceGeometry(appearance, part, morphSource.Geometry, selectedShape);
                    var expressions = FalloutNpcFaceMorph.Resolve(source, appearance, part, morphSource?.Geometry, selectedShape);
                    var scene = NativeNifMeshBuilder.AddActorPart(Read(part.ModelPath), actor.Skeleton,
                        materialOverride: (nif, geometry) => materialOwner?.Invoke(appearance, part, nif, geometry),
                        geometryOwner: morph is null ? null : morph.Apply,
                        rigidFaceBinds: FalloutNpcFaceAttachment.UsesHeadModelSpace(part.Role)
                            ? headBinds ?? throw new NotSupportedException("Rigid FaceGen part has no source skinned head owner.") : null,
                        selectedGeometryName: selectedShape,
                        morphOwner: expressions is null ? null : expressions.Build);
                    scene.Root.SetMeta("opennv_source_model", part.ModelPath);
                    scene.Root.SetMeta("opennv_source_part", part.Role);
                    scene.Root.SetMeta("opennv_source_form", part.Source.ToString());
                    if (morphSource is not null)
                        scene.Root.SetMeta("opennv_source_egm", morphSource.ResourceOwner);
                    parts.Add(scene);
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
                {
                    throw new NotSupportedException($"NPC {appearance.Npc} part {part.Role} ({part.ModelPath}): {error.Message}", error);
                }
            }
            actor.Parts = parts;
            actor.BindFaceTargets();
            if (appearance.Reference is { } key) actor.SetMeta("opennv_reference_form_key", key.ToString());
            actor.SetMeta("opennv_npc_form_key", appearance.Npc.ToString());
            actor.SetMeta("opennv_source_skeleton", appearance.SkeletonPath);
            return actor;
        }
        catch
        {
            // No incomplete body is published when any authored part fails.
            actor.Free();
            throw;
        }

        byte[] Read(string path) => source.TryRead(path, null, out var bytes, out _)
            ? bytes : throw new FileNotFoundException($"NPC source resource is missing: {path}");
    }
}
