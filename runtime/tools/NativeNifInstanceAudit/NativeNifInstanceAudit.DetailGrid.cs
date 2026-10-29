using System.Collections;
using System.Reflection;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseDetailGridBinding()
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("LOD detail texture updates require the renderer.");
        var type = typeof(RuntimeNativeExteriorLod);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var owner = new RuntimeNativeExteriorLod();
        using var shader = NativeNifLodMaterial.BuildShader(false);
        using var material = new ShaderMaterial { Shader = shader };
        var root = new Node3D();
        try
        {
            type.GetField("_units", flags)!.SetValue(owner, 1f);
            var key = new FalloutFormKey("Fixture.esm", 1);
            FalloutCellDefinition Cell(int x, int y) => new(key, "fixture", 0, (x, y), key, null);
            FalloutExteriorGridScene Grid(params FalloutCellDefinition[] cells) => new(null!, cells, key, 1);
            owner.SetDetailGrid(Grid(Cell(0, 0), Cell(1, 1)));
            var first = (ImageTexture)type.GetField("_mask", flags)!.GetValue(owner)!;
            var cache = (IDictionary)type.GetField("_cache", flags)!.GetValue(owner)!;
            var residentType = type.GetNestedType("Resident", BindingFlags.NonPublic)!;
            var resident = Activator.CreateInstance(residentType, flags | BindingFlags.Public, null,
                [root, null, null, new[] { material }, 0L, true, true], null)!;
            cache.Add(new FalloutLodBlock(1, 0, 0, "fixture", null), resident);
            type.GetMethod("BindDetail", flags)!.Invoke(owner, [new[] { material }, true]);
            owner.SetDetailGrid(Grid(Cell(2, 0), Cell(3, 0), Cell(2, 1)));
            var reused = (ImageTexture)type.GetField("_mask", flags)!.GetValue(owner)!;
            if (first != reused || material.GetShaderParameter("detail_mask").AsGodotObject() != first ||
                material.GetShaderParameter("detail_bounds").AsVector4() != new Vector4(8192, -8192, 16384, 0) ||
                !material.GetShaderParameter("detail_mask_enabled").AsBool())
                throw new InvalidDataException("Same-sized LOD grid did not retain texture and update material bounds.");
            using (var image = reused.GetImage())
                if (image.GetPixel(0, 0).R < .9f || image.GetPixel(1, 0).R > .1f || image.GetPixel(1, 1).R < .9f)
                    throw new InvalidDataException("Reused LOD texture did not retain the new cell mask.");
            owner.SetDetailGrid(Grid(Cell(0, 0), Cell(2, 1)));
            var resized = (ImageTexture)type.GetField("_mask", flags)!.GetValue(owner)!;
            if (resized == first || resized.GetWidth() != 3 || material.GetShaderParameter("detail_mask").AsGodotObject() != resized)
                throw new InvalidDataException("Resized LOD grid did not replace and rebind its texture.");
            GD.Print("OPENNV_LOD_DETAIL_GRID_BINDING_PASS reuse=true updatedMask=true bounds=true resizeRebound=true retailPixels=unverified");
        }
        finally { owner.Free(); root.Free(); }
    }
}
