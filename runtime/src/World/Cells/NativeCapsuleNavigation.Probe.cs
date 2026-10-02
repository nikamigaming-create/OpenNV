using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NativeNavigationContact(ulong Collider, int Shape, Vector3 Point, Vector3 Normal,
    Vector3 From, Vector3 Desired, FalloutFormKey? Reference = null);

internal sealed class NativeNavigationProbe(NativeNavigationContact? corridorContact, Func<ulong, FalloutFormKey?>? source = null)
{
    internal NativeNavigationContact? CorridorContact { get; } = corridorContact is null ? null :
        corridorContact with { Reference = source?.Invoke(corridorContact.Collider) };
    internal NativeNavigationContact? RejectedContact { get; private set; }
    internal Vector3[] Approach { get; private set; } = [];
    internal int RejectedEdges { get; private set; }
    private readonly Dictionary<ulong, NativeNavigationContact> _contacts = [];
    internal IReadOnlyCollection<NativeNavigationContact> RejectedContacts => _contacts.Values;
    internal void Record(NativeNavigationContact contact, Func<Vector3[]> approach)
    {
        RejectedEdges++;
        contact = contact with { Reference = source?.Invoke(contact.Collider) };
        if (_contacts.ContainsKey(contact.Collider) || _contacts.Count < 8) _contacts[contact.Collider] = contact;
        // A lateral search may touch many doors. Only the exact source object
        // on the intended corridor qualifies, including its other NIF bodies.
        if (CorridorContact is not { } intended ||
            contact.Collider != intended.Collider && (intended.Reference is null || contact.Reference != intended.Reference) ||
            RejectedContact is { } previous && previous.From.DistanceSquaredTo(intended.Point) <= contact.From.DistanceSquaredTo(intended.Point)) return;
        RejectedContact = contact;
        Approach = approach();
    }
}

internal static partial class NativeCapsuleNavigation
{
    private static NativeNavigationContact? Contact(PhysicsTestMotionResult3D hit, Vector3 from, Vector3 desired, float floorCosine)
    {
        for (var index = 0; index < hit.GetCollisionCount(); index++)
        {
            var normal = hit.GetCollisionNormal(index);
            if (normal.Dot(Vector3.Up) >= floorCosine || normal.Dot(desired - from) >= 0) continue;
            return new(hit.GetColliderId(index), hit.GetColliderShape(index), hit.GetCollisionPoint(index), normal, from, desired);
        }
        return null;
    }

    internal static NativeNavigationContact? FirstCorridorContact(CharacterBody3D body, Vector3 start, IReadOnlyList<Vector3> corridor)
    {
        using var query = new PhysicsTestMotionParameters3D { Margin = body.SafeMargin, MaxCollisions = 4 };
        using var hit = new PhysicsTestMotionResult3D();
        var from = start;
        foreach (var destination in corridor)
        {
            var motion = destination - from; motion.Y = 0;
            query.From = new(body.GlobalBasis, from); query.Motion = motion;
            if (motion.LengthSquared() > .000001f && PhysicsServer3D.BodyTestMotion(body.GetRid(), query, hit) &&
                Contact(hit, from, from + motion, MathF.Cos(body.FloorMaxAngle)) is { } contact) return contact;
            from = destination;
        }
        return null;
    }
}
