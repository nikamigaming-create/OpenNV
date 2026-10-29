using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private void ExerciseReferenceDisableLifecycle()
    {
        var root = new Node3D();
        var collision = new StaticBody3D { CollisionLayer = 5, CollisionMask = 9 };
        var order = new List<int>();
        var first = new NativeEnableAuditLifecycle();
        var second = new NativeEnableAuditLifecycle { Callback = () => order.Add(2) };
        first.Callback = () => { order.Add(1); first.RemoveChild(second); };
        root.AddChild(collision); root.AddChild(first); first.AddChild(second);
        AddChild(root);
        try
        {
            GamebryoReferenceEnableRuntime.Apply(root, false);
            if (!order.SequenceEqual(new[] { 1, 2 }) || collision.CollisionLayer != 0 || collision.CollisionMask != 0 ||
                root.Visible || root.ProcessMode != ProcessModeEnum.Disabled)
                throw new InvalidDataException("Disable lost callback order or left a live body.");
            GamebryoReferenceEnableRuntime.Apply(root, true);
            if (collision.CollisionLayer != 5 || collision.CollisionMask != 9 || !root.Visible)
                throw new InvalidDataException("Re-enable lost the body's intended collision filters.");
            first.AddChild(second);
            order.Clear();
            first.Callback = () => { order.Add(1); throw new InvalidOperationException("source retirement failed"); };
            try
            {
                GamebryoReferenceEnableRuntime.Apply(root, false);
                throw new InvalidDataException("A lifecycle failure was swallowed.");
            }
            catch (InvalidOperationException error) when (error.Message == "source retirement failed") { }
            if (!order.SequenceEqual(new[] { 1, 2 }) || collision.CollisionLayer != 0 || collision.CollisionMask != 0 || root.Visible)
                throw new InvalidDataException("A failed retirement left a callback or collision active.");
            GD.Print("OPENNV_REFERENCE_DISABLE_LIFECYCLE_PASS callbackOrder=true collisionRestore=true failureCloses=true");
        }
        finally { root.Free(); }
    }
}

internal partial class NativeEnableAuditLifecycle : Node, IGamebryoReferenceEnableLifecycle
{
    internal Action Callback { get; set; } = () => { };
    void IGamebryoReferenceEnableLifecycle.BeforeReferenceDisabled() => Callback();
}
