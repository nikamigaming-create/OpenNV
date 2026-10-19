using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Approved custom identity on one owned skinned body. Classic state remains the owner.</summary>
internal sealed partial class ClassicPlayerBody : Node3D
{
    private FalloutPluginStack _records = null!;
    private RuntimeLiveContentSource? _ownedSource;
    private RuntimeNativeNpc _actor = null!;
    private RuntimeNativeNifAnimation _idle = null!, _walk = null!;
    private RuntimeNativeNifAnimation? _heldPose;
    private RuntimeNativeNifAnimation? _heldGrip;
    private double _idleSeconds;
    private ClassicHumanoidShape? _shape;
    internal void ConfigureShape(float shoulders, float waist)
    {
        _shape = new ClassicHumanoidShape(_actor.Skeleton, shoulders, waist);
        SetMeta("classic_body_proportions", new Vector2(shoulders, waist));
    }
    internal RuntimeNativeNpc Actor => _actor;

    internal void Configure(ClassicAppearanceDraft appearance, string appearanceRoot)
    {
        Name = "ClassicApprovedPlayerBody";
        var source = _ownedSource = RuntimeLiveContentSource.Open(appearanceRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        if (appearance.AppearanceStackId != source.StackId) throw new InvalidDataException("Approved character uses a different appearance source.");
        _records = FalloutPluginStack.Load(source.PluginSources);
        var contract = FalloutNativeRaceSexResolver.Resolve(_records);
        FalloutNativeRaceSexResolver.Validate(contract, appearance.Character);
        // This donor has the unarmored jumpsuit silhouette. It supplies meshes,
        // never classic equipment or armor values. Vault 13 texture fidelity is
        // still unbound, and is explicitly reported by the classic player view.
        var outfit = FalloutDialogueTopic.Find(_records, "ARMO", "VaultSuit101");
        var state = FalloutNativeCharacterCreation.ActorState(_records, contract.Player, appearance.Character);
        var resolved = FalloutNpcAppearanceResolver.Resolve(_records, contract.Player, equippedArmor: [outfit.FormKey], appearanceState: state);
        Configure(source, _records, resolved);
        SetMeta("appearance_identity", System.Text.Json.JsonSerializer.Serialize(appearance));
        SetMeta("clothing_donor", outfit.FormKey.ToString());
        SetMeta("unbound", "vault13-clothing-texture,retail-stride-and-turn-blending");
    }

    internal void Configure(RuntimeLiveContentSource source, FalloutPluginStack records, FalloutNpcAppearance resolved, ClassicPropPalette[]? palette = null)
    {
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        _actor = RuntimeNativeNpc.Create(resolved, source, units,
            (npc, part, nif, geometry) => ClassicPropMaterials.Surface(
                NativeNpcMaterial.Resolve(npc, part, nif, geometry, records, new Color(0.22f, 0.24f, 0.22f), source), nif, geometry, palette));
        AddChild(_actor); _actor.SetProcess(false);
        foreach (var mesh in _actor.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
            mesh.SetInstanceShaderParameter("source_ambient", new Vector3(0.22f, 0.24f, 0.22f));
        var skeleton = resolved.SkeletonPath.Replace('\\', '/'); var directory = skeleton[..skeleton.LastIndexOf('/')];
        RuntimeNativeNifAnimation Load(string path, bool moving)
        {
            if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
            var nif = FalloutNifFile.Read(bytes); var sequence = nif.Roots.Select(nif.ReadControllerSequence).Single();
            if (sequence.CycleType != 0 || sequence.StopTime <= sequence.StartTime) throw new InvalidDataException("Classic donor requires a complete locomotion loop.");
            Action<FalloutNifAnimationSample>? root = moving ? sample =>
            {
                if (sample.Translation is null || sample.Scale is { } scale && scale != 1 ||
                    sample.Rotation is { } rotation && (rotation.X != 0 || rotation.Y != 0 || rotation.Z != 0 || rotation.W != 1))
                    throw new NotSupportedException("Classic donor root has unbound rotation or scale.");
                // The complete root channel is consumed here. Source classic
                // hex motion replaces its translation; no second walk is added.
            }
            : null;
            var animation = new RuntimeNativeNifAnimation(nif, sequence, _actor.Skeleton, accumulationRoot: root);
            if (animation.UnboundChannels.Count != 0) throw new NotSupportedException("Classic donor has unbound animation channels: " +
                string.Join("; ", animation.UnboundChannels.Select(row => row.Reason)));
            _actor.Skeleton.Node.SetBonePose(_actor.Skeleton.BoneIndex(sequence.TargetName), Transform3D.Identity);
            SetMeta(moving ? "walk_source" : "idle_source", identity);
            return animation;
        }
        _idle = Load(directory + "/locomotion/mtidle.kf", false);
        _walk = Load(directory + $"/locomotion/{(resolved.Female ? "female" : "male")}/mtforward.kf", true);
        Publish(false, 0, Vector3.Forward);
    }

    internal void Publish(bool moving, double phase, Vector3 facing)
    {
        _shape?.Restore();
        var animation = moving ? _walk : _idle;
        var duration = animation.Sequence.StopTime - animation.Sequence.StartTime;
        if (moving) _idleSeconds = 0;
        else if (IsInsideTree()) _idleSeconds += GetProcessDeltaTime();
        var seconds = animation.Sequence.StartTime + (moving ? (float)(phase % 1) * duration : (float)(_idleSeconds % duration));
        if (_heldPose is null) animation.ApplySourceTime(seconds);
        else if (_heldGrip is null) RuntimeNativeNifAnimation.ApplyLayers((animation, seconds), (_heldPose, _heldPose.Sequence.StopTime));
        else RuntimeNativeNifAnimation.ApplyLayers((animation, seconds), (_heldPose, _heldPose.Sequence.StopTime), (_heldGrip, _heldGrip.Sequence.StopTime));
        _shape?.Apply();
        if (facing.LengthSquared() > 0) _actor.Basis = Basis.LookingAt(facing.Normalized(), Vector3.Up).Scaled(_actor.Scale);
        SetMeta("classic_pose_moving", moving);
        SetMeta("classic_pose_phase", phase);
        SetMeta("classic_pose_facing", facing);
        SetMeta("classic_pose_clip", animation.Sequence.Name);
        SetMeta("classic_pose_seconds", seconds);
    }

    internal void BindHeldItem(RuntimeLiveContentSource source, ClassicHeldItem item)
    {
        byte[] Read(string path) => source.TryRead(path, null, out var bytes, out _) ? bytes : throw new FileNotFoundException(path);
        _ = _actor.Skeleton.BoneIndex(item.Socket);
        var file = FalloutNifFile.Read(Read(item.Pose));
        var sequence = file.Roots.Select(file.ReadControllerSequence).Single();
        var model = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(Read(item.Model)),
            RuntimeConfiguration.Load().World.GameUnitsToMeters, contentSource: source).Root;
        var committed = false;
        try
        {
            ClassicDonorPresentation.Prepare(model);
            // Shared weapon groups include targets for other models. Supply
            // the selected model's source names, just as the native actor path
            // does. Present targets without a channel owner still fail closed.
            var targets = model.FindChildren("*", "", true, false).OfType<Node3D>().Prepend(model)
                .Where(node => node.HasMeta("opennv_nif_source_name"))
                .Select(node => node.GetMeta("opennv_nif_source_name").AsString()).ToHashSet(StringComparer.Ordinal);
            var pose = new RuntimeNativeNifAnimation(file, sequence, _actor.Skeleton, externalObjectTargets: targets);
            if (pose.UnboundChannels.Count != 0) throw new NotSupportedException("Held-item pose has unbound skeleton channels: " +
                string.Join("; ", pose.UnboundChannels.Select(row => row.Source.NodeName + ": " + row.Reason)));
            RuntimeNativeNifAnimation? grip = null;
            if (item.Grip is { } gripPath)
            {
                var gripFile = FalloutNifFile.Read(Read(gripPath));
                grip = new RuntimeNativeNifAnimation(gripFile, gripFile.Roots.Select(gripFile.ReadControllerSequence).Single(),
                    _actor.Skeleton, externalObjectTargets: targets);
                if (grip.UnboundChannels.Count != 0) throw new NotSupportedException("Held item grip has unbound source channels.");
            }
            var socket = new BoneAttachment3D { Name = "ClassicHeldItemSocket", BoneIdx = _actor.Skeleton.BoneIndex(item.Socket) };
            _actor.Skeleton.Node.AddChild(socket); socket.AddChild(model); committed = true;
            _heldPose = pose; _heldGrip = grip;
            SetMeta("held_source_targets", targets.Order(StringComparer.Ordinal).ToArray());
            SetMeta("held_absent_source_targets", pose.AbsentSourceTargets.Select(row => row.NodeName).Distinct().ToArray());
            SetMeta("classic_held_item", item.Pid); SetMeta("held_model", item.Model); SetMeta("held_pose", item.Pose);
            if (item.Grip is { } selectedGrip) SetMeta("held_grip", selectedGrip);
        }
        finally { if (!committed) model.Free(); }
    }

    public override void _ExitTree() { _records?.Dispose(); _ownedSource?.Dispose(); }
}
