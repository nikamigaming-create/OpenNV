using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Physics;

internal sealed class FalloutEarlyControllerProxy
{
    private readonly FalloutEarlyControllerPhantom _phantom;
    private readonly FalloutControllerDynamicArray<object> _first = new("first"), _third = new("third"), _fourth = new("fourth");
    private readonly FalloutControllerDynamicArray<FalloutControllerContactState> _second = new("contact-listeners");
    private readonly uint _maximumSlopeCosine;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private FalloutPhysicalVector4 _up, _velocity;
    private bool _retired, _retained;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal FalloutControllerDynamicArray<object> FirstArray { get { RequireCurrent(); return _first; } }
    internal FalloutCharacterControllerProxyTransform Transform { get { RequireCurrent(); return _phantom.Transform; } }
    internal FalloutPhysicalVector4 Velocity { get { RequireCurrent(); return _velocity; } }
    internal FalloutEarlyControllerProxy(FalloutEarlyControllerPhantom phantom, FalloutEarlyControllerDescriptorSnapshot descriptor)
    {
        _phantom = phantom; phantom.RequireCurrent();
        _maximumSlopeCosine = FalloutControllerContactState.MaximumSlopeCosine(descriptor.MaximumSlopeBits);
        _up = Normalize(descriptor.Up); _velocity = default;
        // The actual selected derived proxy independently replaces base up.
        _up = new(FalloutPhysicalVector3.FromValues(0, 0, 1), 0);
    }
    internal FalloutEarlyControllerProxy(FalloutEarlyControllerPhantom phantom, FalloutEarlyControllerProxySnapshot saved)
    { _phantom = phantom; phantom.RequireCurrent(); _maximumSlopeCosine = saved.MaximumSlopeCosineBits; _up = saved.Up; _velocity = saved.Velocity; }
    internal void Initialize()
    {
        _phantom.RetainProxy(this); _retained = true;
        _phantom.AddPropertyIfAbsent(FalloutEarlyControllerSource.ProxyPropertyKey, this);
    }
    internal void AddContactListener(FalloutControllerContactState contact)
    { RequireCurrent(); _second.Append(contact); }
    internal FalloutControllerContactState ReadContactListener(int index)
    { RequireCurrent(); return _second.Read(index); }
    internal void StoreVelocity(FalloutPhysicalVector4 value) { RequireCurrent(); _velocity = value; }
    internal void ClearFirstArrayCount() { RequireCurrent(); _first.ClearCount(); }
    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Actual character proxy escaped its source thread.");
        _phantom.RequireCurrent();
    }
    internal FalloutEarlyControllerProxySnapshot Capture() => new(Identity, _phantom.Identity, _up, _velocity, _maximumSlopeCosine,
        new[] { _first.Capture(), _second.Capture(), _third.Capture(), _fourth.Capture() }, _second.Count, _retired);
    internal void Retire()
    {
        if (_retired) return;
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Actual proxy retirement escaped its source thread.");
        if (_third.Count != 0 || _fourth.Count != 0)
            throw new NotSupportedException("source-proxy-listener-removal-callback-owner-unbound");
        if (_retained) { _phantom.RemoveProxyProperty(this); _phantom.ReleaseProxy(this); _retained = false; }
        _first.Retire(); _second.Retire(); _third.Retire(); _fourth.Retire(); _retired = true;
    }
    private static FalloutPhysicalVector4 Normalize(FalloutPhysicalVector4 value)
    {
        var xyz = value.Position;
        var squared = FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Add(FalloutPhysicalFloat32.Multiply(xyz.YValue, xyz.YValue),
            FalloutPhysicalFloat32.Multiply(xyz.XValue, xyz.XValue)), FalloutPhysicalFloat32.Multiply(xyz.ZValue, xyz.ZValue));
        if (!float.IsFinite(squared) || squared <= 0) throw new NotSupportedException("source-proxy-exceptional-up-normalization-unbound");
        var magnitude = (float)Math.Sqrt((double)squared); var inverse = FalloutPhysicalFloat32.Divide(1, magnitude);
        return new(FalloutPhysicalVector3.FromValues(FalloutPhysicalFloat32.Multiply(xyz.XValue, inverse),
            FalloutPhysicalFloat32.Multiply(xyz.YValue, inverse), FalloutPhysicalFloat32.Multiply(xyz.ZValue, inverse)),
            BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Multiply(BitConverter.UInt32BitsToSingle(value.W), inverse)));
    }
}
