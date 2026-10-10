using System.Runtime.ExceptionServices;
using Godot;
using OpenNV.Runtime.World;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private RuntimeNativeGlobalTime? _nativeGlobalTime;
    private FalloutReferenceWorld? _nativeGlobalTimeWorld;
    private Exception? _nativeGlobalTimeFailure;

    private object NativeGlobalTimeState => new
    {
        native = _nativeGlobalTime?.State,
        failure = _nativeGlobalTimeFailure?.ToString(),
        actualCurrentWorld = _nativeGlobalTimeWorld is not null && ReferenceEquals(_nativeGlobalTimeWorld, _nativeReferences),
    };

    // Called on the living native thread after immutable source preparation,
    // before bootstrap, ability, result or recurring scripts can execute.
    private void PublishNativeGlobalTime(FalloutReferenceWorld world)
    {
        if (!ReferenceEquals(world, _nativeReferences))
            throw new InvalidOperationException("Global time publication changed the actual campaign world.");
        if (_nativeGlobalTimeFailure is { } original)
            throw new InvalidOperationException("Global time retains its entered native construction failure.", original);
        var owner = world.ScriptGlobalTime;
        if (owner is null)
        {
            if (_nativeGlobalTime is not null || _nativeGlobalTimeWorld is not null)
                throw new InvalidOperationException("Unsupported source cannot replace a living global rate owner.");
            return; // No source declaration, command or native property claim.
        }
        if (_nativeGlobalTime is { } existing)
        {
            if (!ReferenceEquals(_nativeGlobalTimeWorld, world) || !ReferenceEquals(existing.SourceOwner, owner) ||
                !GodotObject.IsInstanceValid(existing) || existing.GetParent() != this || !existing.IsInsideTree() ||
                existing.IsQueuedForDeletion() || existing.NativeRetirementComplete || owner.SaveBlocker is not null)
                throw new InvalidOperationException("Global time lost its current native/source/thread publication.");
            return;
        }
        _nativeGlobalTimeWorld = world;
        try
        {
            var candidate = RuntimeNativeGlobalTime.Create(owner);
            _nativeGlobalTime = candidate; // Retained before the first native attachment/property write.
            AddChild(candidate);
            if (candidate.GetParent() != this || !candidate.IsInsideTree())
                throw new InvalidOperationException("Global rate node has no actual campaign parent.");
            candidate.PublishNative();
        }
        catch (Exception failure)
        {
            _nativeGlobalTimeFailure = failure;
            try { RetireNativeGlobalTime(world); }
            catch (Exception cleanup)
            {
                var retained = new AggregateException("Global rate construction retains its original and independent native cleanup failures.", failure, cleanup);
                _nativeGlobalTimeFailure = retained;
                throw retained;
            }
            // Successful native cleanup cannot replay this failed source lifetime.
            _nativeGlobalTimeFailure = failure;
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private void RetireNativeGlobalTime(FalloutReferenceWorld world)
    {
        if (_nativeGlobalTimeWorld is null)
        {
            if (_nativeGlobalTime is not null)
                throw new InvalidOperationException("Global rate retirement lost its actual world lease.");
            return;
        }
        if (!ReferenceEquals(_nativeGlobalTimeWorld, world))
            throw new InvalidOperationException("Global rate retirement received a foreign world.");
        if (_nativeGlobalTime is { } node)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.RetirePresentation();
                if (!node.NativeRetirementComplete)
                    throw new InvalidOperationException("Returned global rate retirement still owns native simulation/audio properties.");
                node.GetParent()?.RemoveChild(node);
                node.Free();
            }
            else if (!node.NativeRetirementComplete)
                throw new InvalidOperationException("Freed global rate node lost a still-owned native property lease.");
            _nativeGlobalTime = null;
        }
        _nativeGlobalTimeWorld = null;
        _nativeGlobalTimeFailure = null;
    }
}
