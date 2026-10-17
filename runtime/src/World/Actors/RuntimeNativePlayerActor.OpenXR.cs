using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.OpenXR;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private NativeXrArm? _xrLeftArm, _xrRightArm;
    private NativeXrLeg? _xrLeftLeg, _xrRightLeg;
    private NativeXrSpine? _xrSpine;
    private Basis? _xrBodyHeading;
    private Vector3? _xrGroundOrigin;
    internal string? XrBodyPoseError => _xrSpine is { HeadErrorMeters: > .01f }
        ? "Tracked head exceeds the grounded spine's anatomical reach."
        : _xrLeftLeg is { FootErrorMeters: > .01f } || _xrRightLeg is { FootErrorMeters: > .01f }
            ? "Tracked pelvis exceeds a source foot target's anatomical reach." : null;
    internal void SetTrackedBodyFrame(Basis heading, Vector3 groundOrigin)
    { _xrBodyHeading = heading; _xrGroundOrigin = groundOrigin; }
    internal NativeXrArm XrLeftArm => _xrLeftArm ?? throw new InvalidOperationException("Left tracked arm is unbound.");
    internal NativeXrArm XrRightArm => _xrRightArm ?? throw new InvalidOperationException("Right tracked arm is unbound.");
    internal bool XrRightContactReached => _xrRightArm is { WristErrorMeters: <= .001f };
    internal string? XrContactPoseError => _xrLeftArm is { WristErrorMeters: > .001f } || _xrRightArm is { WristErrorMeters: > .001f }
        ? "Physical wrist exceeds anatomical reach; body/contact motion response is unbound." : null;
    internal Transform3D? XrSupportGrip { get; private set; }
    private Vector3 _xrEyeInActor;
    private int[] _xrTorsoBones = [];
    private int[]? _xrTorsoSourceBones;
    private (int Bone, Transform3D Pose)? _xrWeaponHold;
    private Transform3D? _xrHeadFromEye;
    internal Transform3D? TrackedHeadEyeFrame => _xrHeadFromEye is { } offset
        ? Skeleton.Node.GlobalTransform * Skeleton.Node.GetBoneGlobalPose(Skeleton.BoneIndex("Bip01 Head")) * offset : null;
    internal void SetXrInteraction(bool pointing)
    {
        if (_weaponAttachment is not null) _weaponAttachment.Visible = !_first && !_drawn || _drawn && !pointing;
    }
    internal object XrHandState => new
    {
        owner = GetInstanceId(),
        skeleton = Skeleton.Node.GetInstanceId(),
        left = _xrLeftArm?.State,
        right = _xrRightArm?.State,
        sourceEyeInActor = new[] { _xrEyeInActor.X, _xrEyeInActor.Y, _xrEyeInActor.Z },
        headErrorMeters = _xrSpine?.HeadErrorMeters,
        error = XrBodyPoseError,
        legs = _xrLeftLeg is null ? null : new
        {
            leftFootErrorMeters = _xrLeftLeg.FootErrorMeters,
            rightFootErrorMeters = _xrRightLeg!.FootErrorMeters,
            policy = "source-animated-foot-targets;tracked-pelvis;room-scale-stepping-and-slope-adaptation-unbound"
        },
        devices = Actor.Appearance.Models.Count(part => (part.BipedSlots & 64) != 0)
    };

    internal void EnableTrackedArms(RuntimeNativePlayerActor? firstPersonSource = null)
    {
        if (_xrLeftArm is not null) throw new InvalidOperationException("Tracked arms already have an owner.");
        var node = Skeleton.Node;
        var torso = new SortedSet<int>();
        foreach (var name in new[] { "Bip01 L UpperArm", "Bip01 R UpperArm" })
            for (var bone = node.GetBoneParent(Skeleton.BoneIndex(name)); bone >= 0; bone = node.GetBoneParent(bone)) torso.Add(bone);
        _xrTorsoBones = torso.ToArray();
        if (_first)
            _xrEyeInActor = node.Transform * node.GetBoneGlobalRest(Skeleton.BoneIndex("Camera1st")).Origin;
        else
        {
            if (firstPersonSource is not { _first: true }) throw new InvalidOperationException("World body needs the source eye-to-head frame.");
            var headFromEye = NativeXrEyeFrame.InHead(Actor);
            _xrHeadFromEye = headFromEye;
            _xrEyeInActor = node.Transform * (node.GetBoneGlobalRest(Skeleton.BoneIndex("Bip01 Head")) * headFromEye).Origin;
        }
        var previous = Enumerable.Range(0, node.GetBoneCount()).Select(node.GetBonePose).ToArray();
        var closed = Clip("h2haim"); closed.ApplySourceTime(closed.Sequence.StartTime);
        var fingers = Enumerable.Range(0, node.GetBoneCount()).Select(node.GetBonePose).ToArray();
        for (var index = 0; index < previous.Length; index++) node.SetBonePose(index, previous[index]);
        _xrLeftArm = new(Skeleton, true, fingers);
        var hasMuzzle = _weaponNodes.Any(node => node.GetMeta("opennv_nif_source_name", "").AsString()
            .Equals("ProjectileNode", StringComparison.OrdinalIgnoreCase));
        _xrRightArm = new(Skeleton, false, fingers, hasMuzzle ? ProjectileTransformInSkeleton().Basis : null);
        if (Weapon is not null)
        {
            var bone = Skeleton.BoneIndex("Weapon");
            if (node.GetBoneParent(bone) != Skeleton.BoneIndex("Bip01 R Hand"))
                throw new NotSupportedException("Tracked weapon has no source right-hand attachment.");
            _xrWeaponHold = (bone, node.GetBonePose(bone));
        }
        if (Weapon?.AnimationGroup.StartsWith("2h", StringComparison.Ordinal) == true)
            XrSupportGrip = _xrRightArm.SourceGrip(true).AffineInverse() * _xrLeftArm.SourceGrip();
        UseWorldColorEncoding();
    }

    internal void PoseTrackedArms(double delta, Transform3D head, Transform3D left, Transform3D right,
        bool leftTracked, bool rightTracked, float leftGrip, float rightGrip, float leftTrigger, float rightTrigger,
        bool leftThumb, bool rightThumb, bool weaponHeld, Basis? rightAim = null,
        Transform3D? leftContact = null, Transform3D? rightContact = null, bool supporting = false, bool bodyPrepared = false)
    {
        if (_xrLeftArm is null || _xrRightArm is null) throw new InvalidOperationException("Tracked arms have no source binding.");
        if (!bodyPrepared) PrepareTrackedBody(head);
        // A flat reload may move the weapon relative to its animated hand.
        // Tracking owns that hand in VR: retain its authored holding transform
        // and finger grip, while the source clip still animates internal model
        // parts and supplies gameplay timing/sound events.
        if (weaponHeld && _xrWeaponHold is { } hold) Skeleton.Node.SetBonePose(hold.Bone, hold.Pose);
        _xrLeftArm.Publish(left, leftTracked, delta, leftGrip, leftTrigger, leftThumb, supporting, contactHand: leftContact);
        _xrRightArm.Publish(right, rightTracked, delta, rightGrip, rightTrigger, rightThumb, weaponHeld, rightAim, rightContact);
        if (_xrSpine is null && _xrHeadFromEye is { } headFromEye)
        {
            var index = Skeleton.BoneIndex("Bip01 Head");
            var target = Skeleton.Node.GlobalTransform.AffineInverse() * head * headFromEye.AffineInverse();
            var parent = Skeleton.Node.GetBoneParent(index);
            Skeleton.Node.SetBonePose(index, Skeleton.Node.GetBoneGlobalPose(parent).AffineInverse() * target);
        }
    }

    internal void PrepareTrackedBody(Transform3D head)
    {
        Transform3D? leftFoot = null, rightFoot = null;
        Vector3? anchoredOrigin = null;
        if (_xrLeftLeg is not null && _xrGroundOrigin is { } groundOrigin && _xrBodyHeading is { } heading)
        {
            anchoredOrigin = groundOrigin - heading * new Vector3(_xrEyeInActor.X, 0, _xrEyeInActor.Z);
            var ground = new Transform3D(heading, anchoredOrigin.Value);
            leftFoot = ground * _xrLeftLeg.FootInActor;
            rightFoot = ground * _xrRightLeg!.FootInActor;
        }
        foreach (var bone in _xrTorsoBones) Skeleton.Node.SetBonePose(bone, Skeleton.Node.GetBoneRest(bone));
        var forward = -head.Basis.Z; forward.Y = 0;
        if (forward.LengthSquared() < .0001f) forward = -GlobalBasis.Z;
        var body = _xrBodyHeading ?? Basis.LookingAt(forward.Normalized(), Vector3.Up);
        GlobalTransform = new(body, head.Origin - body * _xrEyeInActor);
        if (leftFoot is { } groundedLeft && rightFoot is { } groundedRight)
        {
            // Horizontal head motion bends the spine above a grounded pelvis.
            var anchor = anchoredOrigin!.Value;
            var worldHead = head * _xrHeadFromEye!.Value.AffineInverse();
            var sourceHead = Skeleton.Node.Transform * Skeleton.Node.GetBoneGlobalRest(Skeleton.BoneIndex("Bip01 Head")).Origin;
            GlobalPosition = new(anchor.X, worldHead.Origin.Y - sourceHead.Y, anchor.Z);
            GlobalPosition += Vector3.Up * _xrSpine!.PelvisHeightCorrection(worldHead);
            var drop = Math.Max(_xrLeftLeg!.RequiredPelvisDrop(groundedLeft), _xrRightLeg!.RequiredPelvisDrop(groundedRight));
            GlobalPosition -= Vector3.Up * drop;
            _xrLeftLeg.Publish(groundedLeft); _xrRightLeg.Publish(groundedRight);
            _xrSpine!.Publish(worldHead);
        }
    }

    internal void AdoptTrackedTorso(RuntimeNativePlayerActor body)
    {
        if (!_first || !body._trackedBodyView) throw new InvalidOperationException("Attachments require the prepared full-body torso.");
        GlobalTransform = body.GlobalTransform;
        var mapping = Skeleton.Node.GlobalTransform.AffineInverse() * body.Skeleton.Node.GlobalTransform;
        _xrTorsoSourceBones ??= _xrTorsoBones.Select(bone => body.Skeleton.TryBoneIndex(Skeleton.Node.GetBoneName(bone), out var source) ? source : -1).ToArray();
        // First-person camera/looking helpers have no world-body counterpart.
        // Retain their rest locals; shared anatomical anchors get world poses.
        foreach (var bone in _xrTorsoBones) Skeleton.Node.SetBonePose(bone, Skeleton.Node.GetBoneRest(bone));
        for (var index = 0; index < _xrTorsoBones.Length; index++)
        {
            var bone = _xrTorsoBones[index];
            var source = _xrTorsoSourceBones[index];
            if (source < 0) continue;
            var pose = mapping * body.Skeleton.Node.GetBoneGlobalPose(source);
            var parent = Skeleton.Node.GetBoneParent(bone);
            Skeleton.Node.SetBonePose(bone, parent < 0 ? pose : Skeleton.Node.GetBoneGlobalPose(parent).AffineInverse() * pose);
        }
    }

    internal (string Path, Node3D Root)? WristDevice()
    {
        var matches = Actor.Appearance.Models.Select((source, index) => (source, root: Actor.Parts[index].Root))
            .Where(value => (value.source.BipedSlots & 64) != 0 && value.source.ModelPath is not null).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => (matches[0].source.ModelPath!, matches[0].root),
            _ => throw new InvalidOperationException("Equipped Pip-Boy slot has multiple presentation owners."),
        };
    }

    private void UseWorldColorEncoding()
    {
        foreach (var mesh in FindChildren("*", "", true, false).OfType<MeshInstance3D>())
            for (var index = 0; index < mesh.Mesh.GetSurfaceCount(); index++)
                if (mesh.GetActiveMaterial(index) is ShaderMaterial shader)
                {
                    if (shader.ResourceName is NativeNifLightingMaterial.ResourceIdentity or NativeFaceGenMaterial.ResourceIdentity)
                        NativeNifPointLighting.Bind(shader, [], 1, storeEncoded: false);
                    if (shader.ResourceName == NativeNifEffectMaterial.ResourceIdentity) shader.SetShaderParameter("source_store_encoded", false);
                }
    }
}
