using Godot;

namespace OpenNV.Runtime.World.Physics;

// A real physics-server query volume, including in --headless. Its retained
// fields are C# state. The source reverse/all-hit collector is an independent
// unreduced producer and fails visibly when a segment actually reaches it.
internal sealed class RuntimeNativePenetrationQuery : IFalloutPenetrationQuery
{
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly Func<string, Rid> _readCurrentSpace;
    private readonly float _physicalToNative;
    private readonly Rid _area;
    private readonly BoxShape3D _shape;
    private readonly uint _filter;
    private FalloutPenetrationBounds _bounds;
    private Rid _space;
    private bool _retired;

    internal RuntimeNativePenetrationQuery(FalloutPenetrationSystemConstruction construction,
        Func<string, Rid> readCurrentSpace, float unitsToMetres)
    {
        ArgumentNullException.ThrowIfNull(construction); ArgumentNullException.ThrowIfNull(readCurrentSpace);
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("Physical query requires actual finite positive world units.");
        _readCurrentSpace = readCurrentSpace;
        _physicalToNative = BitConverter.UInt32BitsToSingle(FalloutPhysicalConstructorInputs.PhysicalToSourceBits) * unitsToMetres;
        _filter = construction.PhantomFilter;
        _shape = new BoxShape3D(); _area = PhysicsServer3D.AreaCreate();
        try
        {
            PhysicsServer3D.AreaSetCollisionLayer(_area, 0); PhysicsServer3D.AreaSetCollisionMask(_area, 0);
            PhysicsServer3D.AreaSetMonitorable(_area, false);
            PublishBounds(construction.Bounds);
            PhysicsServer3D.AreaAddShape(_area, _shape.GetRid());
            BindWorld(construction.Graph.Bodies[0].WorldKey);
        }
        catch { PhysicsServer3D.FreeRid(_area); _shape.Dispose(); throw; }
    }

    public string? WorldKey { get; private set; }
    public bool ReverseQueryFlag { get; private set; }
    internal bool HasPhysicalObject { get { Require(); return _area.IsValid; } }

    public void BindWorld(string? world)
    {
        Require();
        var space = world is null ? default : _readCurrentSpace(world);
        if (world is not null && !space.IsValid) throw new InvalidDataException("Query cannot bind a retired physical world.");
        PhysicsServer3D.AreaSetSpace(_area, space); _space = space; WorldKey = world;
    }

    public void PublishBounds(FalloutPenetrationBounds bounds)
    {
        Require(); var minimum = Vector(bounds.Minimum); var maximum = Vector(bounds.Maximum);
        if (!minimum.IsFinite() || !maximum.IsFinite() || minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z)
            throw new InvalidDataException("Physical query AABB has invalid current source bounds.");
        // Keep the actual source store if its independent native projection
        // refuses. Inflating an aligned segment would change its geometry.
        _bounds = bounds;
        var size = maximum - minimum;
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            throw new NotSupportedException("Degenerate physical query volume requires its actual source shape producer.");
        _shape.Size = new(size.X, size.Z, size.Y);
        var middle = (minimum + maximum) * .5f;
        PhysicsServer3D.AreaSetTransform(_area, new(Basis.Identity, new(middle.X, middle.Z, -middle.Y)));
    }

    public void SetReverseQueryFlag(bool value) { Require(); ReverseQueryFlag = value; }

    public IReadOnlyList<FalloutPenetrationRayHit> CastAll(FalloutPenetrationSegment segment, uint filter)
    {
        RequireWorld();
        if ((filter & 0x7f) != 29)
            throw new InvalidDataException("Physical segment changed its ray filter layer.");
        // Root/count reuse may retain an older phantom group. The ray filter
        // uses the utility's current controller group independently.
        throw new NotSupportedException("source-physical-ray-all-hit-collector-order-and-reverse-query-byte-producer-unowned");
    }

    public FalloutPenetrationQuerySnapshot Capture()
    { RequireWorld(); return new(WorldKey, ReverseQueryFlag, _bounds, _filter); }

    public void Restore(FalloutPenetrationQuerySnapshot saved)
    {
        Require(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.CollisionFilter != _filter) throw new InvalidDataException("Cold query changed its constructed collision filter.");
        PublishBounds(saved.Bounds); BindWorld(saved.WorldKey); ReverseQueryFlag = saved.ReverseQueryFlag;
    }

    private Vector3 Vector(FalloutPhysicalVector3 source) => new(source.XValue * _physicalToNative,
        source.YValue * _physicalToNative, source.ZValue * _physicalToNative);

    private void Require()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (System.Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Physical query requires its constructing thread.");
    }

    private void RequireWorld()
    {
        Require();
        if (WorldKey is { } world && _readCurrentSpace(world) != _space)
            throw new InvalidOperationException("Physical query lost its current world lifetime.");
    }

    public void Dispose()
    {
        if (_retired) return; Require(); PhysicsServer3D.AreaSetSpace(_area, default);
        PhysicsServer3D.FreeRid(_area); _shape.Dispose(); _retired = true;
    }
}
