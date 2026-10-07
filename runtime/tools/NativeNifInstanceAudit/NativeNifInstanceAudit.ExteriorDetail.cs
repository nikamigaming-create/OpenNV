using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseExteriorDetailPixels()
    {
        using var shader = new Shader
        {
            Code = "shader_type spatial; render_mode unshaded;\n" + NativeExteriorDetailBlend.NearDeclarations +
                "\nvoid vertex() { " + NativeExteriorDetailBlend.NearVertex + " }\nvoid fragment() { " +
                NativeExteriorDetailBlend.NearFragment + " ALBEDO = vec3(1.0); }"
        };
        using var material = new ShaderMaterial { Shader = shader, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
        var terrain = Create(); var objects = Create(); var ordinary = Create();
        try
        {
            NativeExteriorDetailBlend.Bind(terrain, terrain: true);
            NativeExteriorDetailBlend.Bind(objects, terrain: false);
            foreach (var (terrainCovered, objectsCovered) in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                // The source geometry is outside the selected transition ring.
                // Only a lane with complete coverage may remove its near draw.
                NativeExteriorDetailBlend.SetRegion(new(100, 0, 100), 1, 3, terrainCovered, objectsCovered);
                for (var frame = 0; frame < 4; frame++)
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Require(Visible(terrain) == !terrainCovered, "Terrain fading borrowed object coverage.");
                Require(Visible(objects) == !objectsCovered, "Object fading borrowed terrain coverage.");
                Require(Visible(ordinary), "An ordinary unbound-to-LOD object disappeared at the detail boundary.");
            }
            GD.Print("OPENNV_EXTERIOR_DETAIL_PIXELS_PASS separateTerrainObjects=true incompleteRetained=true ordinaryRetained=true synthetic=true retailParity=unverified");
        }
        finally
        {
            NativeExteriorDetailBlend.SetRegion(Vector3.Zero, 1, 3, false, false);
            terrain.Free(); objects.Free(); ordinary.Free();
        }

        SubViewport Create()
        {
            var view = new SubViewport { Size = new(64, 64), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view);
            view.AddChild(new WorldEnvironment
            {
                Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black }
            });
            view.AddChild(new Camera3D { Position = new(0, 0, 2), Projection = Camera3D.ProjectionType.Orthogonal, Size = 2, Current = true });
            view.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new(2, 2) }, MaterialOverride = material });
            return view;
        }
        static bool Visible(SubViewport view)
        {
            using var image = view.GetTexture().GetImage();
            return image.GetPixel(32, 32).R > .5f;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
