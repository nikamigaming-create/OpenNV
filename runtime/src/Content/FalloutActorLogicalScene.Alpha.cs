using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutActorLogicalScene
{
    internal FalloutNifSceneAlphaCall SetAlpha(Guid invocation, uint alphaBits)
    {
        RequireIdle();
        if (invocation == Guid.Empty) throw new ArgumentException("Logical scene alpha requires its actual child invocation.", nameof(invocation));
        var entered = Next(); var stores = new List<FalloutNifSceneKey>();
        _alpha = new(invocation, alphaBits, entered, entered, FalloutNifSceneAlphaStep.Root, null, Root.Key, [], null);
        _busy = true;
        try
        {
            // An explicit frame stack preserves the source's child-store-before-
            // node-recursion order without imposing a managed recursion limit.
            var pending = new Stack<(FalloutNifSceneObject Node, int Index)>();
            if (Traversable(Root)) pending.Push((Root, 0));
            while (pending.TryPop(out var frame))
            {
                var parent = frame.Node;
                if (frame.Index >= parent.Children.Count) continue;
                pending.Push((parent, frame.Index + 1));
                var child = parent.Children[frame.Index];
                if (child is null) continue;
                Step(FalloutNifSceneAlphaStep.Child, child.Key);
                if (child.IsGeometry && child.Shader is { } property && property.ShaderKind != -1)
                {
                    Step(FalloutNifSceneAlphaStep.ShaderStore, child.Key);
                    property.StoreShaderAlpha(alphaBits); stores.Add(property.Key);
                    _alpha = _alpha! with { Changed = Next() };
                }
                Step(FalloutNifSceneAlphaStep.NodeRecursion, child.Key);
                if (child.IsNode && Traversable(child)) pending.Push((child, 0));
            }
            _alpha = _alpha! with { Stores = Array.AsReadOnly(stores.ToArray()), Step = FalloutNifSceneAlphaStep.Returned, Changed = Next() }; return _alpha;
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            _alpha = _alpha! with
            {
                Stores = Array.AsReadOnly(stores.ToArray()),
                FailedAt = _alpha.Step,
                Step = FalloutNifSceneAlphaStep.Failed,
                Changed = Next(),
                Failure = error.Message
            };
            throw;
        }
        finally { _busy = false; }
        void Step(FalloutNifSceneAlphaStep step, FalloutNifSceneKey key) => _alpha = _alpha! with { Step = step, Current = key, Changed = Next() };
    }
    internal static bool Traversable(FalloutNifSceneObject? node) =>
        node is not null && node.Bound is { } bound && bound.Sphere.Radius != 0;
}
