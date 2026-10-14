using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Physics;

internal sealed class FalloutEarlyControllerPhantom
{
    private readonly FalloutEarlyControllerShapeLease _shapes;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly List<(uint Key, object Value, FalloutCombatActorIdentity? Actor)> _properties = [];
    private readonly HashSet<FalloutEarlyControllerProxy> _proxies = [];
    private readonly List<ulong> _nativeShapeIds = [];
    private readonly Rid _native;
    private FalloutCharacterControllerProxyTransform _transform = new(new(
        FalloutPhysicalVector3.FromValues(1, 0, 0), FalloutPhysicalVector3.FromValues(0, 1, 0),
        FalloutPhysicalVector3.FromValues(0, 0, 1)), default);
    private uint? _translationW = 0;
    private uint _filter;
    private bool _retired, _initialized;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal ulong NativeId => _native.Id;
    internal Rid NativeResource { get { RequireCurrent(); return _native; } }
    internal uint Filter { get { RequireCurrent(); return _filter; } }
    internal string? RequestedWorld => null;
    internal string? CurrentWorld { get { RequireCurrent(); return null; } }
    internal FalloutCharacterControllerProxyTransform Transform { get { RequireCurrent(); return _transform; } }

    internal FalloutEarlyControllerPhantom(FalloutEarlyControllerShapeLease shapes, uint filter, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(shapes); ArgumentNullException.ThrowIfNull(requireCurrent);
        _shapes = shapes; _filter = filter; requireCurrent();
        _native = PhysicsServer3D.AreaCreate();
        if (!_native.IsValid) throw new InvalidOperationException("Actual early phantom native allocation failed.");
        // Retain a successfully allocated resource even when the following
        // configuration/guard fails; its caller's failed prefix owns retirement.
    }
    internal void Initialize(Action requireCurrent)
    {
        requireCurrent();
        foreach (var shape in _shapes.Slot(1))
        {
            PhysicsServer3D.AreaAddShape(_native, shape.Shape.GetRid(), shape.Transform);
            _nativeShapeIds.Add(shape.NativeId);
            requireCurrent();
        }
        PublishFilter(_filter); PhysicsServer3D.AreaSetTransform(_native, ToNative(_transform));
        _initialized = true;
        requireCurrent(); RequireCurrent();
        if (PhysicsServer3D.AreaGetShapeCount(_native) != _shapes.Slot(1).Count)
            throw new InvalidOperationException("Actual early phantom lost its selected source shapes.");
    }
    internal void RetainProxy(FalloutEarlyControllerProxy proxy)
    { RequireCurrent(); if (!_proxies.Add(proxy)) throw new InvalidOperationException("Actual proxy retained its phantom twice."); }
    internal void ReleaseProxy(FalloutEarlyControllerProxy proxy)
    { RequireThread(); if (!_proxies.Remove(proxy)) throw new InvalidOperationException("Actual proxy released a foreign phantom lease."); }
    internal bool AddPropertyIfAbsent(uint key, object owner, FalloutCombatActorIdentity? actor = null)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(owner);
        if (_properties.Any(value => value.Key == key)) return false;
        _properties.Add((key, owner, actor)); return true;
    }
    internal object? Property(uint key)
    { RequireCurrent(); return _properties.FirstOrDefault(value => value.Key == key).Value; }
    internal void RemoveProxyProperty(FalloutEarlyControllerProxy proxy)
    {
        RequireThread(); var index = _properties.FindIndex(value => value.Key == FalloutEarlyControllerSource.ProxyPropertyKey);
        if (index >= 0)
        {
            if (!ReferenceEquals(_properties[index].Value, proxy))
                throw new InvalidOperationException("Proxy retirement encountered an unowned phantom property replacement.");
            _properties.RemoveAt(index);
        }
    }
    internal void StoreTranslation(FalloutPhysicalVector3 xyz, uint? fourthWord)
    {
        RequireCurrent();
        if (!Finite(xyz)) throw new NotSupportedException("source-phantom-exceptional-translation-input-unbound");
        _transform = _transform with { Translation = xyz }; _translationW = fourthWord;
        // The actual current world is null. The source translation words store
        // before its world predicate, so no AABB/broadphase work is invented.
        PhysicsServer3D.AreaSetTransform(_native, ToNative(_transform)); RequireCurrent();
    }
    internal void RebindTransform(FalloutEarlyControllerPhantomSnapshot saved)
    {
        RequireCurrent(); _transform = saved.Transform; _translationW = saved.TranslationW;
        PhysicsServer3D.AreaSetTransform(_native, ToNative(_transform)); RequireCurrent();
    }
    internal void SetPlayerGroup()
    { RequireCurrent(); PublishFilter(((uint)FalloutEarlyControllerSource.PlayerCollisionGroup << 16) | (_filter & 0xffff)); }
    private void PublishFilter(uint filter)
    {
        var layer = filter & 0x7f;
        if (layer >= 32) throw new NotSupportedException("source-controller-native-layer-projection-unbound");
        _filter = filter;
        PhysicsServer3D.AreaSetCollisionLayer(_native, 1u << (int)layer);
        PhysicsServer3D.AreaSetCollisionMask(_native, uint.MaxValue);
        // This is a detached resource. Native bit layers do not qualify source
        // group-pair filtering, contact/solver order or world admission.
    }
    internal void RequireCurrent()
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this); _ = _shapes.Slot(1);
        if (!_native.IsValid || PhysicsServer3D.AreaGetSpace(_native).IsValid ||
            PhysicsServer3D.AreaGetTransform(_native) != ToNative(_transform))
            throw new InvalidOperationException("Actual detached phantom resource/world/transform changed outside its source owner.");
        if (_initialized && (PhysicsServer3D.AreaGetCollisionLayer(_native) != 1u << (int)(_filter & 0x7f) ||
            PhysicsServer3D.AreaGetCollisionMask(_native) != uint.MaxValue ||
            PhysicsServer3D.AreaGetShapeCount(_native) != _shapes.Slot(1).Count))
            throw new InvalidOperationException("Actual detached phantom native shape/filter association changed outside its source owner.");
    }
    internal FalloutEarlyControllerPhantomSnapshot Capture()
    {
        RequireThread();
        return new(Identity, NativeId, _filter, _transform, _translationW, null, null,
            _properties.Select(value => new FalloutControllerPropertySnapshot(value.Key,
                value.Value is FalloutEarlyControllerProxy ? "proxy" : "Actor", (value.Value as FalloutEarlyControllerProxy)?.Identity,
                value.Actor, value.Actor is null ? null : value.Value.GetType().FullName)).ToArray(), _nativeShapeIds.ToArray(), _retired);
    }
    internal void Retire()
    {
        if (_retired) return; RequireThread();
        if (_proxies.Count != 0) throw new InvalidOperationException("Actual phantom must outlive all retained proxies.");
        if (PhysicsServer3D.AreaGetSpace(_native).IsValid)
            throw new InvalidOperationException("Unowned phantom world admission cannot be hidden during retirement.");
        PhysicsServer3D.FreeRid(_native); _properties.Clear(); _retired = true;
    }
    private Transform3D ToNative(FalloutCharacterControllerProxyTransform source)
    {
        var rotation = source.Rotation;
        var basis = GamebryoCoordinate.ConvertBasis([
            rotation.Column0.XValue, rotation.Column1.XValue, rotation.Column2.XValue,
            rotation.Column0.YValue, rotation.Column1.YValue, rotation.Column2.YValue,
            rotation.Column0.ZValue, rotation.Column1.ZValue, rotation.Column2.ZValue], 1, "early phantom source pose");
        var scale = _shapes.PhysicalToNative;
        return new(basis, new(source.Translation.XValue * scale, source.Translation.ZValue * scale, -source.Translation.YValue * scale));
    }
    private void RequireThread()
    { if (_thread != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("Actual phantom escaped its source physics thread."); }
    private static bool Finite(FalloutPhysicalVector3 value) => float.IsFinite(value.XValue) && float.IsFinite(value.YValue) && float.IsFinite(value.ZValue);
}
