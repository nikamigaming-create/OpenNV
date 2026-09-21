using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseEnvironmentPixels()
    {
        var shader = new Shader
        {
            Code = "shader_type spatial; render_mode unshaded;\n" + NativeNifMaterialEnvironment.ShaderSource +
                "\nvoid fragment() { ALBEDO = owned_environment_ambient() + owned_environment_fog_color() * .1 + " +
                "owned_environment_fog_range() * .001 + vec3(owned_environment_fog_units() * .0001); }"
        };
        var material = new ShaderMaterial { Shader = shader, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
        var first = Create(); var second = Create();
        try
        {
            var ambient = new Vector3(.2f, .1f, .05f); var fog = new Vector3(.1f, .2f, .3f); var range = new Vector3(1, 2, 1);
            NativeNifMaterialEnvironment.Bind(first.Mesh, ambient, fog, range, .0142875f);
            NativeNifMaterialEnvironment.Bind(second.Mesh, ambient, fog, range, .0142875f);
            NativeNifMaterialEnvironment.BindExterior(second.Mesh);
            NativeNifMaterialEnvironment.PublishExterior(ambient, fog, range, .0142875f);
            await Draw();
            var local = Pixel(first.View); var exterior = Pixel(second.View);
            Require(Distance(local, exterior) < .01f, "Shared exterior constants changed the local material's pixels.");
            NativeNifMaterialEnvironment.PublishExterior(new(.05f, .5f, .1f), new(.3f, .1f, .2f), new(2, 1, 2), .02f);
            await Draw();
            Require(Distance(local, Pixel(first.View)) < .01f && Distance(exterior, Pixel(second.View)) > .1f,
                "Weather publication missed the exterior or changed an independent local environment.");
            NativeNifMaterialEnvironment.Bind(second.Mesh, ambient, fog, range, .0142875f);
            await Draw();
            Require(Distance(local, Pixel(second.View)) < .01f, "Interior rebinding retained exterior constants.");
            GD.Print("OPENNV_ENVIRONMENT_PIXELS_PASS localEqualsShared=true weatherUpdates=true independentViewport=true interiorRebind=true");
        }
        finally { first.View.Free(); second.View.Free(); }

        (SubViewport View, MeshInstance3D Mesh) Create()
        {
            var view = new SubViewport { Size = new(64, 64), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view);
            view.AddChild(new Camera3D { Position = new(0, 0, 2), Projection = Camera3D.ProjectionType.Orthogonal, Size = 2, Current = true });
            var mesh = new MeshInstance3D { Mesh = new QuadMesh { Size = new(2, 2) }, MaterialOverride = material };
            view.AddChild(mesh);
            return (view, mesh);
        }
        async Task Draw()
        {
            for (var frame = 0; frame < 4; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        static Color Pixel(SubViewport view) { using var image = view.GetTexture().GetImage(); return image.GetPixel(32, 32); }
        static float Distance(Color a, Color b) => new Vector3(a.R - b.R, a.G - b.G, a.B - b.B).Length();
        static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
