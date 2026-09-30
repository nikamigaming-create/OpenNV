using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferencePresentation
{
    private readonly Dictionary<FalloutFormKey, RuntimeNifControllerPlayer[]> _objectControllers = [];
    private readonly Dictionary<FalloutFormKey, FalloutReferenceInstance> _animationInstances = [];
    private readonly Dictionary<FalloutFormKey, Func<IReadOnlyList<FalloutObjectAnimationSnapshot>>> _animationCaptures = [];

    private void BindObjectAnimation(FalloutFormKey key, Node3D node)
    {
        if (node is RuntimeNativeNpc or RuntimeNativeCreature) return;
        var controllers = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(node).ToArray();
        var managed = controllers.Where(controller => controller.SourceController >= 0).ToArray();
        if (managed.GroupBy(controller => (controller.SourceSha256, controller.SourceController)).Any(group => group.Count() != 1))
            throw new NotSupportedException($"Reference {key} has ambiguous source animation controllers.");
        var instance = _world.Get(key);
        var restored = (instance.CaptureObjectAnimations?.Invoke() ?? instance.ObjectAnimations ?? []).Select(state =>
        {
            var controller = managed.SingleOrDefault(value => value.SourceController == state.Controller &&
                value.SourceSha256.Equals(state.Sha256, StringComparison.OrdinalIgnoreCase)) ??
                throw new NotSupportedException($"Reference {key} saved animation differs from its winning model.");
            controller.ValidateScriptState(state);
            return (Controller: controller, State: state);
        }).ToArray();
        foreach (var (controller, state) in restored) controller.RestoreScriptState(state);
        _objectControllers[key] = controllers;
        _animationInstances[key] = instance;
        if (managed.Length != 0)
        {
            IReadOnlyList<FalloutObjectAnimationSnapshot> Capture() => managed.Select(controller => controller.CaptureScriptState())
                .OfType<FalloutObjectAnimationSnapshot>().ToArray();
            _animationCaptures.Add(key, Capture);
            instance.BindObjectAnimationCapture(Capture);
        }
    }

    private void UnbindObjectAnimation(FalloutFormKey key)
    {
        if (!_objectControllers.Remove(key)) return;
        var instance = _animationInstances[key];
        _animationInstances.Remove(key);
        if (_animationCaptures.Remove(key, out var capture)) instance.UnbindObjectAnimationCapture(capture);
    }

    public override void _ExitTree()
    {
        foreach (var key in _objectControllers.Keys.ToArray()) UnbindObjectAnimation(key);
    }

    private RuntimeNifControllerPlayer[] ObjectControllers(FalloutFormKey key)
    {
        var node = Resolve(key) ?? throw new NotSupportedException($"Animation target {key} has no source model.");
        if (node is RuntimeNativeNpc or RuntimeNativeCreature)
            throw new NotSupportedException("Actor animation groups require the actor's skeleton owner.");
        return _objectControllers[key];
    }

    internal void PlayGroup(FalloutFormKey key, string group, int initialization)
    {
        var matching = ObjectControllers(key).Where(controller => controller.SourceController >= 0 && controller.HasSequence(group)).ToArray();
        if (matching.Length != 1) throw new NotSupportedException($"Reference {key} has no unique source animation group {group}.");
        matching[0].RequestSourceSequence(group, initialization);
    }

    internal bool IsAnimPlaying(FalloutFormKey key, string? group)
    {
        var controllers = ObjectControllers(key);
        if (group is null) return controllers.Any(controller => controller.Playing);
        // The optional group selects a sequence type, not an active clip name.
        // Object alternatives in one source manager share that type. Actor
        // upper/lower-body types and absent groups remain explicit boundaries.
        var matching = controllers.Where(controller => controller.SourceController >= 0 && controller.HasSequence(group)).ToArray();
        if (matching.Length != 1) throw new NotSupportedException($"Reference {key} has no unique animation type for group {group}.");
        return matching[0].Playing;
    }
}
