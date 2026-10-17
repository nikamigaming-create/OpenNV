using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRagdollWorldUpdate(FalloutReferenceModelDataInvocation Load, bool RestoredPose,
    long Entered, long Changed, IReadOnlyList<FalloutNifSceneKey> WorldStores,
    IReadOnlyList<FalloutRagdollBodyPoseStore> BodyStores, bool BoundsUpdated, bool Returned, string? Failure);

internal sealed partial class FalloutActorLogicalScene
{
    private readonly List<FalloutRagdollWorldUpdate> _ragdollWorldUpdates = [];

    internal FalloutNifCollisionObject? ReadRagdollTemplateCollision(FalloutReferenceModelDataInvocation load,
        FalloutNifSceneObject node, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        requireActual(load); RequireRagdollTemplateWorld(load);
        if (!_objects.TryGetValue(node.Key, out var current) || !ReferenceEquals(node, current))
            throw new InvalidDataException("Ragdoll collision read selected another actual logical model object.");
        return CollisionDeclaration(node);
    }

    internal void RequireRagdollTemplateWorld(FalloutReferenceModelDataInvocation load)
    {
        RequireIdle(allowFailure: true);
        if (_failure is not null || _ragdollTemplatePose is not { Applied: true, Restored: false, Failure: null } pose ||
            pose.Load != load || _ragdollWorldUpdates is not [{ Returned: true, RestoredPose: false } world] || world.Load != load)
            throw new InvalidOperationException("Physical skeleton requires the same returned temporary-pose world update.");
    }

    internal void UpdateRagdollTemplateWorld(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual,
        Func<FalloutNifSceneObject, FalloutRagdollBodyPoseStore?> collisionUpdate)
    {
        RequireIdle(allowFailure: true); requireActual(load);
        if (_failure is not null || _ragdollTemplatePose is not { Applied: true, Failure: null } pose || pose.Load != load ||
            _ragdollWorldUpdates.Count != (pose.Restored ? 1 : 0) || _ragdollWorldUpdates.Any(call => !call.Returned))
            throw new InvalidOperationException("Ragdoll world update lost its actual ordered temporary-pose call.");
        var worlds = new List<FalloutNifSceneKey>(); var bodies = new List<FalloutRagdollBodyPoseStore>();
        var index = _ragdollWorldUpdates.Count; var entered = Next();
        _ragdollWorldUpdates.Add(new(load, pose.Restored, entered, entered, worlds.AsReadOnly(), bodies.AsReadOnly(), false, false, null));
        var fault = _reentry; _busy = true;
        try
        {
            // Both source call sites explicitly pass a zero controller-delivery
            // byte. The current animation manager/interpolators are untouched.
            var anchor = FalloutNifSceneMath.Identity with { Scale = _model.Appearance.Height };
            foreach (var node in _order)
            {
                Check();
                node.World = node.Parent is { } parent && (node.Flags & 0x200) != 0 ? FalloutNifSceneMath.Copy(parent.World) :
                    FalloutNifSceneMath.Compose(node.Parent?.World ?? anchor, node.Local);
                worlds.Add(node.Key); Changed();
                if (collisionUpdate(node) is { } body) { bodies.Add(body); Changed(); }
                Check();
            }
            UpdateCurrentBounds(); Changed();
            _ragdollWorldUpdates[index] = _ragdollWorldUpdates[index] with { BoundsUpdated = true }; Check();
            _ragdollWorldUpdates[index] = _ragdollWorldUpdates[index] with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            _ragdollWorldUpdates[index] = _ragdollWorldUpdates[index] with { Failure = error.Message, Changed = Next() }; throw;
        }
        finally { _busy = false; }
        void Changed() => _ragdollWorldUpdates[index] = _ragdollWorldUpdates[index] with { Changed = Next() };
        void Check()
        {
            requireActual(load);
            if (_reentry != fault || load.ThreadId != _thread) throw new InvalidOperationException("Ragdoll world update swallowed source reentry or changed thread.");
        }
    }

    private void RestoreRagdollWorldUpdates(FalloutActorLogicalSceneSnapshot saved)
    {
        foreach (var call in saved.RagdollWorldUpdates ?? [])
        {
            if (!call.WorldStores.SequenceEqual(_order.Take(call.WorldStores.Count).Select(node => node.Key)))
                throw new InvalidDataException("Cold ragdoll world update reordered its source graph traversal.");
            _ragdollWorldUpdates.Add(call);
        }
    }
    private static void ValidateRagdollWorldUpdates(FalloutActorLogicalSceneSnapshot saved)
    {
        var calls = saved.RagdollWorldUpdates ?? [];
        if (calls.Count > 2 || calls.Count != 0 && saved.RagdollTemplatePose is null)
            throw new InvalidDataException("Cold ragdoll world update has no actual pose-copy parent.");
        long previous = saved.RagdollTemplatePose?.Entered ?? 0;
        foreach (var (call, index) in calls.Select((call, index) => (call, index)))
        {
            if (call.Load != saved.RagdollTemplatePose!.Load || call.RestoredPose != (index == 1) ||
                call.RestoredPose && !saved.RagdollTemplatePose.Restored || call.Entered <= previous || call.Changed <= call.Entered ||
                call.Changed > saved.Sequence || call.WorldStores is null || call.BodyStores is null ||
                call.WorldStores.Count > saved.Objects.Count || call.Returned && (!call.BoundsUpdated || call.WorldStores.Count != saved.Objects.Count) ||
                call.Returned != (call.Failure is null) || call.Failure is not null && call.Failure != saved.Failure ||
                call.BodyStores.Any(store => store.NativeBody == 0 || store.Body < 0))
                throw new InvalidDataException("Cold ragdoll world update lost its current ordered stores or failed prefix.");
            previous = call.Changed;
        }
    }
}
