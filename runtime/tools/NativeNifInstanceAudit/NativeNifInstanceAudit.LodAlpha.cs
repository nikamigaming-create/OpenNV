using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseLodAlphaPixels()
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("LOD alpha needs the renderer.");
        using var image = Image.CreateFromData(4, 1, false, Image.Format.Rgba8,
            [255, 255, 255, 0, 255, 255, 255, 128, 255, 255, 255, 129, 255, 255, 255, 255]);
        using var texture = ImageTexture.CreateFromImage(image);
        foreach (var landscape in new[] { false, true })
        {
            using var shader = NativeNifLodMaterial.BuildShader(landscape);
            using var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("base_map", texture);
            material.SetShaderParameter("landscape", landscape);
            var view = new SubViewport { Size = new(256, 64), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view);
            try
            {
                view.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
                view.AddChild(new Camera3D { Position = new(0, 0, 2), Projection = Camera3D.ProjectionType.Orthogonal, Size = 4, Current = true });
                var mesh = new MeshInstance3D { Mesh = new QuadMesh { Size = new(4, 1) }, MaterialOverride = material };
                view.AddChild(mesh);
                mesh.SetInstanceShaderParameter("source_ambient", Vector3.One);
                mesh.SetInstanceShaderParameter("source_fog_range", new Vector3(1000, 2000, 1));
                mesh.SetInstanceShaderParameter("source_fog_game_units_per_meter", 1f);
                // Uniform samples avoid filtering across different texels at
                // the exact byte boundary; use the actual production shader.
                foreach (var alpha in new byte[] { 0, 128, 129, 255 })
                {
                    image.Fill(new Color(1, 1, 1, alpha / 255f)); texture.Update(image);
                    for (var frame = 0; frame < 4; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var pixels = view.GetTexture().GetImage();
                    var visible = pixels.GetPixel(128, 32).R > .5f;
                    if (visible != (landscape || alpha > 128)) throw new InvalidOperationException($"LOD alpha failed landscape={landscape} alpha={alpha} visible={visible}.");
                }
            }
            finally { view.Free(); }
        }
        GD.Print("OPENNV_LOD_ALPHA_PIXELS_PASS objectCutout=true thresholdBoundary=true terrainOpaque=true retailPixels=unverified");
    }
}
