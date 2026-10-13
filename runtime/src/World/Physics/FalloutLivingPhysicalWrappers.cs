using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed class FalloutLivingCollisionObject
{
    private readonly FalloutLivingActorModel _model;
    private FalloutLivingRigidBodyWrapper? _wrapper;
    internal FalloutLivingCollisionObject(FalloutLivingActorModel model, FalloutNifCollisionObject source)
    { _model = model; Source = source; }
    internal Guid Instance { get; } = Guid.NewGuid();
    internal FalloutNifCollisionObject Source { get; }
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

internal sealed record FalloutLivingConstraintData(int Type);

internal sealed class FalloutLivingConstraintWrapper
{
    private readonly FalloutLivingActorModel _model;
    private FalloutLivingPhysicalConstraint? _constraint;
    private FalloutLivingConstraintData? _data;
    internal FalloutLivingConstraintWrapper(FalloutLivingActorModel model, FalloutLivingPhysicalConstraint constraint, int type)
    { _model = model; _constraint = constraint; _data = new(type); }
    internal Guid Instance { get; } = Guid.NewGuid();
    internal FalloutLivingPhysicalConstraint? CurrentConstraint { get { _model.RequireCurrent(); return _constraint; } }
    internal FalloutLivingConstraintData? CurrentData { get { _model.RequireCurrent(); return _data; } }
    internal void Retire() { _constraint = null; _data = null; }
}
