using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>One source critter's skinned presentation. MAP state owns position and pose; AI remains separate.</summary>
internal sealed partial class ClassicCreatureBody : Node3D
{
    private RuntimeNativeNifSkeleton _skeleton = null!;
    private RuntimeNativeNifAnimation _animation = null!;
    private double _phase;
    private double _cycleSeconds;
    private bool _idle;
    private bool _staticSourceIdle;
    internal float PresentationHeight { get; private set; }

    internal void Configure(ClassicCreatureBinding binding, RuntimeLiveContentSource library,
        byte[] skeletonBytes, byte[] modelBytes,
        Dictionary<string, (FalloutNifFile File, FalloutNifControllerSequence Sequence)> clips,
        Fallout1NativeMapObject placed, string role, Fallout1NativeFrmFrame frame,
        Fallout1NativeFrmFrame reference, ClassicBlockoutPolicy policy)
    {
        _skeleton = NativeNifMeshBuilder.BuildActorSkeleton(skeletonBytes, RuntimeConfiguration.Load().World.GameUnitsToMeters);
        AddChild(_skeleton.Node);
        var part = NativeNifMeshBuilder.AddActorPart(modelBytes, _skeleton, contentSource: library);
        part.Root.SetMeta("opennv_source_model", binding.Model);
        RuntimeNativeNifAnimation Bind((FalloutNifFile File, FalloutNifControllerSequence Sequence) clip)
        {
            var animation = new RuntimeNativeNifAnimation(clip.File, clip.Sequence, _skeleton,
                accumulationRoot: sample =>
                {
                    // Classic hex translation/facing owns the actor root. Consume
                    // donor accumulation without applying a second world movement.
                    if (sample.Translation is { } translation && (!float.IsFinite(translation.X) ||
                        !float.IsFinite(translation.Y) || !float.IsFinite(translation.Z)))
                        throw new InvalidDataException("Creature root animation is nonfinite.");
                });
            if (animation.UnboundChannels.Count != 0)
                throw new NotSupportedException("Creature animation has unbound channels: " +
                    string.Join("; ", animation.UnboundChannels.Select(row => row.Source.NodeName + ": " + row.Reason)));
            _skeleton.Node.SetBonePose(_skeleton.BoneIndex(clip.Sequence.TargetName), Transform3D.Identity);
            return animation;
        }
        var animations = clips.ToDictionary(row => row.Key, row => Bind(row.Value));
        animations["idle"].ApplySourceTime(animations["idle"].Sequence.StartTime);
        var neighbor = ClassicHexGrid.Neighbor(placed.Tile, placed.Rotation);
        if (neighbor < 0) throw new InvalidDataException("Creature facing leaves the source grid.");
        var facing = Fo1HexMath.Center(neighbor) - Fo1HexMath.Center(placed.Tile);
        _skeleton.Node.Basis = Basis.LookingAt(facing, Vector3.Up) * new Basis(Vector3.Up, Mathf.DegToRad(binding.ForwardYawDegrees));
        var bounds = PosedBounds(part.Root);
        var projected = ClassicSceneryPlacement.Project(_skeleton.Node.Transform * bounds, policy.PixelsPerMeter);
        var scale = Math.Min(reference.Width / projected.Size.X, reference.Height / projected.Size.Y);
        if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Creature source-art scale is invalid.");
        _skeleton.Node.Scale = Vector3.One * scale;
        _skeleton.Node.Position = Vector3.Up * -bounds.Position.Y * scale;
        PresentationHeight = bounds.Size.Y * scale;
        foreach (var mesh in part.Root.GetChildren().OfType<MeshInstance3D>())
            mesh.SetInstanceShaderParameter("source_ambient", new Vector3(0.22f, 0.23f, 0.20f));
        _animation = animations[role];
        _idle = role == "idle" && frame.StoredFps > 0 && frame.FramesPerDirection > 1;
        _staticSourceIdle = role == "idle" && !_idle;
        _cycleSeconds = frame.StoredFps == 0 ? 0 : (double)frame.FramesPerDirection / frame.StoredFps;
        _phase = frame.FramesPerDirection <= 1 ? 0 : (double)placed.Frame / (frame.FramesPerDirection - 1);
        Publish();
        SetMeta("owned_model", binding.Model); SetMeta("owned_game", library.Game); SetMeta("owned_stack", library.StackId);
        SetMeta("source_serial", placed.Serial); SetMeta("source_tile", placed.Tile); SetMeta("source_rotation", placed.Rotation);
        SetMeta("source_fid", placed.Fid.ToString("x8")); SetMeta("source_script", placed.ScriptId.ToString("x8"));
        SetMeta("source_art", binding.ArtBase); SetMeta("source_animation", (int)((placed.Fid >> 16) & 0xff));
        SetMeta("owned_animation", binding.Animations[role]); SetMeta("bound_animation_roles", animations.Keys.ToArray());
        SetMeta("presentation", "owned-skinned-creature; source-pose-and-idle; AI-and-combat-unbound");
    }

    public override void _Process(double delta)
    {
        if (!_idle) return;
        _phase = (_phase + delta / _cycleSeconds) % 1;
        Publish();
    }

    private void Publish()
    {
        var sequence = _animation.Sequence;
        var seconds = sequence.StartTime + (float)_phase * (sequence.StopTime - sequence.StartTime);
        _animation.ApplySourceTime(seconds);
        SetMeta("owned_animation_seconds", seconds);
    }

    internal void PublishPhase(double phase)
    {
        // A one-frame original standing pose still permits the owned 3D idle
        // to breathe. Its clock changes no classic action or source sprite.
        _phase = _staticSourceIdle && IsInsideTree()
            ? (_phase + GetProcessDeltaTime() / (_animation.Sequence.StopTime - _animation.Sequence.StartTime)) % 1
            : phase;
        Publish();
    }

    private Aabb PosedBounds(Node3D part)
    {
        // Measure the visible, posed skin rather than its unanimated vertex box.
        var poses = new Transform3D[_skeleton.Node.GetBoneCount()];
        for (var bone = 0; bone < poses.Length; bone++)
        {
            var parent = _skeleton.Node.GetBoneParent(bone);
            // Godot 4 stores the full local pose, including the rest transform.
            poses[bone] = (parent < 0 ? Transform3D.Identity : poses[parent]) *
                _skeleton.Node.GetBonePose(bone);
        }
        Aabb? bounds = null;
        foreach (var mesh in part.GetChildren().OfType<MeshInstance3D>().Where(mesh => mesh.Visible && mesh.Skin is not null))
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(0);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var influences = bones.Length / vertices.Length;
            for (var vertex = 0; vertex < vertices.Length; vertex++)
            {
                var point = Vector3.Zero;
                for (var influence = 0; influence < influences; influence++)
                {
                    var offset = vertex * influences + influence;
                    var bind = bones[offset];
                    point += (poses[mesh.Skin!.GetBindBone(bind)] * mesh.Skin.GetBindPose(bind) * vertices[vertex]) * weights[offset];
                }
                point = part.Transform * mesh.Transform * point;
                bounds = bounds?.Expand(point) ?? new Aabb(point, Vector3.Zero);
            }
        }
        return bounds ?? throw new InvalidDataException("Creature has no visible skinned bounds.");
    }
}
