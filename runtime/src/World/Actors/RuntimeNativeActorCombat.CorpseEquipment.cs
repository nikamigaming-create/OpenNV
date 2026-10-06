using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutCorpseRouteRetirement? _deathRouteRetirement;
    private string? _corpseVisualBoundary;
    private bool _corpseEquipmentRestored;
    private bool? _retiredCorpseEquipmentReady;
    private bool? _retiredCorpseEquipmentFiniteReady;

    private void RetireDeathPursuit()
    {
        if (!Dead) throw new InvalidOperationException("Only authoritative death can retire a corpse's pursuit intent.");
        if (_routeSearch is not null || _routeDoor is not null || _routeError is not null || _coarseRouteError is not null)
            _deathRouteRetirement ??= new(_routeSearch is not null, _routeError, _coarseRouteError, _routeFailures,
                _routeDoor?.Reference, _routeDoor?.Requested ?? false, _routeDoor?.Seconds ?? 0, _routeDoor?.Error);
        if (_routeSearch is not null) { DisposeRouteSearch(); _retiredPursuitSearches++; }
        // A requested door action belongs to the reference event/motion owner.
        // Retire only this dead actor's wait; never cancel or complete the door.
        _routeDoor = null; _routeProbe = null; _routeIntent = null;
        _pursuitPath = []; _pursuitCursor = 0; _routeClock = 0; _routeStall = 0;
        PackageOwnsPose = false; PackageMoving = false;
        if (_actor is CharacterBody3D body) body.Velocity = Vector3.Zero;
        Activity.SetMovement(false, false);
        RetainCorpseVisualBoundary();
    }

    private void RetainCorpseVisualBoundary()
    {
        var targets = new[] { _enemyObject, _packageWeapon }.Where(attachment => attachment is not null)
            .SelectMany(attachment => attachment!.Targets).ToHashSet(StringComparer.Ordinal);
        var clips = _combatClips.Values.Concat(new[] { _packageAim, _packageGrip, _hitReactionClip }
            .Where(clip => clip is not null).Select(clip => clip!));
        var channel = clips.SelectMany(clip => clip.Animation.Sequence.ControlledBlocks)
            .FirstOrDefault(link => targets.Contains(link.NodeName) && link.ControllerType is not ("NiTransformController" or "NiVisController"));
        if (channel is not null)
            _corpseVisualBoundary ??= $"Corpse weapon has an unowned source visual channel: {channel.NodeName}/{channel.ControllerType}.";
    }

    private bool CorpseEquipmentCaptureReady(bool allowFiniteSoundWait)
    {
        if ((allowFiniteSoundWait ? _retiredCorpseEquipmentFiniteReady : _retiredCorpseEquipmentReady) is { } retired) return retired;
        if (!Dead) return true;
        try
        {
            _ = CaptureCorpseEquipment();
            foreach (var sounds in new[] { _enemySounds, _hitReactionSounds, _packageSounds })
                if (sounds?.CanCaptureSilent == false && !(allowFiniteSoundWait && sounds.CanAwaitFiniteCompletion))
                    throw new NotSupportedException("Corpse equipment retains an unfinished or unowned source sound continuation.");
            _state.CorpseEquipmentCaptureBlocker = null;
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            _state.CorpseEquipmentCaptureBlocker = error.Message;
            return false;
        }
    }

    private FalloutActorCorpseEquipment? CaptureCorpseEquipment()
    {
        if (!Dead) return null;
        if (!_deathPresentationAttempted || !_corpseEquipmentRestored || _ragdoll?.CaptureReady != true)
            throw new NotSupportedException("Corpse equipment awaits its actual native physical and attachment owners.");
        if (_routeSearch is not null || _routeDoor is not null || PackageOwnsPose || PackageMoving ||
            _state.HitReaction is not null || _pendingHitscanImpacts != 0)
            throw new NotSupportedException("Corpse equipment has a pending route, hit or independent motion continuation.");
        if (Error is not null || _engagementError is not null || _assistanceError is not null)
            throw new NotSupportedException("Corpse equipment retains an unowned combat failure.");
        if (_enemyMuzzle?.CaptureReady == false || _enemyShotEffects?.CaptureReady == false)
            throw new NotSupportedException("Corpse equipment has a pending source attack effect, projectile, decal or effect fault.");
        if (_corpseVisualBoundary is { } boundary) throw new NotSupportedException(boundary);
        if (_enemyObject is null && _packageWeapon is null && _enemyWeaponHandling is null && _deathRouteRetirement is null)
        {
            if (_state.Engagement?.WeaponHandling is not null || _state.CorpseEquipment is not null)
                throw new NotSupportedException("Corpse equipment lost an already retained independent owner.");
            return null;
        }
        var attachments = new List<FalloutCorpseWeaponAttachment>();
        if (_enemyObject is not null) attachments.Add(_enemyObject.CapturePersistence("combat", _records));
        if (_packageWeapon is not null) attachments.Add(_packageWeapon.CapturePersistence("package", _records));
        var handlingSource = _enemyWeaponHandling is null ? null : _enemyWeapon is { } weapon
            ? new FalloutCorpseWeaponSource(weapon.Form, FalloutActorFurnitureContinuation.RecordHash(_records.GetEffective(weapon.Form)))
            : throw new NotSupportedException("Corpse weapon handling has no actual selected source weapon.");
        var pose = _actor.GlobalTransform;
        var root = new float[] { pose.Basis.X.X, pose.Basis.X.Y, pose.Basis.X.Z, pose.Basis.Y.X, pose.Basis.Y.Y, pose.Basis.Y.Z,
            pose.Basis.Z.X, pose.Basis.Z.Y, pose.Basis.Z.Z, pose.Origin.X, pose.Origin.Y, pose.Origin.Z };
        var result = new FalloutActorCorpseEquipment(root, Activity.Capture(), CaptureCorpseResidualPose(), attachments,
            handlingSource, _enemyWeaponHandling?.Capture(), _embeddedMuzzle?.BoneName.ToString(), _deathRouteRetirement);
        result.ValidateSource(_records, _state.Inventory?.Contents ??
            throw new NotSupportedException("Corpse equipment has no actual retained reference inventory."));
        return result;
    }

    private FalloutActorResidualPose CaptureCorpseResidualPose()
    {
        var node = _skeleton.Node;
        var bones = new List<FalloutActorResidualBonePose>();
        var coverage = _ragdoll!.PoseCoverage;
        for (var index = 0; index < coverage.Count; index++)
        {
            if (coverage[index].Covered == FalloutNifTransformComponents.All) continue;
            var p = node.GetBonePosePosition(index); var q = node.GetBonePoseRotation(index); var s = node.GetBonePoseScale(index);
            bones.Add(new(index, node.GetBoneName(index).ToString(), [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W], [s.X, s.Y, s.Z]));
        }
        return new(_skeletonPath, _skeleton.Source.Sha256, bones);
    }

    private void RestoreCorpseEquipment()
    {
        if (_state.CorpseEquipment is not { } saved) return;
        if (_enemyObject is not null || _packageWeapon is not null || _enemyWeaponHandling is not null || _embeddedMuzzle is not null)
            throw new InvalidOperationException("Corpse equipment restoration needs fresh native owners.");
        var inventory = _state.Inventory?.Contents ?? throw new InvalidDataException("Saved corpse equipment has no inventory.");
        saved.ValidateSource(_records, inventory);
        saved.ResidualPose.ValidateBinding(_skeletonPath, _skeleton.Source.Sha256, _ragdoll!.PoseCoverage);
        FalloutWeaponPresentation? weapon = null;
        FalloutWeaponHandling? handling = null;
        if (saved.HandlingWeapon is { } source)
        {
            weapon = source.ValidateSource(_records, inventory);
            handling = new(inventory, nativeNpc: true);
            handling.Restore(saved.WeaponHandling!, key => FalloutWeaponPresentation.Read(_records, key, false));
        }

        if (saved.EmbeddedMuzzleBone is { } muzzle)
        {
            var names = Enumerable.Range(0, _skeleton.Node.GetBoneCount()).Select(index => _skeleton.Node.GetBoneName(index).ToString())
                .Where(name => name.StartsWith("ProjectileNode", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (names.Length != 1 || names[0] != muzzle)
                throw new InvalidDataException("Saved embedded corpse muzzle differs from its source skeleton.");
        }
        var staged = new List<(FalloutCorpseWeaponAttachment Saved, NativeActorWeaponAttachment Native)>();
        try
        {
            foreach (var attachment in saved.Attachments)
                staged.Add((attachment, new(attachment.Source.ValidateSource(_records, inventory), _skeleton, _content)));
            // Validate the complete graph before publishing any saved pose.
            foreach (var (receipt, native) in staged) native.ValidatePersistence(receipt, _records);
            if (Activity.Alerted != saved.Activity.Alerted)
                throw new InvalidDataException("Corpse activity differs from its shared source alert owner.");
            Activity.Restore(saved.Activity);
            var root = saved.RootPose;
            _actor.GlobalTransform = new(new Vector3(root[0], root[1], root[2]), new Vector3(root[3], root[4], root[5]),
                new Vector3(root[6], root[7], root[8]), new Vector3(root[9], root[10], root[11]));
            foreach (var (receipt, native) in staged) native.RestorePersistence(receipt, _records);
            foreach (var bone in saved.ResidualPose.Bones)
            {
                if (bone.Position is { } p) _skeleton.Node.SetBonePosePosition(bone.Index, new(p[0], p[1], p[2]));
                if (bone.Rotation is { } q) _skeleton.Node.SetBonePoseRotation(bone.Index, new(q[0], q[1], q[2], q[3]));
                if (bone.Scale is { } s) _skeleton.Node.SetBonePoseScale(bone.Index, new(s[0], s[1], s[2]));
            }
            _enemyObject = staged.SingleOrDefault(pair => pair.Saved.Owner == "combat").Native;
            _packageWeapon = staged.SingleOrDefault(pair => pair.Saved.Owner == "package").Native;
            _enemyWeapon = weapon; _enemyWeaponHandling = handling;
            if (_packageWeapon is not null)
            {
                _packageWeaponPrepared = true; _packageWeaponType = _packageWeapon.Weapon.WeaponAnimationType;
            }
            if (saved.EmbeddedMuzzleBone is { } embedded)
            {
                _embeddedMuzzle = new() { Name = "SourceEmbeddedWeaponMuzzle", BoneName = embedded };
                _skeleton.Node.AddChild(_embeddedMuzzle);
            }
            _deathRouteRetirement = saved.RouteRetirement;
        }
        catch
        {
            foreach (var (_, native) in staged) native.FreeFailedRestore();
            throw;
        }
    }

    private void RetainCorpseEquipment()
    {
        if (_state.CaptureCorpseEquipment != CaptureCorpseEquipment) return;
        if (Dead)
        {
            var ready = CorpseEquipmentCaptureReady(false);
            var finiteReady = CorpseEquipmentCaptureReady(true);
            _retiredCorpseEquipmentReady = ready; _retiredCorpseEquipmentFiniteReady = finiteReady;
            if (finiteReady) _state.CorpseEquipment = CaptureCorpseEquipment()?.Copy();
        }
        _state.CanCaptureCorpseEquipment = null; _state.CaptureCorpseEquipment = null;
    }
}
