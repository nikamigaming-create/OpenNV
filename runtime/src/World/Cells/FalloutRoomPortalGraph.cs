using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using V3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutRoomGeometry(FalloutFormKey Reference, bool Enabled, FalloutVisibilityBound Bound);
internal sealed record FalloutPortalGeometry(FalloutFormKey Reference, bool Enabled, IReadOnlyList<V3> Rectangle);
internal sealed record FalloutRoomCamera(V3 Origin, V3 NearPoint, IReadOnlyList<FalloutVisibilityPlane> Frustum);
internal sealed record FalloutRoomVisibility(FalloutFormKey? Room, IReadOnlyList<FalloutVisibilityWindow> Windows);
internal sealed record FalloutRoomPortalView(FalloutFormKey? CachedRoom,
    IReadOnlyList<FalloutFormKey?> Traversal, IReadOnlyList<FalloutFormKey> Portals,
    IReadOnlyList<FalloutRoomVisibility> Visibility);

// The registration stream is the returning native source factory stream. This
// owner never reorders records by identity, distance, bounds or a model name.
internal sealed partial class FalloutRoomPortalGraph(FalloutPortalCullingSource source, FalloutFormKey cell)
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<FalloutDynamicPrimitive> _registrations = [];
    private readonly Dictionary<FalloutFormKey, FalloutDynamicPrimitive> _primitives = [];
    private readonly Dictionary<FalloutFormKey, List<FalloutFormKey>> _neighbors = [];
    private readonly List<FalloutFormKey> _external = [];
    private FalloutFormKey?[] _cachedRooms = [];
    private IReadOnlyList<FalloutRoomCamera> _currentCameras = [];
    private bool _closed, _retired, _entered;
    internal string? Failure { get; private set; }
    internal long Generation { get; private set; }
    internal bool ColdNativeTopologyRebuilt { get; private set; }
    internal IReadOnlyList<FalloutDynamicPrimitive> Registrations => _registrations.AsReadOnly();
    internal IReadOnlyList<FalloutRoomPortalView> Views { get; private set; } = [];
    internal FalloutFormKey Cell => cell;
    internal string RegistrationSha256 { get; private set; } = string.Empty;

    internal void Register(FalloutDynamicPrimitive primitive)
    {
        Require(); source.RequireCurrent();
        if (_closed || primitive.Cell != cell || primitive.Source.EngineSha256 != source.EngineSha256 ||
            primitive.Role is not (FalloutDynamicPrimitiveRole.Room or FalloutDynamicPrimitiveRole.Portal) ||
            !_primitives.TryAdd(primitive.Reference, primitive))
            throw new InvalidDataException("Room/portal factory registration is foreign, duplicate or already sealed.");
        _registrations.Add(primitive);
        if (primitive.Role == FalloutDynamicPrimitiveRole.Room) _neighbors.Add(primitive.Reference, []);
    }

    internal void Seal(Func<FalloutFormKey, FalloutFormKey?> resolveActualRoom)
    {
        Require(); source.RequireCurrent();
        if (_closed || _entered) throw new InvalidOperationException("Room/portal graph is already sealed or entered.");
        try
        {
            foreach (var portal in _registrations.Where(row => row.Role == FalloutDynamicPrimitiveRole.Portal))
            {
                if (portal.PortalRooms.Count != 2)
                    throw new InvalidDataException("Portal has lost its two physical source room slots.");
                var rooms = new FalloutFormKey?[2];
                for (var ordinal = 0; ordinal < rooms.Length; ordinal++)
                {
                    if (portal.PortalRooms[ordinal] is not { } original) continue;
                    rooms[ordinal] = resolveActualRoom(original) ??
                        throw new NotSupportedException("Source portal target/enable-parent has no living native room consumer.");
                    if (!_neighbors.ContainsKey(rooms[ordinal]!.Value))
                        throw new InvalidDataException("Portal borrowed a room outside its actual source graph.");
                }
                _resolvedRooms.Add(portal.Reference, Array.AsReadOnly(rooms));
                // Each physical slot contributes its native tail insertion.
                // Repeated slots remain repeated; traversal owns visited state.
                foreach (var room in rooms)
                    if (room is { } key) _neighbors[key].Add(portal.Reference);
                    else _external.Add(portal.Reference);
            }
            foreach (var room in _registrations.Where(row => row.Role == FalloutDynamicPrimitiveRole.Room))
                foreach (var linked in room.LinkedRooms)
                    if (!_neighbors.ContainsKey(linked))
                        throw new NotSupportedException("Linked multibound has no current source room-bound owner.");
            var text = new StringBuilder(source.ContractSha256).Append('|').Append(cell);
            foreach (var row in _registrations)
            {
                text.Append('|').Append(row.Reference).Append(':').Append(row.SourceSha256);
                foreach (var target in row.PortalRooms) text.Append(':').Append(target?.ToString() ?? "external");
                if (row.Role == FalloutDynamicPrimitiveRole.Portal)
                {
                    text.Append(":resolved");
                    foreach (var target in _resolvedRooms[row.Reference])
                        text.Append(':').Append(target?.ToString() ?? "external");
                }
                foreach (var target in row.LinkedRooms) text.Append(':').Append(target);
            }
            RegistrationSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
            _closed = true;
        }
        catch (Exception error) { Failure = error.Message; throw; }
    }
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutFormKey?>> _resolvedRooms = [];

    internal void Observe(IReadOnlyList<FalloutRoomCamera> cameras, IReadOnlyList<FalloutRoomGeometry> rooms,
        IReadOnlyList<FalloutPortalGeometry> portals, float unitsToMetres)
    {
        Require(); source.RequireCurrent();
        if (!_closed || _entered || cameras.Count == 0 || !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new NotSupportedException("Portal graph has no sealed source/current camera observation.");
        _entered = true;
        try
        {
            var roomMap = rooms.ToDictionary(row => row.Reference);
            var portalMap = portals.ToDictionary(row => row.Reference);
            foreach (var sourceRoom in _registrations.Where(row => row.Role == FalloutDynamicPrimitiveRole.Room))
            {
                if (!roomMap.TryGetValue(sourceRoom.Reference, out var current))
                    throw new NotSupportedException("Registered room lost its actual current native bound.");
                current.Bound.Validate();
            }
            foreach (var sourcePortal in _registrations.Where(row => row.Role == FalloutDynamicPrimitiveRole.Portal))
                if (!portalMap.ContainsKey(sourcePortal.Reference))
                    throw new NotSupportedException("Registered portal lost its actual current native rectangle.");
            if (_cachedRooms.Length != cameras.Count)
            {
                if (Views.Count != 0)
                    throw new NotSupportedException("Active room-camera view topology changed without a new render lifetime.");
                ColdNativeTopologyRebuilt = Generation != 0;
                // Flat/XR share gameplay and saves. A different fresh native
                // view topology cannot borrow old view-index caches; each new
                // actual camera selects its source containing room afresh.
                _cachedRooms = new FalloutFormKey?[cameras.Count];
            }
            var views = new List<FalloutRoomPortalView>();
            for (var view = 0; view < cameras.Count; view++)
            {
                var camera = cameras[view];
                if (!FalloutPortalVisibilityMath.Finite(camera.Origin) || !FalloutPortalVisibilityMath.Finite(camera.NearPoint) ||
                    camera.Frustum.Count != 6)
                    throw new InvalidDataException("Current room camera lost its origin/near point/six frustum planes.");
                foreach (var plane in camera.Frustum) plane.Validate();
                bool Contains(FalloutFormKey room)
                {
                    var geometry = roomMap[room];
                    return geometry.Enabled && (geometry.Bound.Contains(camera.NearPoint) ||
                        _primitives[room].LinkedRooms.Any(link => roomMap[link].Enabled && roomMap[link].Bound.Contains(camera.NearPoint)));
                }
                IReadOnlyList<FalloutFormKey?> starts;
                if (_cachedRooms[view] is { } cached && roomMap.ContainsKey(cached) && Contains(cached)) starts = [cached];
                else
                {
                    starts = _registrations.Where(row => row.Role == FalloutDynamicPrimitiveRole.Room && Contains(row.Reference))
                        .Select(row => (FalloutFormKey?)row.Reference).ToArray();
                    if (starts.Count == 0) starts = [null];
                    _cachedRooms[view] = starts[0];
                }
                views.Add(Traverse(camera, starts, portalMap, roomMap, source.CoplanarGameUnits * unitsToMetres));
            }
            Views = views.AsReadOnly(); _currentCameras = cameras.ToArray(); Generation = checked(Generation + 1);
        }
        catch (Exception error) { Failure = error.Message; throw; }
        finally { _entered = false; }
    }

    private FalloutRoomPortalView Traverse(FalloutRoomCamera camera, IReadOnlyList<FalloutFormKey?> starts,
        IReadOnlyDictionary<FalloutFormKey, FalloutPortalGeometry> portals,
        IReadOnlyDictionary<FalloutFormKey, FalloutRoomGeometry> rooms, float coplanar)
    {
        var windows = new Dictionary<FalloutFormKey, List<FalloutVisibilityWindow>>();
        var outside = new List<FalloutVisibilityWindow>();
        List<FalloutVisibilityWindow> Window(FalloutFormKey? room)
        {
            if (room is null) return outside;
            if (!windows.TryGetValue(room.Value, out var result)) windows.Add(room.Value, result = []);
            return result;
        }
        var visited = new HashSet<FalloutFormKey>();
        var orderedPortals = new List<FalloutFormKey>(); var traversal = new List<FalloutFormKey?>();
        foreach (var start in starts)
        {
            var queue = new Queue<FalloutFormKey?>(); queue.Enqueue(start);
            Window(start).Add(new(camera.Frustum));
            while (queue.TryDequeue(out var room))
            {
                traversal.Add(room);
                var incoming = Window(room).ToArray();
                IReadOnlyList<FalloutFormKey> neighbors = room is null ? _external : _neighbors[room.Value];
                foreach (var key in neighbors)
                {
                    if (visited.Contains(key) || !portals[key].Enabled) continue;
                    var portal = portals[key];
                    var passing = incoming.Where(window => window.Clip(portal.Rectangle).Length >= 3).ToArray();
                    if (passing.Length == 0) continue;
                    var slots = _resolvedRooms[key];
                    var next = slots[0] == room ? slots[1] : slots[0];
                    if (next == room || next is { } nextRoom && !rooms[nextRoom].Enabled) continue;
                    var cone = FalloutPortalVisibilityMath.Cone(camera.Origin, portal.Rectangle, coplanar);
                    var destination = Window(next);
                    foreach (var window in passing) destination.Add(window.Intersect(cone));
                    visited.Add(key); orderedPortals.Add(key);
                    if (next != start) queue.Enqueue(next);
                }
            }
        }
        var visible = new List<FalloutRoomVisibility>();
        foreach (var sourceRoom in _registrations.Where(row => row.Role == FalloutDynamicPrimitiveRole.Room))
            if (windows.TryGetValue(sourceRoom.Reference, out var values)) visible.Add(new(sourceRoom.Reference, values.AsReadOnly()));
        if (outside.Count != 0) visible.Add(new(null, outside.AsReadOnly()));
        return new(starts[0], traversal.AsReadOnly(), orderedPortals.AsReadOnly(), visible.AsReadOnly());
    }

    internal bool Sees(FalloutFormKey? room, FalloutVisibilitySphere currentBound)
    {
        Require(); currentBound.Validate();
        if (!_closed || Views.Count == 0) throw new NotSupportedException("Visibility has no genuine current camera consumer.");
        if (room is { } original && !_neighbors.ContainsKey(original))
            throw new NotSupportedException("Source room member has no registered native room consumer.");
        return Views.Any(view => view.Visibility.Any(row => row.Room == room && row.Windows.Any(window => window.Sees(currentBound))));
    }

    internal bool SeesUnassigned(FalloutVisibilitySphere currentBound)
    {
        Require(); currentBound.Validate();
        if (_currentCameras.Count == 0) throw new NotSupportedException("Unassigned geometry has no current native camera frustum.");
        return _currentCameras.Any(camera => new FalloutVisibilityWindow(camera.Frustum).Sees(currentBound));
    }

    internal FalloutRoomPortalSnapshot Capture()
    {
        Require();
        if (!_closed || Views.Count == 0 || _entered) throw new NotSupportedException("Room/portal graph capture lacks current camera publication.");
        return new(source.EngineSha256, source.ContractSha256, cell, RegistrationSha256, Generation,
            Array.AsReadOnly((FalloutFormKey?[])_cachedRooms.Clone()), Failure);
    }
    internal void Restore(FalloutRoomPortalSnapshot saved)
    {
        Require(); saved.Validate();
        if (!_closed || Generation != 0 || Views.Count != 0 || saved.EngineSha256 != source.EngineSha256 ||
            saved.ContractSha256 != source.ContractSha256 || saved.Cell != cell || saved.RegistrationSha256 != RegistrationSha256 ||
            saved.CachedRooms.Any(room => room is { } key && !_neighbors.ContainsKey(key)))
            throw new InvalidDataException("Cold room/portal graph differs from its original registrations/source lifetime.");
        if (saved.Failure is not null)
            throw new NotSupportedException("Cold room/portal graph retains its actual failed camera consumer: " + saved.Failure);
        _cachedRooms = saved.CachedRooms.ToArray();
        Generation = saved.LastNativeGeneration;
        // Render windows and native leases are process-local. A fresh actual
        // camera observation is mandatory before any draw or complete capture.
    }
    internal void Retire()
    {
        if (_retired) return;
        if (Environment.CurrentManagedThreadId != _thread || _entered)
            throw new InvalidOperationException("Portal graph retirement changed its actual caller or entered consumer.");
        _retired = true; Views = []; _cachedRooms = []; _currentCameras = [];
    }
    private void Require()
    {
        if (_retired || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Room/portal graph lost its actual product-thread lifetime.");
        if (Failure is not null) throw new NotSupportedException("Room/portal graph retained a failed consumer: " + Failure);
    }
}
