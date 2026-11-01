using System.Runtime.CompilerServices;
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
    private readonly record struct RawContact(ulong Collider, int Shape, Vector3 Point, Vector3 Normal);
    private readonly record struct ResolvedContact(RawContact Raw, FalloutFormKey? Reference);
    [InlineArray(4)]
    private struct ContactScratch { private ResolvedContact _first; }
    private ResolvedContact[] _downwardContacts = [];
    private int _downwardCount;
    private bool _hasDownward, _downwardCollided;
    private Vector3 _downwardFrom, _downwardDesired, _downwardTravel;
    private NativeNavigationSweep? _lastDownwardSweep;
    internal NativeNavigationContact? CorridorContact { get; } = corridorContact is null ? null :
        corridorContact with { Reference = source?.Invoke(corridorContact.Collider) };
    internal NativeNavigationContact? RejectedContact { get; private set; }
    internal Vector3[] Approach { get; private set; } = [];
    internal int RejectedEdges { get; private set; }
    internal long SampledEdges { get; private set; }
    internal NativeNavigationRejection? LastRejection { get; private set; }
    internal NativeNavigationRejection? GuideRejection { get; private set; }
    internal NativeNavigationSweep? LastDownwardSweep => _lastDownwardSweep ??= _hasDownward ? CaptureDownward() : null;
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
    internal void BeginEdge() { SampledEdges++; _lastDownwardSweep = null; _hasDownward = false; }
    private NativeNavigationSweep CaptureDownward()
    {
        var contacts = _downwardCount == 0 ? Array.Empty<NativeNavigationContact>() : new NativeNavigationContact[_downwardCount];
        for (var index = 0; index < contacts.Length; index++)
        {
            var value = _downwardContacts[index]; var raw = value.Raw;
            contacts[index] = new(raw.Collider, raw.Shape, raw.Point, raw.Normal, _downwardFrom, _downwardDesired,
                value.Reference, _downwardTravel);
        }
        return new(_downwardFrom, _downwardDesired, _downwardTravel, _downwardCollided, contacts);
    }
    internal void ObserveDownward(PhysicsTestMotionResult3D hit, Vector3 from, Vector3 desired, bool collided)
    {
        var travel = hit.GetTravel();
        var count = hit.GetCollisionCount();
        // Capture the native fields before source lookup, as in the original
        // two-stage observation. Most sweeps have at most four contacts; do
        // not allocate temporary managed contacts just to add their identities.
        Span<RawContact> raw = count <= 4 ? stackalloc RawContact[count] : new RawContact[count];
        for (var index = 0; index < count; index++)
            raw[index] = new(hit.GetColliderId(index), hit.GetColliderShape(index),
                hit.GetCollisionPoint(index), hit.GetCollisionNormal(index));
        // Resolve every source identity now, preserving lookup/error timing.
        // Inline staging also keeps nested callbacks from aliasing this edge.
        ContactScratch scratch = default;
        Span<ResolvedContact> resolved = scratch;
        if (count > resolved.Length) resolved = new ResolvedContact[count];
        resolved = resolved[..count];
        for (var index = 0; index < count; index++)
            resolved[index] = new(raw[index], source?.Invoke(raw[index].Collider));
        if (_downwardContacts.Length < count) Array.Resize(ref _downwardContacts, Math.Max(4, count));
        resolved.CopyTo(_downwardContacts);
        _downwardFrom = from; _downwardDesired = desired; _downwardTravel = travel;
        _downwardCollided = collided; _downwardCount = count;
        _lastDownwardSweep = null; _hasDownward = true;
        FirstDownwardSweep ??= LastDownwardSweep;
    }
    internal void ObserveDownward(NativeNavigationSweep sweep)
    {
        var captured = sweep with
        {
            Contacts = sweep.Contacts.Select(contact => contact with { Reference = source?.Invoke(contact.Collider) }).ToArray()
        };
        _lastDownwardSweep = captured; _hasDownward = false;
        FirstDownwardSweep ??= captured;
    }
    internal void ObserveReconciledLanding(NativeNavigationContact support)
    {
        if (_lastDownwardSweep is null && !_hasDownward)
            throw new InvalidOperationException("Root support requires its downward capsule observation.");
        ReconciledLandings++;
        FirstReconciledLanding ??= new(LastDownwardSweep!, support with { Reference = source?.Invoke(support.Collider) });
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

    internal static NativeNavigationContact? FirstCorridorContact(CharacterBody3D body, Vector3 start, IReadOnlyList<Vector3> corridor)
    {
        using var query = new PhysicsTestMotionParameters3D { Margin = body.SafeMargin, MaxCollisions = 4 };
        using var hit = new PhysicsTestMotionResult3D();
        var from = start;
        foreach (var destination in corridor)
        {
            var motion = destination - from; motion.Y = 0;
            query.From = new(body.GlobalBasis, from); query.Motion = motion;
            if (motion.LengthSquared() > .000001f && NativeCharacterStep.TestMotion(body.GetRid(), query, hit) &&
                Contact(hit, from, from + motion, MathF.Cos(body.FloorMaxAngle)) is { } contact) return contact;
            from = destination;
        }
        return null;
    }
}
