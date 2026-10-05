using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NativeNavigationContact(ulong Collider, int Shape, Vector3 Point, Vector3 Normal,
    Vector3 From, Vector3 Desired, FalloutFormKey? Reference = null, Vector3? Travel = null);

internal sealed record NativeNavigationSweep(Vector3 From, Vector3 Desired, Vector3 Travel, bool Collided,
    IReadOnlyList<NativeNavigationContact> Contacts);

internal sealed record NativeNavigationLandingSupport(NativeNavigationSweep Capsule, NativeNavigationContact Floor);

internal sealed record NativeNavigationRejection(string Reason, Vector3 From, Vector3 Desired,
    NativeNavigationContact? Contact);

internal sealed class NativeNavigationProbe(NativeNavigationContact? corridorContact, Func<ulong, FalloutFormKey?>? source = null)
{
    internal NativeNavigationContact? CorridorContact { get; } = corridorContact is null ? null :
        corridorContact with { Reference = source?.Invoke(corridorContact.Collider) };
    internal NativeNavigationContact? RejectedContact { get; private set; }
    internal Vector3[] Approach { get; private set; } = [];
    internal int RejectedEdges { get; private set; }
    internal NativeNavigationRejection? LastRejection { get; private set; }
    internal NativeNavigationRejection? GuideRejection { get; private set; }
    internal NativeNavigationSweep? LastDownwardSweep { get; private set; }
    internal NativeNavigationSweep? FirstDownwardSweep { get; private set; }
    internal NativeNavigationSweep? GuideDownwardSweep { get; private set; }
    internal int ReconciledLandings { get; private set; }
    internal NativeNavigationLandingSupport? FirstReconciledLanding { get; private set; }
    internal int OmittedContactObservations { get; private set; }
    internal int OmittedRejectionObservations { get; private set; }
    private readonly Dictionary<ulong, NativeNavigationContact> _contacts = [];
    private readonly Dictionary<string, NativeNavigationRejection> _rejections = [];
    internal IReadOnlyCollection<NativeNavigationContact> RejectedContacts => _contacts.Values;
    internal void Reject(string reason, Vector3 from, Vector3 desired, NativeNavigationContact? contact)
    {
        if (contact is not null) contact = contact with { Reference = source?.Invoke(contact.Collider) };
        LastRejection = new(reason, from, desired, contact);
        if (_rejections.ContainsKey(reason) || _rejections.Count < 16) _rejections[reason] = LastRejection;
        else OmittedRejectionObservations++;
    }
    internal void BeginEdge() => LastDownwardSweep = null;
    internal void ObserveDownward(NativeNavigationSweep sweep)
    {
        LastDownwardSweep = sweep with
        {
            Contacts = sweep.Contacts.Select(contact => contact with { Reference = source?.Invoke(contact.Collider) }).ToArray()
        };
        FirstDownwardSweep ??= LastDownwardSweep;
    }
    internal void ObserveReconciledLanding(NativeNavigationContact support)
    {
        var sweep = LastDownwardSweep ?? throw new InvalidOperationException("Root support requires its downward capsule observation.");
        ReconciledLandings++;
        FirstReconciledLanding ??= new(sweep, support with { Reference = source?.Invoke(support.Collider) });
    }
    internal void RejectGuide()
    {
        GuideRejection = LastRejection;
        GuideDownwardSweep = LastDownwardSweep;
    }

    internal string DescribeFailure()
    {
        static string Contact(NativeNavigationContact? contact) => contact is null ? "none" :
            $"collider={contact.Collider} shape={contact.Shape} reference={contact.Reference?.ToString() ?? "unbound"} point={contact.Point} normal={contact.Normal} " +
            $"from={contact.From} desired={contact.Desired} travel={contact.Travel?.ToString() ?? "unobserved"}";
        static string Sweep(NativeNavigationSweep? sweep) => sweep is null ? "unobserved" :
            $"from={sweep.From} desired={sweep.Desired} travel={sweep.Travel} collided={sweep.Collided} " +
            "contacts=[" + string.Join(";", sweep.Contacts.Select(Contact)) + "]";
        static string Describe(NativeNavigationRejection? rejection) => rejection is null ? "none" :
            $"reason={rejection.Reason} from={rejection.From} desired={rejection.Desired} " +
            $"contact=[{Contact(rejection.Contact)}]";
        return $"directSweepHint=[{Contact(CorridorContact)}] selectedRejectedContact=[{Contact(RejectedContact)}] " +
            $"guide=[{Describe(GuideRejection)}] guideDownward=[{Sweep(GuideDownwardSweep)}] " +
            $"last=[{Describe(LastRejection)}] lastDownward=[{Sweep(LastDownwardSweep)}] rejectedEdges={RejectedEdges} " +
            $"retainedContactBodies={_contacts.Count} contactHistoryLimit=8 omittedContactObservations={OmittedContactObservations} " +
            "rejectionKinds=[" + string.Join(";", _rejections.Values.Select(Describe)) +
            $"] rejectionKindLimit=16 omittedRejectionObservations={OmittedRejectionObservations}";
    }
    internal void Record(NativeNavigationContact contact, Func<Vector3[]> approach)
    {
        RejectedEdges++;
        contact = contact with { Reference = source?.Invoke(contact.Collider) };
        if (_contacts.ContainsKey(contact.Collider) || _contacts.Count < 8) _contacts[contact.Collider] = contact;
        else OmittedContactObservations++;
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
            return new(hit.GetColliderId(index), hit.GetColliderShape(index), hit.GetCollisionPoint(index), normal,
                from, desired, Travel: hit.GetTravel());
        }
        return null;
    }

    private static NativeNavigationContact? FirstContact(PhysicsTestMotionResult3D hit, Vector3 from, Vector3 desired)
        => hit.GetCollisionCount() == 0 ? null : new(hit.GetColliderId(0), hit.GetColliderShape(0),
            hit.GetCollisionPoint(0), hit.GetCollisionNormal(0), from, desired, Travel: hit.GetTravel());

    private static NativeNavigationSweep CaptureSweep(PhysicsTestMotionResult3D hit, Vector3 from, Vector3 desired,
        bool collided) => new(from, desired, hit.GetTravel(), collided,
            Enumerable.Range(0, hit.GetCollisionCount()).Select(index => new NativeNavigationContact(
                hit.GetColliderId(index), hit.GetColliderShape(index), hit.GetCollisionPoint(index),
                hit.GetCollisionNormal(index), from, desired, Travel: hit.GetTravel())).ToArray());

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
