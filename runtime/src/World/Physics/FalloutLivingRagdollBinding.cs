using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollSourceBone(FalloutNifSceneKey Scene, int Body, int? ParentBody, string Name);
internal sealed record FalloutRagdollSourceJoint(int Constraint, int FirstBody, int SecondBody);
internal sealed record FalloutRagdollSourceSkeleton(string Source, Guid Model, FalloutNifSceneKey CollisionRoot,
    int RootCollision, int RootBody, IReadOnlyList<FalloutRagdollSourceBone> Bones,
    IReadOnlyList<FalloutRagdollSourceJoint> Joints)
{
    internal static FalloutRagdollSourceSkeleton Read(FalloutActorSourceModel source, FalloutActorLogicalScene graph)
    {
        if (!ReferenceEquals(source.LogicalScene, graph) || source.Factory != graph.Factory)
            throw new InvalidDataException("Ragdoll skeleton changed its actual current source model/graph.");
        var current = graph.Objects.ToDictionary(node => node.Key);
        var declared = source.Skeleton.Scene.Where(node => node.Collision is not null).ToArray();
        if (declared.Length == 0) throw new NotSupportedException("source-ragdoll-first-collision-object-absent");
        if (source.Parts.Any(part => part.Physical.Scene.Any(node => node.Collision is not null)))
            throw new NotSupportedException("source-ragdoll-attached-part-physical-hierarchy-linking-unowned");
        var root = declared[0]; var collision = root.Collision!;
        if (source.Skeleton.FindAttachedBody(collision.Body) is null)
            throw new NotSupportedException("source-ragdoll-first-collision-non-rigid-object-unowned");
        var bodiesByScene = new Dictionary<FalloutNifSceneKey, int>();
        var bodies = new HashSet<int>(); var bones = new List<FalloutRagdollSourceBone>();
        foreach (var node in source.Skeleton.Scene)
        {
            if (node.Collision is not { Body: >= 0 } child) continue;
            if (source.Skeleton.FindAttachedBody(child.Body) is null)
                throw new NotSupportedException("source-ragdoll-current-shape-phantom-skeleton-linking-unowned");
            var key = new FalloutNifSceneKey(0, node.Declaration.Block.Index);
            if (!current.TryGetValue(key, out var actual) || !bodies.Add(child.Body))
                throw new InvalidDataException("Ragdoll body has no unique actual current scene producer.");
            int? parentBody = null;
            for (var parent = actual.Parent; parent is not null; parent = parent.Parent)
                if (bodiesByScene.TryGetValue(parent.Key, out var found)) { parentBody = found; break; }
            bodiesByScene.Add(key, child.Body);
            var name = graph.ReadControllerBodyName(0, child.Body);
            if (name != actual.Name) throw new InvalidDataException("Ragdoll body name lost its actual model preparation store.");
            bones.Add(new(key, child.Body, parentBody, name));
        }
        var joints = source.Skeleton.Constraints.Select(joint =>
        {
            if (!bodies.Contains(joint.Header.EntityA) || !bodies.Contains(joint.Header.EntityB))
                throw new InvalidDataException("Ragdoll constraint leaves the current body hierarchy.");
            return new FalloutRagdollSourceJoint(joint.Header.Block.Index, joint.Header.EntityA, joint.Header.EntityB);
        }).ToArray();
        if (bones.Count != source.Skeleton.Bodies.Count)
            throw new InvalidDataException("Ragdoll hierarchy omitted an imported source rigid body.");
        return new(source.Skeleton.Source.Sha256, source.Factory, new(0, root.Declaration.Block.Index),
            collision.Block.Index, collision.Body, bones.AsReadOnly(), Array.AsReadOnly(joints));
    }
}

internal sealed record FalloutLivingRagdollBodyBinding(int Body, string CurrentBody, ulong NativeBody,
    FalloutNifSceneKey Scene, int? ParentBody, string Name);
internal sealed record FalloutLivingRagdollJointBinding(int Constraint, string CurrentJoint, ulong NativeJoint,
    string FirstBody, string SecondBody);
internal sealed record FalloutLivingRagdollBindingSnapshot(FalloutReferenceModelDataInvocation Load, Guid Model,
    FalloutRagdollSourceSkeleton Skeleton, IReadOnlyList<FalloutLivingRagdollBodyBinding> Bodies,
    IReadOnlyList<FalloutLivingRagdollJointBinding> Joints, bool Returned, string? Failure)
{
    internal void Validate()
    {
        if (Load is null || Load.Call == Guid.Empty || Model == Guid.Empty || Skeleton is null || Skeleton.Model == Guid.Empty ||
            Skeleton.Source is not { Length: 64 } || !Skeleton.Source.All(Uri.IsHexDigit) ||
            Skeleton.Bones is not { Count: > 0 } || Skeleton.Joints is null || Bodies is null || Joints is null ||
            Skeleton.RootBody != Skeleton.Bones[0].Body || Bodies.Count > Skeleton.Bones.Count || Joints.Count > Skeleton.Joints.Count ||
            Returned && (Failure is not null || Bodies.Count != Skeleton.Bones.Count || Joints.Count != Skeleton.Joints.Count) ||
            !Returned && string.IsNullOrWhiteSpace(Failure) ||
            Bodies.Select(body => body.Body).Distinct().Count() != Bodies.Count ||
            Bodies.Select(body => body.NativeBody).Distinct().Count() != Bodies.Count ||
            Joints.Select(joint => joint.NativeJoint).Distinct().Count() != Joints.Count)
            throw new InvalidDataException("Ragdoll binding lost its actual model, native resources or failed prefix.");
        foreach (var (body, source) in Bodies.Zip(Skeleton.Bones))
            if (body.Body != source.Body || body.Scene != source.Scene || body.ParentBody != source.ParentBody ||
                body.Name != source.Name || body.NativeBody == 0 || string.IsNullOrWhiteSpace(body.CurrentBody))
                throw new InvalidDataException("Ragdoll binding changed its actual source/body/parent/name join.");
        var bodies = Bodies.ToDictionary(body => body.Body);
        foreach (var (joint, source) in Joints.Zip(Skeleton.Joints))
            if (joint.Constraint != source.Constraint || joint.NativeJoint == 0 || string.IsNullOrWhiteSpace(joint.CurrentJoint) ||
                !bodies.TryGetValue(source.FirstBody, out var first) || first.CurrentBody != joint.FirstBody ||
                !bodies.TryGetValue(source.SecondBody, out var second) || second.CurrentBody != joint.SecondBody)
                throw new InvalidDataException("Ragdoll binding changed its actual joint endpoint graph.");
    }
}

internal sealed class FalloutLivingRagdollBinding
{
    private readonly FalloutLivingActorModel _model;
    private readonly List<FalloutLivingRagdollBodyBinding> _bodies = [];
    private readonly List<FalloutLivingRagdollJointBinding> _joints = [];
    private readonly FalloutRagdollSourceSkeleton _skeleton;
    private readonly FalloutReferenceModelDataInvocation _load;
    private bool _returned, _busy;
    private string? _failure;

    internal FalloutLivingRagdollBinding(FalloutLivingActorModel model, FalloutActorLogicalScene graph,
        FalloutReferenceModelDataInvocation load, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        requireActual(load); model.RequireCurrent();
        _model = model; _load = load; _skeleton = FalloutRagdollSourceSkeleton.Read(model.Source, graph);
    }

    internal void Bind(Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        if (_busy || _returned || _failure is not null || _bodies.Count != 0 || _joints.Count != 0)
            throw new InvalidOperationException("Ragdoll skeleton binding replayed its actual native resource join.");
        _busy = true;
        try
        {
            Check(); var bodies = _model.Bodies.ToDictionary(body => body.Source.Block.Index);
            foreach (var source in _skeleton.Bones)
            {
                Check(); var body = bodies[source.Body]; body.RequireCurrent();
                if (!body.Backend.HasPhysicalObject || body.Backend.PhysicalObjectId == 0 || body.Name != source.Name)
                    throw new InvalidDataException("Ragdoll skeleton did not bind its genuine current named native body.");
                _bodies.Add(new(source.Body, body.Key, body.Backend.PhysicalObjectId, source.Scene, source.ParentBody, body.Name));
            }
            var joints = _model.Constraints.ToDictionary(joint => joint.Source.Header.Block.Index);
            foreach (var source in _skeleton.Joints)
            {
                Check(); var joint = joints[source.Constraint]; joint.RequireCurrent();
                if (!joint.Backend.HasPhysicalObject || joint.Backend.PhysicalObjectId == 0 ||
                    !ReferenceEquals(joint.FirstBody, bodies[source.FirstBody]) || !ReferenceEquals(joint.SecondBody, bodies[source.SecondBody]))
                    throw new InvalidDataException("Ragdoll skeleton joined another current joint or body endpoint.");
                _joints.Add(new(source.Constraint, joint.Key, joint.Backend.PhysicalObjectId, joint.FirstBody.Key, joint.SecondBody.Key));
            }
            Check(); _returned = true;
        }
        catch (Exception error) { _failure ??= error.Message; throw; }
        finally { _busy = false; }
        void Check() { requireActual(_load); _model.RequireCurrent(); }
    }

    internal FalloutLivingRagdollBindingSnapshot Capture()
    {
        var saved = new FalloutLivingRagdollBindingSnapshot(_load, _model.Generation, _skeleton,
            _bodies.ToArray(), _joints.ToArray(), _returned, _failure);
        saved.Validate(); return saved;
    }
}
