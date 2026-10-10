using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

public partial class NativeRenderedMenuAudit
{
    private async Task MenuPixelOutput(string? root, string[] options)
    {
        GD.Print("OPENNV_MENU_PIXEL_OUTPUT_ASSEMBLY " + JsonSerializer.Serialize(new
        {
            location = GetType().Assembly.Location,
            mvid = GetType().Assembly.ManifestModule.ModuleVersionId,
            driver = RenderingServer.GetCurrentRenderingDriverName(),
            method = RenderingServer.GetCurrentRenderingMethod(),
        }));
        var direct = FalloutD3D9PixelProgram.Read(MenuOutputWords(0xffff0200,
            0x02000001, 0x800f0800, 0xa0e40000, 0x0000ffff));
        var green = new NumericsVector4(26f / 255, 1, 128f / 255, 1);
        var authored = await RenderMenuPixelOutput(direct, new Dictionary<int, NumericsVector4> { [0] = green },
            new Dictionary<int, NumericsVector4>(), green);
        object? owned = null;
        try
        {
            if (root is not null)
            {
                using var selected = OpenUiColorSource(root, options);
                RuntimeLiveContentSource.Configure(root, selected.Game, selected.ContentRoots.Skip(1).ToArray(),
                    selected.PluginSources.Select(plugin => plugin.Name).ToArray(), selected.Settings);
                var source = RuntimeLiveContentSource.Current!;
                var settings = FalloutInstallationSettings.Read(source);
                var path = $"shaders/shaderpackage{settings.Renderer.ShaderPackage:000}.sdp";
                if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
                var hash = SHA256.HashData(bytes);
                var entry = FalloutShaderPackage.Read(bytes).Single(shader =>
                    shader.Name.Equals("ISTV.pso", StringComparison.OrdinalIgnoreCase));
                var program = FalloutD3D9PixelProgram.Read(entry.Bytecode, new HashSet<int> { 1, 2 });
                if (!program.Constants.SequenceEqual(new[] { 0, 1, 2, 3, 4 }) ||
                    !program.Samplers.SequenceEqual(new[] { 0, 1, 2 }))
                    throw new NotSupportedException("Selected rendered-menu pixel interface has another owner.");
                var constants = new Dictionary<int, NumericsVector4>
                {
                    [0] = new(1f / 1280, 1f / 960, 0, 0),
                    [1] = new(1, 0, 0, 0),
                    [2] = new(0, -1, 0, 0),
                    [3] = new(0, -1, 0, 0),
                    [4] = NumericsVector4.One,
                };
                // These are independently authored uniform fixture textures,
                // not a claimed original menu pose, effect map or final pixel.
                var textures = new Dictionary<int, NumericsVector4>
                {
                    [0] = green,
                    [1] = NumericsVector4.One,
                    [2] = NumericsVector4.One,
                };
                var expected = program.Evaluate(new(0.8f, 0.5f), constants, (index, _) => textures[index]);
                if (!float.IsFinite(expected.X) || !float.IsFinite(expected.Y) || !float.IsFinite(expected.Z))
                    throw new InvalidDataException("Owned instruction output is non-finite in the authored transport.");
                var native = await RenderMenuPixelOutput(program, constants, textures, expected);
                if (!source.TryRead(path, null, out var after, out var afterIdentity) || afterIdentity != identity ||
                    !SHA256.HashData(after).AsSpan().SequenceEqual(hash))
                    throw new InvalidDataException("Pixel output fixture changed its original winning package.");
                owned = new
                {
                    source.Game,
                    source.StackId,
                    identity,
                    sourceSha256 = Convert.ToHexString(hash),
                    bytecodeSha256 = Convert.ToHexString(SHA256.HashData(entry.Bytecode.Span)),
                    native,
                    sourceBytesUnchanged = true,
                    fixtureTextures = true,
                    originalMenuPose = false,
                };
            }
            else if (options.Length != 0) throw new ArgumentException("Authored pixel check has no selection options.");
        }
        finally { if (root is not null) RuntimeLiveContentSource.Clear(); }
        GD.Print("OPENNV_NATIVE_MENU_PIXEL_OUTPUT_PASS " + JsonSerializer.Serialize(new
        {
            authored,
            owned,
            blackGreyWhiteGeometry = true,
            originalBlackOutputPreserved = true,
            recording = false,
            retainedFrames = 0,
            campaignAcceptance = false,
            retailPixelParity = false,
        }));
    }

    private async Task<object> RenderMenuPixelOutput(FalloutD3D9PixelProgram program,
        IReadOnlyDictionary<int, NumericsVector4> constants,
        IReadOnlyDictionary<int, NumericsVector4> samples, NumericsVector4 expected)
    {
        SubViewport? view = null;
        var resources = new List<Resource>();
        var meshes = new List<ArrayMesh>();
        try
        {
            view = new()
            {
                Size = new(192, 48),
                OwnWorld3D = true,
                TransparentBg = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            AddChild(view);
            var camera = new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal,
                KeepAspect = Camera3D.KeepAspectEnum.Height,
                Size = 2,
                Near = 0.01f,
                Far = 10,
                Current = true,
            };
            view.AddChild(camera);
            var shader = new Shader { Code = FalloutD3D9MenuSurface.SpatialShader(program) };
            resources.Add(shader);
            var material = new ShaderMaterial { Shader = shader };
            resources.Add(material);
            foreach (var index in program.Constants)
            {
                var value = constants[index];
                material.SetShaderParameter($"c{index}", new Vector4(value.X, value.Y, value.Z, value.W));
            }
            foreach (var index in program.Samplers)
            {
                var value = samples[index];
                using var image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
                image.Fill(new(value.X, value.Y, value.Z, value.W));
                var texture = ImageTexture.CreateFromImage(image);
                resources.Add(texture);
                material.SetShaderParameter($"s{index}", texture);
            }
            var blackProgram = FalloutD3D9PixelProgram.Read(MenuOutputWords(0xffff0200,
                0x02000001, 0x800f0800, 0xa0e40000, 0x0000ffff));
            var blackShader = new Shader { Code = FalloutD3D9MenuSurface.SpatialShader(blackProgram) };
            resources.Add(blackShader);
            var blackMaterial = new ShaderMaterial { Shader = blackShader };
            resources.Add(blackMaterial);
            blackMaterial.SetShaderParameter("c0", new Vector4(0, 0, 0, 1));
            var colors = new[] { Colors.Black, new Color(0.5f, 0.5f, 0.5f, 1), Colors.White, Colors.White };
            for (var index = 0; index < colors.Length; index++)
            {
                using var quad = new QuadMesh { Size = new(1.5f, 1.5f) };
                using var arrays = quad.SurfaceGetArrays(0);
                var count = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
                arrays[(int)Mesh.ArrayType.Color] = Enumerable.Repeat(colors[index], count).ToArray();
                arrays[(int)Mesh.ArrayType.TexUV] = Enumerable.Repeat(new Vector2(0.8f, 0.5f), count).ToArray();
                var mesh = new ArrayMesh();
                meshes.Add(mesh);
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                view.AddChild(new MeshInstance3D
                {
                    Mesh = mesh,
                    MaterialOverride = index == 3 ? blackMaterial : material,
                    Position = new(-3 + index * 2, 0, -2),
                });
            }
            await MenuOutputDrawFrames(4);
            using var pixels = view.GetTexture().GetImage();
            if (pixels.IsEmpty()) throw new InvalidDataException("Menu output has no genuine native rendered pixels.");
            var actual = Enumerable.Range(0, 4).Select(index => pixels.GetPixel(24 + index * 48, 24)).ToArray();
            for (var index = 0; index < 3; index++)
            {
                var sample = actual[index];
                var tolerance = 2f / 255;
                if (MathF.Abs(sample.R - Math.Clamp(expected.X, 0, 1)) > tolerance ||
                    MathF.Abs(sample.G - Math.Clamp(expected.Y, 0, 1)) > tolerance ||
                    MathF.Abs(sample.B - Math.Clamp(expected.Z, 0, 1)) > tolerance)
                    throw new InvalidDataException($"Mesh colour {index} changed original pixel output: expected={expected}; actual={sample}.");
            }
            if (actual[0] != actual[1] || actual[1] != actual[2])
                throw new InvalidDataException("Black, grey and white geometry produced different decoded-program output.");
            if (actual[3].R != 0 || actual[3].G != 0 || actual[3].B != 0)
                throw new InvalidDataException("A source-black pixel program was artificially brightened.");
            return new
            {
                expected = new[] { expected.X, expected.Y, expected.Z },
                actual = actual.Select(value => new[] { value.R, value.G, value.B }).ToArray(),
            };
        }
        finally
        {
            view?.Free();
            foreach (var mesh in meshes) mesh.Dispose();
            foreach (var resource in resources.AsEnumerable().Reverse()) resource.Dispose();
            await MenuOutputDrawFrames(2);
        }
    }

    private async Task MenuOutputDrawFrames(int frames)
    {
        var rendering = Draw();
        if (await Task.WhenAny(rendering, Task.Delay(TimeSpan.FromSeconds(2))) != rendering)
            throw new InvalidOperationException("Native renderer did not complete bounded menu pixel frames.");
        await rendering;
        async Task Draw()
        {
            for (var index = 0; index < frames; index++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    private static byte[] MenuOutputWords(params uint[] words)
    {
        var bytes = new byte[checked(words.Length * sizeof(uint))];
        for (var index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)), words[index]);
        return bytes;
    }
}
