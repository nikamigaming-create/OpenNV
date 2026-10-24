namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceInstance
{
    internal IReadOnlyList<FalloutObjectPhysicsSnapshot>? ObjectPhysics { get; set; }
    private readonly List<Func<IReadOnlyList<FalloutObjectPhysicsSnapshot>>> _physicsCaptures = [];
    internal Func<IReadOnlyList<FalloutObjectPhysicsSnapshot>>? CaptureObjectPhysics => _physicsCaptures.LastOrDefault();

    internal void BindObjectPhysicsCapture(Func<IReadOnlyList<FalloutObjectPhysicsSnapshot>> capture) => _physicsCaptures.Add(capture);

    internal void UnbindObjectPhysicsCapture(Func<IReadOnlyList<FalloutObjectPhysicsSnapshot>> capture)
    {
        if (!_physicsCaptures.Contains(capture)) throw new InvalidOperationException("Object physics capture was not bound.");
        try
        {
            if (CaptureObjectPhysics == capture) ObjectPhysics = capture();
        }
        finally { _physicsCaptures.Remove(capture); }
    }
}
