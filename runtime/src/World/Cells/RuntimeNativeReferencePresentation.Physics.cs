using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferencePresentation
{
    private readonly Dictionary<FalloutFormKey, (FalloutReferenceInstance Instance,
        Func<IReadOnlyList<FalloutObjectPhysicsSnapshot>> Capture, bool Active)> _physicsBindings = [];

    public override void _EnterTree()
    {
        foreach (var (key, binding) in _physicsBindings.ToArray())
        {
            binding.Instance.BindObjectPhysicsCapture(binding.Capture);
            _physicsBindings[key] = (binding.Instance, binding.Capture, true);
        }
    }

    private void BindObjectPhysics(FalloutFormKey key, RuntimeNifRigidBody[] bodies)
    {
        var instance = _world.Get(key);
        var saved = instance.CaptureObjectPhysics?.Invoke() ?? instance.ObjectPhysics;
        if (bodies.GroupBy(body => (body.SourceSha256, body.SourceBody)).Any(group => group.Count() != 1))
            throw new NotSupportedException($"Reference {key} has ambiguous dynamic source bodies.");
        if (saved is not null)
        {
            FalloutObjectPhysicsSnapshot.ValidateSet(saved);
            if (saved.Count != bodies.Length) throw new InvalidDataException($"Reference {key} saved dynamic body set differs from its winning model.");
            var restored = saved.Select(state => (State: state, Body: bodies.SingleOrDefault(body => body.SourceBody == state.Body &&
                body.SourceSha256.Equals(state.Sha256, StringComparison.OrdinalIgnoreCase)) ??
                throw new InvalidDataException($"Reference {key} saved physics differs from its winning model."))).ToArray();
            foreach (var (state, body) in restored) body.ValidatePhysics(state);
            foreach (var (state, body) in restored) body.RestorePhysics(state);
        }
        if (bodies.Length == 0) return;
        IReadOnlyList<FalloutObjectPhysicsSnapshot> Capture() => bodies.Select(body => body.CapturePhysics()).ToArray();
        var active = IsInsideTree();
        _physicsBindings.Add(key, (instance, Capture, active));
        if (active) instance.BindObjectPhysicsCapture(Capture);
    }

    private void UnbindObjectPhysics(FalloutFormKey key)
    {
        if (!_physicsBindings.Remove(key, out var binding)) return;
        if (binding.Active) binding.Instance.UnbindObjectPhysicsCapture(binding.Capture);
    }
}
