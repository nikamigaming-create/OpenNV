using Godot;
using Environment = System.Environment;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.SceneGraph;
using V3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.World.Cells;

// Godot adapts actual camera/bounds and publishes the C# graph result to native
// render RIDs. Source Node.Visible, Layers, collision and processing are untouched.
internal sealed partial class RuntimeNativeRoomPortalGraph : Node
{
    private Node3D _root = null!;
    private FalloutPluginStack _records = null!;
    private FalloutRoomPortalGraph _state = null!;
    private Node _memberOwner = null!;
    private Func<IReadOnlyDictionary<FalloutFormKey, Node3D>> _observeMembers = null!;
    private RuntimeNativeSourcePortalPrimitive[] _primitives = [];
    private readonly Dictionary<FalloutFormKey, FalloutRoomMembership> _memberships = [];
    private readonly HashSet<GeometryInstance3D> _renderInstances = [];
    private readonly Dictionary<GeometryInstance3D, bool> _lastRenderPublication = [];
    private Action<Exception> _retainFailure = null!;
    private float _units;
    private int _thread;
    private bool _subscribed, _published, _retired, _drawing;
    internal string? Failure { get; private set; }
    internal FalloutFormKey Cell => _state.Cell;
    internal FalloutRoomPortalGraph State => _state;
    internal object Observation => new
    {
        cell = Cell.ToString(),
        source = _state.RegistrationSha256,
        generation = _state.Generation,
        published = _published,
        retired = _retired,
        coldNativeTopologyRebuilt = _state.ColdNativeTopologyRebuilt,
        primitiveCount = _primitives.Length,
        renderInstances = _renderInstances.Count,
        membershipConsumer = "winning-XMBR",
        automaticRoomAttachment = "unowned",
        sourceOcclusionBooleanComposition = "current-camera-negative-source-halfspaces",
        occlusionPlanes = _occlusionPlanes.Length,
        occlusionGeneration = _occlusion.Generation,
        occlusionModeMutation = "unowned",
        serializedPoseMutation = "unowned",
        pixelParity = "unmeasured",
        views = _state.Views.Select(view => new
        {
            cachedRoom = view.CachedRoom?.ToString(),
            rooms = view.Traversal.Select(room => room?.ToString() ?? "external"),
            portals = view.Portals.Select(portal => portal.ToString())
        }),
        failure = Failure ?? _state.Failure
    };

    internal static RuntimeNativeRoomPortalGraph Attach(Node3D root, FalloutReferenceWorld world,
        FalloutPluginStack records, FalloutCellScene scene, FalloutPortalCullingSource source,
        Node memberOwner, Func<IReadOnlyDictionary<FalloutFormKey, Node3D>> observeMembers,
        float unitsToMetres, Action<Exception> retainFailure, FalloutRoomPortalSnapshot? saved = null)
    {
        ArgumentNullException.ThrowIfNull(retainFailure); ArgumentNullException.ThrowIfNull(observeMembers);
        source.RequireCurrent();
        if (!GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion() || !GodotObject.IsInstanceValid(memberOwner) ||
            memberOwner.IsQueuedForDeletion() || memberOwner.GetParent() != root ||
            !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("Portal graph has no real native CELL/presentation lifetime.");
        if (scene.Cell.Worldspace is not null || (scene.Cell.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0)
            throw new NotSupportedException("Exterior portal/room graph requires its genuine shared WRLD native graph owner.");
        var primitives = root.GetChildren().OfType<RuntimeNativeSourcePortalPrimitive>().ToArray();
        var planes = root.GetChildren().OfType<RuntimeNativeSourceOcclusionPlane>().ToArray();
        if (primitives.Length == 0 && planes.Length == 0)
            throw new InvalidDataException("CELL visibility cannot be fabricated without returning source primitives.");
        var state = new FalloutRoomPortalGraph(source, scene.Cell.FormKey);
        FalloutOcclusionGraph? occlusion = null;
        RuntimeNativeRoomPortalGraph? result = null;
        try
        {
            foreach (var primitive in primitives) state.Register(primitive.Source);
            FalloutFormKey? ResolveRoom(FalloutFormKey requested)
            {
                if (primitives.Any(node => node.Source.Reference == requested && node.Source.Role == FalloutDynamicPrimitiveRole.Room))
                    return requested;
                var parent = world.Get(requested).EnableParent?.Reference;
                return parent is { } key && primitives.Any(node => node.Source.Reference == key &&
                    node.Source.Role == FalloutDynamicPrimitiveRole.Room) ? key : null;
            }
            state.Seal(ResolveRoom);
            if (saved is not null) state.Restore(saved);
            occlusion = ConstructOcclusion(records, scene.Cell.FormKey, source, primitives, planes, saved);
            result = new RuntimeNativeRoomPortalGraph
            {
                Name = "SourceRoomPortalGraph",
                ProcessMode = ProcessModeEnum.Always,
                _root = root,
                _records = records,
                _state = state,
                _memberOwner = memberOwner,
                _observeMembers = observeMembers,
                _primitives = primitives,
                _occlusion = occlusion,
                _occlusionPlanes = planes,
                _units = unitsToMetres,
                _retainFailure = retainFailure,
                _thread = Environment.CurrentManagedThreadId
            };
            root.AddChild(result);
            if (result.GetParent() != root) throw new InvalidDataException("Portal graph did not join its actual CELL root.");
            foreach (var primitive in primitives) primitive.Bind(result);
            foreach (var plane in planes) plane.Bind(result);
            return result;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            try { result?.Free(); } catch (Exception error) { failures.Add(error); }
            try { occlusion?.Retire(); } catch (Exception error) { failures.Add(error); }
            try { state.Retire(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 1)
                throw new AggregateException("CELL visibility construction and retirement both failed.", failures);
            throw;
        }
    }

    public override void _EnterTree()
    {
        RenderingServer.FramePreDraw += OnBeforeDraw; _subscribed = true;
    }
    public override void _ExitTree() => Retire();

    internal void Publish()
    {
        RequireNative();
        if (_published) { RequireCurrent(); return; }
        try { Draw(); _published = true; }
        catch (Exception error) { Retain(error); throw; }
    }
    private void OnBeforeDraw()
    {
        if (!_published || _retired || Failure is not null) return;
        try { Draw(); }
        catch (Exception error) { Retain(error); }
    }
    private void Draw()
    {
        RequireNative();
        if (_drawing) throw new InvalidOperationException("Portal graph native draw consumer reentered.");
        _drawing = true;
        try
        {
            foreach (var primitive in _primitives) primitive.RequireNative();
            var rooms = _primitives.Where(node => node.Source.Role == FalloutDynamicPrimitiveRole.Room).Select(node => node.Room()).ToArray();
            var portals = _primitives.Where(node => node.Source.Role == FalloutDynamicPrimitiveRole.Portal).Select(node => node.Portal()).ToArray();
            var cameras = RuntimeNativeRoomCamera.Observe(_root.GetViewport());
            _state.Observe(cameras, rooms, portals, _units);
            ObserveOcclusion(cameras);
            var actual = new HashSet<GeometryInstance3D>();
            var members = _observeMembers();
            foreach (var (key, node) in members)
            {
                if (node is RuntimeNativeSourcePortalPrimitive or RuntimeNativeSourceOcclusionPlane) continue;
                if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion() || !node.IsInsideTree() || node.GetParent() != _root)
                    throw new NotSupportedException("Room graph source member lost its actual resident native root.");
                if (!_memberships.TryGetValue(key, out var membership))
                    _memberships.Add(key, membership = FalloutRoomMembership.Read(_records, key));
                // Missing XMBR leaves automatic room attachment unknown.
                // Its genuine camera frustum and CELL-global negative plane
                // consumer still run; no outside-room membership is invented.
                var geometry = NodeTraversal.SelfAndDescendants<GeometryInstance3D>(node).ToArray();
                if (membership.Room is not null && geometry.Length == 0 &&
                    NodeTraversal.SelfAndDescendants<VisualInstance3D>(node).Any())
                    throw new NotSupportedException("Source room member has a non-geometry visibility consumer that is not yet owned.");
                foreach (var instance in geometry)
                {
                    if (!GodotObject.IsInstanceValid(instance) || instance.IsQueuedForDeletion() || !instance.IsInsideTree())
                        throw new InvalidOperationException("Room member geometry was retired during its actual draw consumer.");
                    actual.Add(instance);
                    var bounds = instance.GlobalTransform * instance.GetAabb();
                    var sphere = new FalloutVisibilitySphere(N(bounds.GetCenter()), bounds.Size.Length() * .5f);
                    sphere.Validate();
                    var visible = JointVisibility(membership.Room, sphere);
                    var published = visible && instance.IsVisibleInTree();
                    RenderingServer.InstanceSetVisible(instance.GetInstance(), published);
                    _lastRenderPublication[instance] = published;
                    _renderInstances.Add(instance);
                }
            }
            foreach (var outgoing in _renderInstances.Where(instance => !actual.Contains(instance)).ToArray())
            {
                RestoreRenderInstance(outgoing); _renderInstances.Remove(outgoing); _lastRenderPublication.Remove(outgoing);
            }
            // Source appearance/model replacement is observed through the
            // actual presentation's current native descendants each draw.
            foreach (var outgoing in _memberships.Keys.Where(key => !members.ContainsKey(key)).ToArray())
                _memberships.Remove(outgoing);
        }
        finally { _drawing = false; }
    }

    internal void RequirePublished(RuntimeNativeSourcePortalPrimitive primitive)
    {
        if (!_primitives.Contains(primitive) || primitive.GetParent() != _root)
            throw new InvalidDataException("Native room/portal borrowed a foreign camera graph.");
        if (!_published) Publish();
        RequireCurrent();
    }
    internal void RequireCurrent()
    {
        RequireNative();
        if (!_published || _state.Generation == 0 || _state.Views.Count == 0 || _drawing)
            throw new NotSupportedException("Source room/portal has no returned native camera/render consumer.");
        foreach (var primitive in _primitives) primitive.RequireNative();
        foreach (var plane in _occlusionPlanes) plane.RequireNative();
        if (_occlusion.Generation != _state.Generation || _occlusion.Views.Count != _state.Views.Count)
            throw new NotSupportedException("CELL visibility lost its actual correlated source frame.");
    }
    internal FalloutRoomPortalSnapshot Capture()
    {
        RequireCurrent();
        // Settle actual current enable/camera state before recording its cold
        // authority. A stale previous draw must not override later Disable.
        try { Draw(); }
        catch (Exception error) { Retain(error); throw; }
        foreach (var source in _state.Registrations)
        {
            var current = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_records.GetEffective(source.Reference).ReadData())).ToLowerInvariant();
            if (current != source.SourceSha256)
                throw new InvalidDataException("Current portal/room source identity changed before capture.");
        }
        foreach (var membership in _memberships.Values)
            if (FalloutRoomMembership.Read(_records, membership.Reference) != membership)
                throw new InvalidDataException("Current source room membership changed before capture.");
        return _state.Capture() with { Occlusion = CaptureOcclusion() };
    }
    internal bool NativeVisibility(GeometryInstance3D instance)
    {
        RequireCurrent();
        return _lastRenderPublication.TryGetValue(instance, out var visible) ? visible :
            throw new NotSupportedException("Geometry has no actual returned native portal visibility publication.");
    }
    internal void Retire()
    {
        if (_retired) return;
        if (Environment.CurrentManagedThreadId != _thread || _drawing)
            throw new InvalidOperationException("Room graph retirement changed its actual product caller or entered draw.");
        var failures = new List<Exception>();
        if (_subscribed) { RenderingServer.FramePreDraw -= OnBeforeDraw; _subscribed = false; }
        foreach (var instance in _renderInstances)
            try { RestoreRenderInstance(instance); }
            catch (Exception error) { failures.Add(error); }
        _renderInstances.Clear(); _lastRenderPublication.Clear(); _memberships.Clear();
        try { _occlusion.Retire(); } catch (Exception error) { failures.Add(error); }
        try { _state.Retire(); } catch (Exception error) { failures.Add(error); }
        _published = false; _retired = true;
        if (failures.Count != 0)
        {
            var error = new AggregateException("Source room graph failed independent native render retirement.", failures);
            Retain(error); throw error;
        }
    }
    private static void RestoreRenderInstance(GeometryInstance3D instance)
    {
        if (GodotObject.IsInstanceValid(instance) && !instance.IsQueuedForDeletion() && instance.IsInsideTree())
            RenderingServer.InstanceSetVisible(instance.GetInstance(), instance.IsVisibleInTree());
    }
    private void Retain(Exception error)
    {
        Failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
        try { _retainFailure(error); }
        finally { GD.PushError("OPENNV_SOURCE_PORTAL_CULLING_FAILED " + Failure); }
    }
    private void RequireNative()
    {
        if (_retired || Environment.CurrentManagedThreadId != _thread || !IsInsideTree() || GetParent() != _root ||
            !GodotObject.IsInstanceValid(_root) || _root.IsQueuedForDeletion() || !_root.IsInsideTree() ||
            !GodotObject.IsInstanceValid(_memberOwner) || _memberOwner.IsQueuedForDeletion() ||
            !_memberOwner.IsInsideTree() || _memberOwner.GetParent() != _root)
            throw new NotSupportedException("Room/portal draw lost its genuine native CELL/product-thread lifetime.");
        if (Failure is not null) throw new NotSupportedException("Source room/portal draw retains a failure: " + Failure);
    }
    private static V3 N(Vector3 value) => new(value.X, value.Y, value.Z);
}
