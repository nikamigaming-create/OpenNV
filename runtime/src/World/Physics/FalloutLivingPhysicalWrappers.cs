using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutLivingCollisionSnapshot(int Source, ushort Flags, int? ModelMotion, uint? BlendWeight, uint? BlendVelocityWeight, long FlagStores);
internal sealed class FalloutLivingCollisionObject
{
    private readonly FalloutLivingActorModel _model;
    private FalloutLivingRigidBodyWrapper? _wrapper;
    internal FalloutLivingCollisionObject(FalloutLivingActorModel model, FalloutNifCollisionObject source)
    { _model = model; Source = source; _flags = source.Flags; }
    private ushort _flags;
    private int? _modelMotion;
    private uint? _blendWeight, _blendVelocityWeight;
    private long _flagStores;
    internal Guid Instance { get; } = Guid.NewGuid();
    internal FalloutNifCollisionObject Source { get; }
    internal ushort Flags { get { _model.RequireCurrent(); return _flags; } }
    internal int? ModelMotion { get { _model.RequireCurrent(); return _modelMotion; } }
    internal void PublishModelMotion(byte motion)
    {
        _model.RequireCurrent();
        if (Source.IsBlend)
        {
            var body = _wrapper?.CurrentBody ?? throw new InvalidOperationException("Model motion has no current collision/body field owner.");
            (_blendWeight, _blendVelocityWeight) = BlendWeights(motion, body.CollisionFilter);
        }
        _modelMotion = motion;
    }
    internal void SetRagdollBlendFlag(bool enabled)
    {
        _model.RequireCurrent();
        _flags = enabled ? (ushort)(_flags | 0x20) : (ushort)(_flags & ~0x20);
        _flagStores = checked(_flagStores + 1);
    }
    internal FalloutLivingCollisionSnapshot Capture() => new(Source.Block.Index, Flags, _modelMotion, _blendWeight, _blendVelocityWeight, _flagStores);
    internal void Validate(FalloutLivingCollisionSnapshot saved, uint filter)
    {
        _model.RequireCurrent();
        if (saved.Source != Source.Block.Index || saved.FlagStores < 0 || saved.FlagStores == 0 && saved.Flags != Source.Flags ||
            ((saved.Flags ^ Source.Flags) & ~0x20) != 0 ||
            saved.ModelMotion is { } mode && mode is < 1 or > 8 ||
            Source.IsBlend && saved.ModelMotion is { } motion && BlendWeights(motion, filter) != (saved.BlendWeight, saved.BlendVelocityWeight) ||
            (!Source.IsBlend || saved.ModelMotion is null) && (saved.BlendWeight is not null || saved.BlendVelocityWeight is not null))
            throw new InvalidDataException("Cold collision state changed its actual source flags or model-motion fields.");
    }
    internal void Restore(FalloutLivingCollisionSnapshot saved)
    {
        Validate(saved, _wrapper!.CurrentBody!.CollisionFilter);
        _flags = saved.Flags; _modelMotion = saved.ModelMotion; _blendWeight = saved.BlendWeight;
        _blendVelocityWeight = saved.BlendVelocityWeight; _flagStores = saved.FlagStores;
    }
    private (uint? Pose, uint? Velocity) BlendWeights(int motion, uint filter)
    {
        var pose = motion >= 6 && motion != 8 ? 1f : 0f; var velocity = pose;
        if ((filter & 0x7f) is 8 or 0x1d)
        {
            var factors = _model.ReadCollisionBlendFactors((int)(filter >> 8 & 0x1f));
            if (factors.Pose > 1) pose = velocity = 1;
            else { pose = FalloutPhysicalFloat32.Multiply(pose, factors.Pose); velocity = FalloutPhysicalFloat32.Multiply(velocity, factors.Velocity); }
        }
        return (BitConverter.SingleToUInt32Bits(pose), BitConverter.SingleToUInt32Bits(velocity));
    }
    internal FalloutLivingRigidBodyWrapper? CurrentWrapper { get { _model.RequireCurrent(); return _wrapper; } }
    internal void PublishBody(FalloutLivingPhysicalBody body)
    {
        _model.RequireCurrent();
        if (_wrapper is not null || body.Source.Block.Index != Source.Body || !ReferenceEquals(body.CollisionProperty, this))
            throw new InvalidDataException("Living collision publication changed its real body/property owner.");
        _wrapper = body.Wrapper;
    }
    internal void Retire() { _wrapper = null; }
}

internal sealed class FalloutLivingRigidBodyWrapper
{
    private readonly FalloutLivingActorModel _model;
    private FalloutLivingPhysicalBody? _body;
    private IReadOnlyList<FalloutLivingConstraintWrapper> _linked = [];
    internal FalloutLivingRigidBodyWrapper(FalloutLivingActorModel model, FalloutLivingPhysicalBody body)
    { _model = model; _body = body; LinkedConstraints = new CurrentLinks(this); }
    internal Guid Instance { get; } = Guid.NewGuid();
    internal FalloutLivingPhysicalBody? CurrentBody { get { _model.RequireCurrent(); return _body; } }
    internal IReadOnlyList<IFalloutPenetrationConstraint?> LinkedConstraints { get; }
    internal void BindLinks(FalloutLivingPhysicalConstraint[] constraints) =>
        _linked = Array.AsReadOnly(constraints.Select(value => value.Wrapper).ToArray());
    internal void Retire() { _body = null; }

    private sealed class CurrentLinks(FalloutLivingRigidBodyWrapper owner) : IReadOnlyList<IFalloutPenetrationConstraint?>
    {
        public int Count { get { owner._model.RequireCurrent(); return owner._linked.Count; } }
        public IFalloutPenetrationConstraint? this[int index] => owner._linked[index].CurrentConstraint;
        public IEnumerator<IFalloutPenetrationConstraint?> GetEnumerator()
        {
            owner._model.RequireCurrent();
            foreach (var wrapper in owner._linked) yield return wrapper.CurrentConstraint;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

internal sealed record FalloutLivingConstraintData(int Type, FalloutLivingJointParameters? Joint = null, FalloutLivingConstraintData? Wrapped = null);

internal sealed class FalloutLivingConstraintWrapper
{
    private readonly FalloutLivingActorModel _model;
    private FalloutLivingPhysicalConstraint? _constraint;
    private FalloutLivingConstraintData? _data;
    internal FalloutLivingConstraintWrapper(FalloutLivingActorModel model, FalloutLivingPhysicalConstraint constraint, int type, FalloutLivingJointParameters parameters)
    {
        _model = model; _constraint = constraint;
        var core = new FalloutLivingConstraintData(checked((int)parameters.Type), parameters);
        _data = type == 13 ? new(type, null, core) : core;
    }
    internal Guid Instance { get; } = Guid.NewGuid();
    internal FalloutLivingPhysicalConstraint? CurrentConstraint { get { _model.RequireCurrent(); return _constraint; } }
    internal FalloutLivingConstraintData? CurrentData { get { _model.RequireCurrent(); return _data; } }
    internal void Retire() { _constraint = null; _data = null; }
}
