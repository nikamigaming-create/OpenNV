using Godot;

namespace OpenNV.Runtime.World.Cells;

internal static partial class NativeCharacterStep
{
    // One planner cursor owns these mutable native query objects. Ordinary
    // mover calls still create their own objects, and every support query
    // resolves the body's live world through QuerySupport as before.
    internal sealed class QueryResources(CharacterBody3D body) : IDisposable
    {
        private readonly ulong _owner = body.GetInstanceId();
        private readonly Rid _rid = body.GetRid();
        private PhysicsTestMotionParameters3D? _parameters;
        private PhysicsTestMotionResult3D? _result;
        private PhysicsRayQueryParameters3D? _ray;
        private bool _disposed;

        private void RequireOwner(CharacterBody3D current)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!GodotObject.IsInstanceValid(current) || current.GetInstanceId() != _owner || !current.GetRid().Equals(_rid))
                throw new InvalidOperationException("Native step query resources belong to another body.");
        }

        internal PhysicsTestMotionParameters3D MotionParameters(CharacterBody3D current)
        {
            RequireOwner(current);
            _parameters ??= new();
            _parameters.Margin = current.SafeMargin; _parameters.MaxCollisions = 4;
            return _parameters;
        }

        internal PhysicsTestMotionResult3D MotionResult(CharacterBody3D current)
        {
            RequireOwner(current);
            return _result ??= new();
        }

        internal PhysicsRayQueryParameters3D SupportRay(CharacterBody3D current)
        {
            RequireOwner(current);
            _ray ??= PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, current.CollisionMask, [_rid]);
            _ray.CollisionMask = current.CollisionMask;
            return _ray;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ray?.Dispose(); _result?.Dispose(); _parameters?.Dispose();
        }
    }
}
