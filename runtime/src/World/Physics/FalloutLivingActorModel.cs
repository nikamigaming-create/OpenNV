using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

// Source declarations enter once. The retained wrappers, bodies, constraints
// and memberships thereafter describe the living physical objects, rather than
// reconstructing current state from NIF arrays or Godot presentation nodes.
internal sealed partial class FalloutLivingActorModel : IFalloutPenetrationModel, IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IFalloutLivingPhysicsBackend _backend;
    private readonly Dictionary<int, FalloutLivingPhysicalBody> _bodies = [];
    private readonly Dictionary<int, FalloutLivingPhysicalConstraint> _constraints = [];
    private readonly List<FalloutLivingPhysicalBody> _bodyOrder = [];
    private readonly List<FalloutLivingPhysicalConstraint> _constraintOrder = [];
    private readonly IReadOnlyCollection<FalloutLivingPhysicalBody> _readBodies;
    private readonly IReadOnlyCollection<FalloutLivingPhysicalConstraint> _readConstraints;
    private readonly Dictionary<int, FalloutLivingPhysicalScene> _scene = [];
    private IReadOnlyList<IFalloutPenetrationScene?> _roots = [];
    private bool _retired;
    private FalloutLivingRagdollBinding? _ragdollBinding;
    private FalloutLivingModelPlacement? _placement;
    private FalloutPhysicalConstructorInputs? _inputs;
    private FalloutLivingRagdollDrive? _ragdollDrive;

    private FalloutLivingActorModel(string key, FalloutActorSourceModel source, IFalloutLivingPhysicsBackend backend)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Living model requires its actual Actor/model binding.", nameof(key));
        Key = key; Source = source; _backend = backend;
        _readBodies = _bodyOrder.AsReadOnly(); _readConstraints = _constraintOrder.AsReadOnly();
    }

    public string Key { get; }
    public IReadOnlyList<IFalloutPenetrationScene?> Roots { get { RequireCurrent(); return _roots; } }
    internal FalloutActorSourceModel Source { get; }
    internal Guid Generation { get; } = Guid.NewGuid();
    internal IReadOnlyCollection<FalloutLivingPhysicalBody> Bodies { get { RequireCurrent(); return _readBodies; } }
    internal IReadOnlyCollection<FalloutLivingPhysicalConstraint> Constraints { get { RequireCurrent(); return _readConstraints; } }
    internal string BackendBoundary => _backend.Boundary;
    internal (float Pose, float Velocity) ReadCollisionBlendFactors(int group) =>
        (_inputs ?? throw new InvalidOperationException("Living model has no actual physical source fields.")).ReadCollisionBlendFactors(group);
    internal FalloutLivingRagdollDriveSnapshot? CaptureRagdollDrive() => _ragdollDrive?.Capture();
    internal void InitializeRagdollDrive(World.Cells.FalloutReferenceModelDataInvocation load,
        Func<string, FalloutIniValue> readSetting, Action<World.Cells.FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        if (_ragdollDrive is not null || _physicalSkeleton is not { } instance || instance.Load != load)
            throw new InvalidOperationException("Ragdoll drive requires its same returned physical instance and cannot replay.");
        _ragdollDrive = new(this, instance, instance.Descriptor.Bones.Select(bone => _bodies[bone.Body]).ToArray(), load);
        _ragdollDrive.Initialize(readSetting, requireActual);
        _ragdollController?.PublishDrive(instance, _ragdollDrive.Capture());
    }

    internal FalloutLivingRagdollBindingSnapshot BindRagdollSkeleton(FalloutActorLogicalScene graph,
        World.Cells.FalloutReferenceModelDataInvocation load, Action<World.Cells.FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        if (_ragdollBinding is not null) throw new InvalidOperationException("Living model already owns an entered ragdoll skeleton binding.");
        _ragdollBinding = new(this, graph, load, requireActual);
        _ragdollBinding.Bind(requireActual);
        return _ragdollBinding.Capture();
    }
    internal FalloutLivingRagdollBindingSnapshot? CaptureRagdollBinding() => _ragdollBinding?.Capture();

    internal static FalloutLivingActorModel Construct(string key, FalloutActorSourceModel source,
        FalloutPhysicalConstructorInputs inputs, IFalloutLivingPhysicsBackend backend,
        Func<FalloutNifPhysicalSceneObject, FalloutLivingBodyConstruction> currentBody, FalloutLivingModelPlacement? placement = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(backend); ArgumentNullException.ThrowIfNull(currentBody);
        if (source.Parts.Any(part => part.Physical.Bodies.Count != 0 || part.Physical.Constraints.Count != 0 ||
            part.Physical.Scene.Any(scene => scene.Collision is { Body: not -1 })))
            throw new NotSupportedException("Living equipment/appearance physical graph attachment and admission are unowned.");
        backend.RequireCurrent();
        var result = new FalloutLivingActorModel(key, source, backend) { _placement = placement, _inputs = inputs };
        try
        {
            var physical = source.Skeleton;
            foreach (var declaration in physical.Scene)
                result._scene.Add(declaration.Declaration.Block.Index,
                    new(result, declaration, $"{key}/scene/{declaration.Declaration.Block.Index}"));
            foreach (var declaration in physical.Scene)
            {
                var scene = result._scene[declaration.Declaration.Block.Index];
                scene.BindChildren(declaration.Children.Select(index => index == -1 ? null :
                    (IFalloutPenetrationScene)result._scene[index]).ToArray());
                if (declaration.Collision is not { Body: not -1 } collision) continue;
                if (physical.FindAttachedBody(collision.Body) is not { } body)
                    throw new NotSupportedException("Living source scene phantom construction is unowned.");
                if (result._bodies.ContainsKey(body.Block.Index))
                    throw new InvalidDataException("Living physical body has more than one scene property producer.");
                var construction = currentBody(declaration) ?? throw new InvalidDataException("Actual living body publication is absent.");
                if (string.IsNullOrWhiteSpace(construction.Name))
                    throw new InvalidDataException("Living physical body has no current constructor name.");
                construction = construction with
                { PrimitiveShape = FalloutLivingPrimitiveShapeParameters.Construct(physical.Source, body.Shape, construction.ShapeScaleBits) };
                var projected = inputs.ProjectRigidBody(body);
                var native = backend.CreateBody(physical.Source, collision, projected, construction);
                try
                {
                    native.RequireCurrent();
                    if (!native.HasPhysicalObject || native.IsWorldAttached)
                        throw new InvalidDataException("Living factory did not construct a detached actual physical body.");
                    var current = new FalloutLivingPhysicalBody(result, body, collision, projected,
                        construction, native, $"{key}/body/{body.Block.Index}", scene.CollisionObject!);
                    result._bodies.Add(body.Block.Index, current); result._bodyOrder.Add(current); scene.PublishBody(current);
                    if (projected.SavedMotion is { } saved) current.PublishMotion(saved);
                }
                catch { native.Dispose(); throw; }
            }
            foreach (var declaration in physical.Constraints)
            {
                if (!result._bodies.TryGetValue(declaration.Header.EntityA, out var first) ||
                    !result._bodies.TryGetValue(declaration.Header.EntityB, out var second))
                    throw new InvalidDataException("Living structural constraint leaves its actual body assembly.");
                var parameters = FalloutLivingJointParameters.Construct(declaration);
                var native = backend.CreateConstraint(declaration, parameters, first.Backend, second.Backend);
                try
                {
                    native.RequireCurrent();
                    if (!native.HasPhysicalObject || native.IsWorldAttached)
                        throw new InvalidDataException("Living factory did not construct an unattached actual physical constraint.");
                    var current = new FalloutLivingPhysicalConstraint(result, declaration, parameters, first, second, native, $"{key}/constraint/{declaration.Header.Block.Index}");
                    result._constraints.Add(declaration.Header.Block.Index, current); result._constraintOrder.Add(current);
                }
                catch { native.Dispose(); throw; }
            }
            // Stream linking pushes each admitted wrapper at the list head.
            // Null/unadmitted source slots never enter the constructed list.
            var linked = physical.Registrations.Where(row => row.Constraint is not null)
                .GroupBy(row => row.Body.Block.Index);
            foreach (var rows in linked)
                result._bodies[rows.Key].BindLinkedConstraints(rows.Reverse().Select(row =>
                    result._constraints[row.Constraint!.Header.Block.Index]).ToArray());
            result._roots = Array.AsReadOnly(physical.Roots.Select(index => index == -1 ? null :
                (IFalloutPenetrationScene)result._scene[index]).ToArray());
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal void RequireCurrent()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Living physical model was used outside its owning thread.");
        ObjectDisposedException.ThrowIf(_retired, this);
        _backend.RequireCurrent();
    }

    internal FalloutLivingModelSnapshot Capture()
    {
        RequireCurrent();
        if (_bodyOrder.Any(body => body.PendingWorld is not null))
            throw new NotSupportedException("Living model pending physical operations are not a completed save boundary.");
        return new(Key, Source.Skeleton.Source.Sha256, _bodyOrder.Select(body => body.Capture()).ToArray(), _ragdollBinding?.Capture(), _placement, _ragdollTemplate, _physicalSkeleton, _ragdollDrive?.Capture(), _animationSkeleton, _ragdollMapping, _ragdollArms, _ragdollController?.Capture(), CaptureRagdollBodyParts(), _footIk, _deathInitialization);
    }

    internal void RestoreDetached(FalloutLivingModelSnapshot saved)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.RagdollBinding is not null || saved.RagdollTemplate is not null || saved.PhysicalSkeleton is not null || saved.Drive is not null || saved.AnimationSkeleton is not null || saved.Mapping is not null || saved.Arms is not null || saved.Controller is not null || saved.BodyPartPreparation is not null || saved.FootIk is not null || saved.DeathInitialization is not null)
            throw new NotSupportedException("source-cold-living-ragdoll-binding-fresh-body-joint-resource-reconstruction-unowned");
        if (saved.Key != Key || saved.SourceSha256 != Source.Skeleton.Source.Sha256 || saved.Placement != _placement ||
            saved.Bodies.Count != _bodies.Count || saved.Bodies.Select(row => row.Key).Distinct().Count() != saved.Bodies.Count ||
            !_bodyOrder.Select(body => body.Key).SequenceEqual(saved.Bodies.Select(row => row.Key)) ||
            _bodyOrder.Any(body => body.CurrentWorld is not null || body.PendingWorld is not null))
            throw new InvalidDataException("Cold physical model source/order or detached boundary changed.");
        foreach (var (body, state) in _bodyOrder.Zip(saved.Bodies)) body.ValidateRestore(state);
        foreach (var (body, state) in _bodyOrder.Zip(saved.Bodies)) body.Restore(state);
    }

    public void Dispose()
    {
        if (_retired) return;
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Living physical model retirement requires its owning thread.");
        if (_ragdollBodyPartsEntered)
        { ++_ragdollBodyPartsReentry; throw new InvalidOperationException("Living model cannot retire inside its actual body-part constructor child."); }
        if (_footIkEntered)
        { ++_footIkReentry; throw new InvalidOperationException("Living model cannot retire inside its actual foot-IK constructor child."); }
        foreach (var world in _bodyOrder.Select(body => body.CurrentWorld).Where(world => world is not null).Distinct())
            world!.RetireModel(this);
        if (_bodyOrder.Any(body => body.PendingWorld is not null) || _constraintOrder.Any(value => value.PendingWorld is not null))
            throw new NotSupportedException("Living physical queued-object retirement is unowned.");
        // Underlying joints retain endpoint bodies. Release all joints first.
        foreach (var solver in _footSolvers) solver.Retire();
        _ragdollController?.Retire();
        _ragdollDrive?.Retire();
        foreach (var constraint in _constraintOrder) { constraint.Backend.Dispose(); constraint.Wrapper.Retire(); }
        foreach (var body in _bodyOrder) { body.Backend.Dispose(); body.Wrapper.Retire(); body.CollisionProperty.Retire(); }
        _retired = true;
    }
}

internal sealed class FalloutLivingPhysicalScene : IFalloutPenetrationScene
{
    private readonly FalloutLivingActorModel _owner;
    private IReadOnlyList<IFalloutPenetrationScene?> _children = [];
    internal FalloutLivingPhysicalScene(FalloutLivingActorModel owner, FalloutNifPhysicalSceneObject source, string key)
    {
        _owner = owner; Source = source; Key = key;
        if (source.Collision is { } collision) CollisionObject = new(owner, collision);
    }
    internal FalloutNifPhysicalSceneObject Source { get; }
    public string Key { get; }
    public IReadOnlyList<IFalloutPenetrationScene?> Children { get { _owner.RequireCurrent(); return _children; } }
    internal FalloutLivingCollisionObject? CollisionObject { get; }
    public IFalloutPenetrationBody? Body { get { _owner.RequireCurrent(); return CollisionObject?.CurrentWrapper?.CurrentBody; } }
    internal void BindChildren(IFalloutPenetrationScene?[] children) => _children = Array.AsReadOnly(children);
    internal void PublishBody(FalloutLivingPhysicalBody body) => (CollisionObject ??
        throw new InvalidDataException("Living scene has no actual collision object.")).PublishBody(body);
}
