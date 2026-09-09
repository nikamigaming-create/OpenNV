using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Actors;

/// <summary>The player's source body, equipment attachment and animation palette.</summary>
internal sealed partial class RuntimeNativePlayerActor : Node3D
{
    private readonly RuntimeLiveContentSource _content;
    private readonly FalloutPluginStack _records;
    private readonly bool _first;
    private readonly string _directory;
    private readonly Dictionary<string, RuntimeNativeNifAnimation> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Node3D> _weaponNodes = [];
    private string? _group;
    private double _seconds;
    private RuntimeNativeNifAnimation? _active;
    private RuntimeNativeNifAnimation? _grip;
    private RuntimeNativeNifAnimation? _idle;
    private RuntimeNativeNifAnimation? _pipBoyIdle;
    private RuntimeNativeNifAnimation? _movement;
    private string? _movementPath, _movementFallback;
    private double _movementSeconds;
    private readonly Dictionary<RuntimeNativeNifAnimation, float> _movementSpeeds = [];
    private readonly Dictionary<string, bool> _availableClips = new(StringComparer.OrdinalIgnoreCase);
    private readonly (RuntimeNativeNifAnimation, float)[] _animationLayers = new (RuntimeNativeNifAnimation, float)[5];
    internal RuntimeNativeNpc Actor { get; }
    internal FalloutWeaponPresentation? Weapon { get; }
    internal RuntimeNativeNifSkeleton Skeleton => Actor.Skeleton;
    internal string? Error { get; private set; }
    internal object State => new
    {
        firstPerson = _first,
        weapon = Weapon?.Form.ToString(),
        group = _group,
        seconds = _seconds,
        bones = Skeleton.Node.GetBoneCount(),
        error = Error,
        visibility = BodyVisibilityState,
        movement = _movementPath,
        movementFallback = _movementFallback,
        movementSeconds = _movementSeconds,
        drawn = _drawn,
        action = _actionClip?.Sequence.Name,
        actionSeconds = _actionSeconds,
        weaponSurfaces = _weaponNodes.OfType<MeshInstance3D>().Select(mesh => new
        {
            name = mesh.GetMeta("opennv_nif_source_name", "").AsString(),
            visible = mesh.IsVisibleInTree(),
            position = new[] { mesh.GlobalPosition.X, mesh.GlobalPosition.Y, mesh.GlobalPosition.Z }
        }).ToArray(),
        unbound = "transition-blending,attack-damage,animation-audio-acceptance,cold-animation-phase"
    };
    internal Transform3D SourceCamera => Skeleton.Node.Transform * Skeleton.Node.GetBoneGlobalPose(Skeleton.BoneIndex("Camera1st"));
    internal double PipBoyDuration => PipBoyClip.TextKeys.Single(key => key.Value.Equals("Hit", StringComparison.OrdinalIgnoreCase)).Time - PipBoyClip.Sequence.StartTime;
    private RuntimeNativeNifAnimation PipBoyClip => Clip($"locomotion/{(Actor.Appearance.Female ? "female/pipboyfemale" : "male/pipboy")}");
    internal void PosePipBoy(double seconds, float raised = 1, double? buttonSeconds = null)
    {
        if (!_first) throw new InvalidOperationException("Pip-Boy hand pose needs the first-person source skeleton.");
        var clip = PipBoyClip;
        var time = clip.Sequence.StartTime + (float)Math.Clamp(buttonSeconds ?? PipBoyDuration, 0, PipBoyDuration);
        _pipBoyIdle ??= ClipPath(FalloutActorIdleSource.Resolve(_records, Actor.Appearance.Female ? "PipboyWaverFemale" : "PipboyWaver").AnimationPath);
        Skeleton.Node.ResetBonePoses();
        Skeleton.Node.SetBonePose(Skeleton.BoneIndex(_idle!.Sequence.TargetName), Transform3D.Identity);
        _idle!.ApplySourceTime(_idle.Sequence.StartTime);
        var lowered = raised < 1 ? Enumerable.Range(0, Skeleton.Node.GetBoneCount()).Select(Skeleton.Node.GetBonePose).ToArray() : [];
        RuntimeNativeNifAnimation.ApplyLayers((_idle, _idle.Sequence.StartTime), (clip, time), (_pipBoyIdle, SourceTime(_pipBoyIdle, seconds)));
        if (raised < 1)
            for (var bone = 0; bone < lowered.Length; bone++)
                Skeleton.Node.SetBonePose(bone, lowered[bone].InterpolateWith(Skeleton.Node.GetBonePose(bone), Math.Clamp(raised, 0, 1)));
        _group = "Pipboy"; _seconds = seconds;
    }

    internal RuntimeNativePlayerActor(FalloutPluginStack records, RuntimeLiveContentSource content, FalloutNpcAppearance appearance,
        FalloutFormKey? weapon, bool firstPerson, float units, Color ambient)
    {
        Name = firstPerson ? "NativeFirstPersonBody" : "NativeThirdPersonBody";
        _records = records; _content = content; _first = firstPerson;
        _directory = firstPerson ? "meshes/characters/_1stperson" : appearance.SkeletonPath[..appearance.SkeletonPath.LastIndexOf('/')];
        var models = firstPerson ? appearance.Models.Where(part => part.Role is "body" or "hand-left" or "hand-right" or "armor" or "armor-addon")
            .Select(FirstPersonPart).ToArray() : appearance.Models;
        try
        {
            Actor = RuntimeNativeNpc.Create(appearance with { Models = models, SkeletonPath = firstPerson ? _directory + "/skeleton.nif" : appearance.SkeletonPath }, content, units,
                (npc, part, nif, geometry) => NativeNpcMaterial.Resolve(npc, part, nif, geometry, records, ambient));
            AddChild(Actor); Actor.SetProcess(false);
            var pipBoy = appearance.EquippedArmor.Any(key => (records.GetEffective(key).ReadSubrecords()
                .Single(field => field.Signature == "BMDT").Data.Span[0] & 0x40) != 0);
            for (var index = 0; index < Actor.Parts.Count; index++)
            {
                var part = models[index]; var source = FalloutNifFile.Read(Read(part.ModelPath!));
                foreach (var mesh in Actor.Parts[index].Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
                {
                    if (!mesh.HasMeta("opennv_nif_geometry_block")) continue;
                    var name = source.ReadGeometry(mesh.GetMeta("opennv_nif_geometry_block").AsInt32()).Name;
                    if (name.StartsWith("PipBoyOn", StringComparison.OrdinalIgnoreCase)) mesh.Visible &= pipBoy;
                    else if (name.StartsWith("PipBoyOff", StringComparison.OrdinalIgnoreCase)) mesh.Visible &= !pipBoy;
                    else if (firstPerson && !name.Equals("Arms", StringComparison.OrdinalIgnoreCase) &&
                        (part.Role == "body" || part.Role == "armor" && (part.BipedSlots & 4) != 0))
                        mesh.Visible = false;
                    if (name is "PipboyLightEffect:0") mesh.Visible = false;
                }
            }
            if (weapon is { } form)
            {
                Weapon = FalloutWeaponPresentation.Read(records, form, firstPerson);
                var source = FalloutNifFile.Read(Read(Weapon.Model.ModelPath ?? throw new InvalidDataException("Player weapon has no model.")));
                var targets = source.Blocks.Where(block => block.TypeName is "NiNode" or "NiTriShape" or "NiTriStrips")
                    .Select(block => source.ReadObject(block.Index) is FalloutNifNode node ? node.Name : source.ReadGeometry(block.Index).Name).ToHashSet(StringComparer.Ordinal);
                var scene = RuntimeNativeNifMeshBuilder.Build(source, units, externalTransformTargets: targets);
                NativeNifCollisionBuilder.BindAnimatedAttachment(scene.Root);
                var attachment = new BoneAttachment3D { Name = "EquippedWeapon", BoneName = "Weapon" };
                Skeleton.Node.AddChild(attachment); attachment.AddChild(scene.Root);
                _weaponAttachment = attachment; _weaponRoot = scene.Root; _weaponRest = scene.Root.Transform;
                _weaponNodes.AddRange(scene.Root.FindChildren("*", "", true, false).OfType<Node3D>());
                foreach (var mesh in _weaponNodes.OfType<MeshInstance3D>())
                {
                    if (!mesh.HasMeta("opennv_nif_geometry_block")) continue;
                    var geometry = source.ReadGeometry(mesh.GetMeta("opennv_nif_geometry_block").AsInt32());
                    mesh.MaterialOverride = NativeNifMeshBuilder.BuildMaterial(source, geometry,
                        texturePaths: NativeNpcMaterial.Alternate(Weapon.Model, source, geometry));
                    foreach (var property in geometry.Properties.Where(index => index >= 0).Select(source.ReadObject))
                        Skeleton.MaterialChannels.Add(geometry.Name, source, property, [mesh.MaterialOverride]);
                }
            }
            SetAmbient(ambient);
            _idle = Clip(firstPerson ? "mtidle" : "locomotion/mtidle");
            if (Weapon?.Grip is { } grip && grip != 255)
            {
                var suffix = grip switch { 230 => 1, 231 => 2, 232 => 3, _ => throw new NotSupportedException($"WEAP grip {grip} is unbound.") };
                _grip = Clip(Weapon.AnimationGroup + "handgrip" + suffix);
            }
            Advance(0, Vector3.Zero, true, false);
        }
        catch { Free(); throw; }
    }

    private byte[] Read(string path) => _content.TryRead(path, null, out var bytes, out _) ? bytes : throw new FileNotFoundException("Player source resource is missing.", path);
    private FalloutNpcAppearancePart FirstPersonPart(FalloutNpcAppearancePart part)
    {
        if ((part.BipedSlots & 0x18) == 0 || part.ModelPath is not { } path || !path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) return part;
        // Owned hand variants carry inverse binds for the first-person finger
        // hierarchy. Keeping the third-person inverse binds distorts the grip.
        var candidate = path[..^4] + "1st.nif";
        return _content.TryRead(candidate, null, out _, out _) ? part with { ModelPath = candidate } : part;
    }
    private RuntimeNativeNifAnimation Clip(string name) => ClipPath(_directory + "/" + name + ".kf");
    private RuntimeNativeNifAnimation ClipPath(string path)
    {
        if (_clips.TryGetValue(path, out var cached)) return cached;
        var source = FalloutNifFile.Read(Read(path));
        var sequences = source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        var requestedGroup = Path.GetFileNameWithoutExtension(path);
        // A KF may export several groups. Its requested resource name selects
        // the matching named sequence, after the weapon/locomotion prefix.
        var matching = sequences.Where(value => requestedGroup.EndsWith(value.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var sequence = sequences.Length == 1 ? sequences[0] : matching.Length == 1 ? matching[0] :
            throw new NotSupportedException($"Player source {path} has no unique group among: " + string.Join(", ", sequences.Select(value => value.Name)));
        Action<float>? Other(FalloutNifControllerLink link)
        {
            var nodes = _weaponNodes.Where(node => node.GetMeta("opennv_nif_source_name", "").AsString() == link.NodeName).ToArray();
            if (nodes.Length > 1)
            {
                var rendered = nodes.Where(node => node is MeshInstance3D || node.FindChildren("*", "MeshInstance3D", true, false).Count != 0).ToArray();
                if (rendered.Length == 1)
                {
                    SetMeta("opennv_weapon_empty_alias", link.NodeName);
                    nodes = rendered;
                }
            }
            if (nodes.Length != 1) return null;
            if (link.ControllerType == "NiVisController")
            {
                var visibility = new FalloutNifBoolAnimation(source, link.Interpolator);
                return time => nodes[0].Visible = visibility.Sample(time);
            }
            if (link.ControllerType != "NiTransformController") return null;
            var sampler = new FalloutNifAnimationSampler(source, link.Interpolator);
            return time =>
            {
                var sample = sampler.Sample(time);
                if (sample.Translation is { } position) nodes[0].Position = GamebryoCoordinate.ConvertVector(new(position.X, position.Y, position.Z)) * Skeleton.UnitsToMetres;
                if (sample.Rotation is { } rotation) nodes[0].Quaternion = new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized();
                if (sample.Scale is { } scale) nodes[0].Scale = Vector3.One * scale;
            };
        }
        // Movement remains with the player's collision controller. The KF's
        // accumulation channel is retained separately from local bone poses.
        var clip = new RuntimeNativeNifAnimation(source, sequence, Skeleton, Other, accumulationRoot: sample =>
        {
            if (sample.Translation is { } root) SetMeta("opennv_accumulation", new Vector3(root.X, root.Y, root.Z));
        }, externalObjectTargets: _weaponNodes.Select(node => node.GetMeta("opennv_nif_source_name", "").AsString()).ToHashSet(StringComparer.Ordinal));
        if (clip.UnboundChannels.Count != 0) throw new NotSupportedException($"Player clip {path}: " +
            string.Join("; ", clip.UnboundChannels.Select(channel => channel.Source.NodeName + "/" + channel.Reason)));
        _clips.Add(path, clip); return clip;
    }
    private static float SourceTime(RuntimeNativeNifAnimation clip, double seconds)
    {
        var sequence = clip.Sequence; var duration = sequence.StopTime - sequence.StartTime;
        var clock = (float)(seconds * sequence.Frequency);
        return sequence.StartTime + (duration <= 0 ? 0 : sequence.CycleType == 0 ? clock % duration : Math.Min(clock, duration));
    }
    internal void SetAmbient(Color ambient)
    {
        foreach (var mesh in Actor.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
        {
            NativeNifMaterialEnvironment.Bind(mesh, new(ambient.R, ambient.G, ambient.B), Vector3.Zero, new(100000, 200000, 1), 1);
            if (!_first) continue;
            for (var index = 0; index < mesh.Mesh.GetSurfaceCount(); index++)
                if (mesh.GetActiveMaterial(index) is ShaderMaterial shader)
                {
                    if (shader.ResourceName is NativeNifLightingMaterial.ResourceIdentity or NativeFaceGenMaterial.ResourceIdentity)
                        NativeNifPointLighting.Bind(shader, [], 1, storeEncoded: true);
                    if (shader.ResourceName == NativeNifEffectMaterial.ResourceIdentity) shader.SetShaderParameter("source_store_encoded", true);
                }
        }
    }
    internal void Advance(double delta, Vector3 movement, bool grounded, bool aiming)
    {
        if (Error is not null) return;
        AdvanceMuzzle(delta);
        try
        {
            var group = _drawn && Weapon is { } weapon ? weapon.AnimationGroup + (aiming ? "aimis" : "aim") : _first ? "mtidle" : "locomotion/mtidle";
            if (group != _group) { _active = Clip(group); _group = group; _seconds = 0; }
            _seconds += delta * (Weapon?.AnimationMultiplier ?? 1);
            SelectMovement(delta, movement, grounded);
            Skeleton.Node.ResetBonePoses();
            Skeleton.Node.SetBonePose(Skeleton.BoneIndex(_idle!.Sequence.TargetName), Transform3D.Identity);
            var count = 0;
            if (_idle is not null) _animationLayers[count++] = (_idle, SourceTime(_idle, _seconds));
            if (_movement is not null) _animationLayers[count++] = (_movement, SourceTime(_movement, _movementSeconds));
            if (_active is not null && _active != _idle) _animationLayers[count++] = (_active, SourceTime(_active, _seconds));
            if (_drawn && _grip is not null) _animationLayers[count++] = (_grip, SourceTime(_grip, _seconds));
            if (_actionClip is not null) _animationLayers[count++] = (_actionClip, SourceTime(_actionClip, _actionSeconds));
            RuntimeNativeNifAnimation.ApplyLayers(_animationLayers.AsSpan(0, count));
            PublishWeaponActionParent();
        }
        catch (Exception error) { Error = error.Message; GD.PushError("OPENNV_PLAYER_ANIMATION_UNBOUND " + error); }
    }

    private void SelectMovement(double delta, Vector3 velocity, bool grounded)
    {
        var speed = new Vector2(velocity.X, velocity.Z).Length();
        if (grounded && speed < .05f) { _movement = null; _movementPath = null; _movementSeconds = 0; return; }
        var family = _drawn ? Weapon?.AnimationGroup ?? "mt" : "mt";
        var direction = MathF.Abs(velocity.Z) >= MathF.Abs(velocity.X) ? velocity.Z < 0 ? "forward" : "backward" : velocity.X < 0 ? "left" : "right";
        var prefix = !_first && family == "mt" ? $"{(Actor.Appearance.Female ? "female" : "male")}/mt" : family;
        var requested = grounded ? $"locomotion/{prefix}{direction}" : family + "jumploop";
        bool Exists(string name)
        {
            if (!_availableClips.TryGetValue(name, out var exists))
            { exists = _content.TryRead(_directory + "/" + name + ".kf", null, out _, out _); _availableClips.Add(name, exists); }
            return exists;
        }
        _movementFallback = null;
        if (!Exists(requested))
        {
            var common = grounded ? $"locomotion/{(_first ? "" : Actor.Appearance.Female ? "female/" : "male/")}mt{direction}" : "mtjumploop";
            _movementFallback = requested + " -> " + common + "; retail-group-inheritance-unverified";
            requested = common;
        }
        var selected = Clip(requested);
        if (grounded)
        {
            var fastPath = requested.Replace(direction, "fast" + direction, StringComparison.Ordinal);
            if (Exists(fastPath))
            {
                var fast = Clip(fastPath);
                var walkSpeed = MovementSpeed(selected); var fastSpeed = MovementSpeed(fast);
                // Source accumulation supplies gait speed. First-person clips
                // without accumulation use the controller's run presentation.
                if (walkSpeed == 0 || MathF.Abs(speed - fastSpeed) < MathF.Abs(speed - walkSpeed))
                { selected = fast; requested = fastPath; }
            }
        }
        if (selected != _movement) { _movement = selected; _movementPath = requested; _movementSeconds = 0; }
        var authoredSpeed = grounded ? MovementSpeed(selected) : 0;
        _movementSeconds += delta * (authoredSpeed > 0 ? speed / authoredSpeed : 1);
    }

    private float MovementSpeed(RuntimeNativeNifAnimation clip)
    {
        if (_movementSpeeds.TryGetValue(clip, out var speed)) return speed;
        var sequence = clip.Sequence;
        var root = sequence.ControlledBlocks.SingleOrDefault(link => link.NodeName == sequence.TargetName && link.ControllerType == "NiTransformController");
        speed = 0;
        if (root is not null)
        {
            var sampler = new FalloutNifAnimationSampler(clip.Source, root.Interpolator);
            if (sampler.Sample(sequence.StartTime).Translation is { } start && sampler.Sample(sequence.StopTime).Translation is { } end)
                speed = new Vector2(end.X - start.X, end.Y - start.Y).Length() * Skeleton.UnitsToMetres * sequence.Frequency / (sequence.StopTime - sequence.StartTime);
        }
        _movementSpeeds.Add(clip, speed); return speed;
    }
}
