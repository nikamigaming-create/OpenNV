using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeSessionRetirementAudit : Node3D
{
    public override async void _Ready()
    {
        Node3D? cell = null;
        var retained = new List<RetailHdrCompositorEffect>();
        try
        {
            if (RenderingServer.GetRenderingDevice() is null)
                throw new InvalidOperationException("This lifecycle fixture requires the native Forward+ renderer.");
            var configuration = RuntimeConfiguration.Load();
            RetailHdrCompositorEffect Effect() => new(1, 1, new(1, .2f, 1, 1), Vector4.Zero, Vector4.Zero,
                configuration.FalloutEnvironment.ImageSpace, configuration.Capture,
                configuration.ActorCompiler.FaceGenMaterial.RuntimeAlbedoTransfer, runtimeAdaptation: true);
            var unused = Effect(); retained.Add(unused);
            unused.ReleaseRenderingResources(); unused.ReleaseRenderingResources();
            if (unused.Enabled || unused.Operational) throw new InvalidOperationException("An unused retired effect remained enabled.");
            for (var iteration = 0; iteration < 4; iteration++)
            {
                cell = new Node3D(); AddChild(cell);
                var effect = Effect(); retained.Add(effect);
                var compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { effect } };
                cell.AddChild(new WorldEnvironment
                {
                    Environment = new Godot.Environment
                    {
                        BackgroundMode = Godot.Environment.BGMode.Color,
                        BackgroundColor = new(.1f, .2f, .3f),
                    },
                    Compositor = compositor,
                });
                cell.AddChild(new Camera3D { Position = new(0, 0, 3), Current = true });
                cell.AddChild(new MeshInstance3D { Mesh = new SphereMesh() });
                var traits = new float[32]; traits[4] = 1;
                var source = new FalloutImageSpace(new("Synthetic.esm", 1), 15, new('0', 64), traits,
                    null, new(1, .2f, 1, 1), System.Numerics.Vector4.Zero, []);
                var owner = new RuntimeNativeImageSpace();
                owner.Configure(source, new FalloutImageSpaceState(), effect); cell.AddChild(owner);
                for (var frame = 0; frame < 120 && !effect.Operational; frame++)
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!effect.Operational) throw new InvalidOperationException("The replacement cell did not render its compositor.");
                // Keep the resource alive after its cell dies: deferred managed
                // collection must not determine native GPU ownership.
                cell.Free(); cell = null;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                effect.ReleaseRenderingResources();
                if (effect.Enabled || effect.Operational)
                    throw new InvalidOperationException("The retired cell retained an active compositor.");
            }
            GD.Print("OPENNV_NATIVE_SESSION_RETIREMENT_PASS replacements=4 retainedEffects=true repeatedRelease=true unusedRelease=true fixture=true parity=unverified");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"OPENNV_NATIVE_SESSION_RETIREMENT_FAIL {error}");
            GetTree().Quit(1);
        }
        finally
        {
            cell?.Free();
            foreach (var effect in retained) { effect.ReleaseRenderingResources(); effect.Dispose(); }
        }
    }
}
