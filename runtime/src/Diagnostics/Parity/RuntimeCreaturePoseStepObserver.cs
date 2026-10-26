using Godot;

namespace OpenNV.Runtime.Diagnostics.Parity;

// Final-priority physics samples follow gameplay pose publication. Draw samples
// follow the renderer's actual frame signal; no images or textures are read.
internal sealed partial class RuntimeCreaturePoseStepObserver : Node
{
    internal Action<double>? Physics, Render, Heartbeat;
    public override void _Ready() => RenderingServer.FramePostDraw += AfterDraw;
    public override void _PhysicsProcess(double delta) => Physics?.Invoke(delta);
    public override void _Process(double delta) => Heartbeat?.Invoke(delta);
    private void AfterDraw() => Render?.Invoke(GetProcessDeltaTime());
    public override void _ExitTree() => RenderingServer.FramePostDraw -= AfterDraw;
}
