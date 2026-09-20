using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutBodyPartData? _combatBodyParts;
    private readonly Dictionary<string, byte> _combatHitParts = new(StringComparer.Ordinal);

    internal byte CombatHitPart(Node collider)
    {
        if (collider == this) return 0;
        var actor = _thirdPerson ?? throw new InvalidOperationException("Player combat body is absent.");
        if (!actor.IsAncestorOf(collider)) throw new InvalidOperationException("Combat hit does not belong to the player body.");
        if (!collider.HasMeta("opennv_nif_collision_bone")) throw new NotSupportedException("Player hit lacks a source collision bone.");
        var boneName = collider.GetMeta("opennv_nif_collision_bone").AsString();
        if (_combatHitParts.TryGetValue(boneName, out var cached)) return cached;
        var parts = BodyParts();
        for (var bone = actor.Skeleton.BoneIndex(boneName); bone >= 0; bone = actor.Skeleton.Node.GetBoneParent(bone))
        {
            var current = actor.Skeleton.Node.GetBoneName(bone).ToString();
            var part = parts.Parts.SingleOrDefault(value => value.Node == current);
            if (part is not null) { _combatHitParts.Add(boneName, part.Type); return part.Type; }
        }
        var torso = parts.Parts.Single(value => value.Type == 0).Type;
        _combatHitParts.Add(boneName, torso);
        return torso;
    }

    internal byte? CombatHitPartFrom(Vector3 origin, Node? attacker = null)
    {
        var actor = _thirdPerson ?? throw new InvalidOperationException("Player combat body is absent.");
        if (actor.BodyContacts.Count == 0) throw new NotSupportedException("Player has no source body-part hit volumes.");
        var target = CombatTargetPoint;
        if (!origin.IsFinite() || !target.IsFinite()) throw new InvalidDataException("Player combat ray is invalid.");
        var excluded = new Godot.Collections.Array<Rid> { GetRid() };
        if (attacker is CollisionObject3D source) excluded.Add(source.GetRid());
        if (attacker is not null)
            foreach (var body in attacker.FindChildren("*", "", true, false).OfType<CollisionObject3D>())
                excluded.Add(body.GetRid());
        using var query = PhysicsRayQueryParameters3D.Create(origin, target, _configuration.Player.CollisionLayer, excluded);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        using var collision = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (collision.Count == 0) return null;
        if (collision["collider"].AsGodotObject() is not Node collider) return null;
        if (collider != this && !actor.IsAncestorOf(collider)) return null;
        return CombatHitPart(collider);
    }

    private FalloutBodyPartData BodyParts() => _combatBodyParts ??= FalloutBodyPartData.Read(
        _presentationRecords!.GetEffective(_presentationRecords.RuntimeFormKey(0x1d)));
}
