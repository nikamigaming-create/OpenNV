using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutEarlyControllerShapeInvocation(Guid Initialization, FalloutCombatActorIdentity Actor,
    Guid Process, long Epoch, Guid Constructor, int ThreadId);
internal sealed record FalloutEarlyControllerNativeShape(string Role, Shape3D Shape, Transform3D Transform, ulong NativeId);
internal sealed record FalloutEarlyControllerShapeCall(FalloutEarlyControllerShapeInvocation Invocation,
    FalloutPlayerControllerConstructionInputs Inputs, bool ReusedConvex, IReadOnlyList<ulong> Allocated,
    IReadOnlyList<ulong> ReturnedNativeShapes, bool Returned, string? Failure);

// The cache owns actual resources, and each lease owns its fresh slot0 capsule.
// No resource is attached to a default world by this source construction.
internal sealed class FalloutEarlyControllerShapeCache
{
    private readonly FalloutPhysicalConstructorInputs _inputs;
    private readonly Action<FalloutEarlyControllerShapeInvocation> _requireActualConstruction;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly float _physicalToNative;
    private readonly HashSet<FalloutEarlyControllerShapeLease> _leases = [];
    private readonly List<FalloutEarlyControllerNativeShape> _pendingRetirement = [];
    private readonly List<FalloutEarlyControllerShapeCall> _calls = [];
    private IReadOnlyList<FalloutEarlyControllerNativeShape>? _convex;
    private FalloutStandardControllerConvexInputs? _convexInputs;
    private FalloutEarlyControllerConvexGeometry? _geometry;
    private bool _busy, _retired;
    private long _fault;
    private string? _failure;
    internal FalloutEarlyControllerShapeCall? LastCall { get; private set; }
    internal string? SaveBlocker => _busy ? "source-early-controller-shape-construction-in-flight" : _failure ??
        (_retired ? "source-early-controller-shape-cache-retired" : null);

    internal FalloutEarlyControllerShapeCache(FalloutPhysicalConstructorInputs inputs, float sourceUnitsToMetres,
        Action<FalloutEarlyControllerShapeInvocation> requireActualConstruction)
    {
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(requireActualConstruction);
        if (!float.IsFinite(sourceUnitsToMetres) || sourceUnitsToMetres <= 0)
            throw new InvalidDataException("Early controller shape conversion has no positive current source units.");
        _inputs = inputs; _requireActualConstruction = requireActualConstruction;
        _physicalToNative = FalloutPhysicalFloat32.Multiply(BitConverter.UInt32BitsToSingle(FalloutPhysicalConstructorInputs.PhysicalToSourceBits),
            sourceUnitsToMetres);
        if (!float.IsFinite(_physicalToNative) || _physicalToNative <= 0)
            throw new InvalidDataException("Early controller conversion lost its finite positive native scale.");
    }

    internal FalloutEarlyControllerShapeLease Acquire(FalloutEarlyControllerShapeInvocation invocation,
        FalloutPlayerControllerConstructionInputs player)
    {
        RequireOwner(); ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(player);
        if (_failure is not null) throw new InvalidOperationException("Early shape factory retains its original failed construction: " + _failure);
        var created = new List<FalloutEarlyControllerNativeShape>(); var reused = _convex is not null; var fault = _fault;
        _busy = true;
        try
        {
            Guard();
            if (player != _inputs.ProjectPlayer(player.ReferencePosition, player.StandardCenter) ||
                player.SelectedShapeSlot != FalloutPhysicalConstructorInputs.SelectedConvexShapeSlot)
                throw new InvalidDataException("Early shape factory changed the actual selected source projection.");
            var capsule = CreateCapsule("slot-zero-capsule", player.CapsuleNegativeEndpoint, player.CapsulePositiveEndpoint,
                BitConverter.UInt32BitsToSingle(player.CapsuleRadiusBits), created, Guard);
            var convexInputs = _inputs.ProjectStandardConvex(player, 0);
            if (convexInputs.Points.Count != FalloutEarlyControllerConvexGeometry.StandardPointCount)
                throw new InvalidDataException("Early controller shape construction changed the eighteen-point source extent.");
            if (_convex is null)
            {
                var geometry = new FalloutEarlyControllerConvexGeometry(convexInputs); Guard();
                var convex = CreateRoundedConvex(geometry, created, Guard);
                _geometry = geometry; _convexInputs = convexInputs; _convex = Array.AsReadOnly(convex.ToArray());
            }
            else if (_convexInputs!.ConvexRadiusBits != convexInputs.ConvexRadiusBits ||
                _convexInputs.SpecializedRadiusBits != convexInputs.SpecializedRadiusBits ||
                _convexInputs.KeepContactDistanceBits != convexInputs.KeepContactDistanceBits ||
                !_convexInputs.Points.SequenceEqual(convexInputs.Points))
                throw new NotSupportedException("source-shared-standard-controller-shape-current-input-writer-unbound");
            Guard();
            var lease = new FalloutEarlyControllerShapeLease(this, capsule, _convex!, _geometry!, player, _physicalToNative);
            _leases.Add(lease);
            var call = new FalloutEarlyControllerShapeCall(invocation, player, reused, created.Select(value => value.NativeId).ToArray(),
                new[] { capsule.NativeId }.Concat(_convex!.Select(value => value.NativeId)).ToArray(), true, null);
            _calls.Add(call); LastCall = call; return lease;
        }
        catch (Exception error)
        {
            _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            var call = new FalloutEarlyControllerShapeCall(invocation, player, reused, created.Select(value => value.NativeId).ToArray(), [], false, _failure);
            _calls.Add(call); LastCall = call;
            // A published shared cache outlives the failed caller. Unpublished
            // resources retire in reverse order; rejected releases remain real.
            var published = _convex?.Select(value => value.NativeId).ToHashSet() ?? [];
            RetireResources(created.Where(value => !published.Contains(value.NativeId)).Reverse());
            throw;
        }
        finally { _busy = false; }

        void Guard()
        {
            if (_fault != fault || _retired || invocation.Initialization == Guid.Empty || invocation.Constructor == Guid.Empty ||
                invocation.Process == Guid.Empty || invocation.Epoch < 1 || invocation.ThreadId != _thread ||
                _thread != System.Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Early shape child lost its actual constructor, process, epoch or source thread.");
            invocation.Actor.Validate(); _requireActualConstruction(invocation);
            if (_fault != fault || _retired || _thread != System.Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Early shape guard swallowed reentry or replaced its current source owner.");
        }
    }

    private List<FalloutEarlyControllerNativeShape> CreateRoundedConvex(FalloutEarlyControllerConvexGeometry geometry,
        List<FalloutEarlyControllerNativeShape> created, Action guard)
    {
        var output = new List<FalloutEarlyControllerNativeShape>();
        var radius = BitConverter.UInt32BitsToSingle(geometry.RadiusBits);
        output.Add(CreateConvex("slot-one-core", geometry.Points, created, guard));
        for (var faceIndex = 0; faceIndex < geometry.Faces.Count; ++faceIndex)
        {
            var face = geometry.Faces[faceIndex];
            for (var index = 1; index < face.Vertices.Count - 1; ++index)
            {
                FalloutPhysicalVector3[] triangle = [geometry.Points[face.Vertices[0]], geometry.Points[face.Vertices[index]],
                    geometry.Points[face.Vertices[index + 1]]];
                var points = triangle.Concat(triangle.Select(value => AddRadius(value, face.OutwardNormal, radius))).ToArray();
                output.Add(CreateConvex($"slot-one-face/{faceIndex}/{index - 1}", points, created, guard));
            }
        }
        for (var index = 0; index < geometry.Edges.Count; ++index)
        {
            var edge = geometry.Edges[index];
            output.Add(CreateCapsule("slot-one-edge/" + index, geometry.Points[edge.First], geometry.Points[edge.Second], radius, created, guard));
        }
        foreach (var index in geometry.Vertices)
        {
            var transform = new Transform3D(Basis.Identity, ToNative(geometry.Points[index]));
            output.Add(Create("slot-one-vertex/" + index, () => new SphereShape3D(), shape =>
            { shape.Radius = radius * _physicalToNative; shape.Margin = 0; }, transform, created, guard));
        }
        return output;
    }

    private FalloutEarlyControllerNativeShape CreateConvex(string role, IReadOnlyList<FalloutPhysicalVector3> points,
        List<FalloutEarlyControllerNativeShape> created, Action guard) => Create(role, () => new ConvexPolygonShape3D(), shape =>
        { shape.Points = points.Select(ToNative).ToArray(); shape.Margin = 0; }, Transform3D.Identity, created, guard);

    private FalloutEarlyControllerNativeShape CreateCapsule(string role, FalloutPhysicalVector3 first, FalloutPhysicalVector3 second,
        float radius, List<FalloutEarlyControllerNativeShape> created, Action guard)
    {
        var a = ToNative(first); var b = ToNative(second); var axis = b - a;
        if (!float.IsFinite(radius) || radius <= 0 || !a.IsFinite() || !b.IsFinite() || !axis.IsFinite() || axis.LengthSquared() == 0)
            throw new InvalidDataException("Early physical capsule requires two distinct finite endpoints and a positive source radius.");
        var up = axis.Normalized(); var right = MathF.Abs(up.Dot(Vector3.Right)) < .9f ? Vector3.Right : Vector3.Forward;
        right = (right - up * right.Dot(up)).Normalized();
        var basis = new Basis(right, up, right.Cross(up));
        var transform = new Transform3D(basis, (a + b) * .5f);
        return Create(role, () => new CapsuleShape3D(), shape =>
        { shape.Radius = radius * _physicalToNative; shape.Height = axis.Length() + 2 * shape.Radius; shape.Margin = 0; }, transform, created, guard);
    }

    private static FalloutPhysicalVector3 AddRadius(FalloutPhysicalVector3 point, FalloutPhysicalVector3 normal, float radius) =>
        FalloutPhysicalVector3.FromValues(point.XValue + normal.XValue * radius, point.YValue + normal.YValue * radius, point.ZValue + normal.ZValue * radius);
    private Vector3 ToNative(FalloutPhysicalVector3 source) => new(source.XValue * _physicalToNative, source.ZValue * _physicalToNative,
        -source.YValue * _physicalToNative);
    private static FalloutEarlyControllerNativeShape Create<T>(string role, Func<T> allocate, Action<T> configure,
        Transform3D transform, List<FalloutEarlyControllerNativeShape> created, Action guard) where T : Shape3D
    {
        guard(); var shape = allocate(); var id = shape.GetRid().Id;
        var resource = new FalloutEarlyControllerNativeShape(role, shape, transform, id); created.Add(resource);
        configure(shape); guard();
        if (id == 0 || shape.GetRid().Id != id) throw new InvalidDataException("Early shape factory did not retain its actual native resource.");
        return resource;
    }

    internal void RequireLease(FalloutEarlyControllerShapeLease lease)
    {
        if (_retired || _thread != System.Environment.CurrentManagedThreadId || !_leases.Contains(lease))
            throw new InvalidOperationException("Early shape lease lost its actual source cache/thread lifetime.");
    }
    internal void Release(FalloutEarlyControllerShapeLease lease, FalloutEarlyControllerNativeShape capsule)
    {
        RequireLease(lease);
        try { capsule.Shape.Dispose(); _leases.Remove(lease); }
        catch
        {
            _failure ??= "source-early-controller-capsule-release-rejected";
            // The actual borrower and resource stay retained for a real retry.
            throw;
        }
    }
    internal object Capture()
    {
        RequireOwner();
        return new
        {
            source = _inputs.EngineSha256,
            physicalToNative = _physicalToNative,
            activeLeases = _leases.Count,
            nativeCache = _convex?.Select(value => new { value.Role, value.NativeId }).ToArray(),
            calls = _calls.ToArray(),
            failure = _failure,
            pendingRetirement = _pendingRetirement.Select(value => value.NativeId).ToArray(),
            canonical = FalloutEarlyControllerConvexGeometry.CanonicalBoundary,
            controller = "INDEPENDENT_UNRETURNED"
        };
    }
    internal void Retire()
    {
        if (_retired) return;
        RequireOwner();
        if (_leases.Count != 0) throw new InvalidOperationException("Early shape cache must outlive every actual controller shape lease.");
        var resources = _pendingRetirement.Concat(_convex ?? []).DistinctBy(value => value.NativeId).Reverse().ToArray();
        _pendingRetirement.Clear(); _convex = null; RetireResources(resources);
        if (_pendingRetirement.Count != 0)
        {
            _failure ??= "source-early-controller-shape-retirement-rejected";
            throw new InvalidOperationException("Early shape retirement retains rejected native resource releases.");
        }
        _retired = true;
    }
    private void RetireResources(IEnumerable<FalloutEarlyControllerNativeShape> resources)
    {
        foreach (var resource in resources)
            try { resource.Shape.Dispose(); }
            catch { _pendingRetirement.Add(resource); }
    }
    private void RequireOwner()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("Early native shapes require their actual constructor thread.");
        if (_busy) { _fault = checked(_fault + 1); throw new InvalidOperationException("Early native shape factory reentered."); }
    }
}

internal sealed class FalloutEarlyControllerShapeLease : IDisposable
{
    private readonly FalloutEarlyControllerShapeCache _cache;
    private readonly FalloutEarlyControllerNativeShape _capsule;
    private readonly IReadOnlyList<FalloutEarlyControllerNativeShape> _convex;
    private bool _retired;
    internal FalloutEarlyControllerConvexGeometry Geometry { get; }
    internal FalloutPlayerControllerConstructionInputs Inputs { get; }
    internal float PhysicalToNative { get; }
    internal FalloutEarlyControllerShapeLease(FalloutEarlyControllerShapeCache cache, FalloutEarlyControllerNativeShape capsule,
        IReadOnlyList<FalloutEarlyControllerNativeShape> convex, FalloutEarlyControllerConvexGeometry geometry,
        FalloutPlayerControllerConstructionInputs inputs, float physicalToNative)
    { _cache = cache; _capsule = capsule; _convex = convex; Geometry = geometry; Inputs = inputs; PhysicalToNative = physicalToNative; }
    internal IReadOnlyList<FalloutEarlyControllerNativeShape> Slot(int index)
    {
        if (_retired) throw new ObjectDisposedException(nameof(FalloutEarlyControllerShapeLease));
        _cache.RequireLease(this);
        return index switch { 0 => [_capsule], 1 => _convex, _ => throw new InvalidDataException("Unknown source controller shape slot.") };
    }
    public void Dispose() { if (_retired) return; _cache.Release(this, _capsule); _retired = true; }
}
