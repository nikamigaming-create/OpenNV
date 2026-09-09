using System.Numerics;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class SurfaceInputsContracts
{
    internal static void Run()
    {
        var uv = new Vector2(.3f, .7f);
        foreach (var scale in new[] { .1f, 1f, 100f })
        {
            Near(FalloutNifSurfaceInputs.ParallaxCoordinates(uv, 1, new Vector3(3, 0, 4) * scale), uv + new Vector2(.012f, 0));
            Near(FalloutNifSurfaceInputs.ParallaxCoordinates(uv, 0, new Vector3(0, -3, 4) * scale), uv + new Vector2(0, .012f));
        }
        Near(FalloutNifSurfaceInputs.ParallaxCoordinates(uv, .5f, new(4, 3, 0)), uv);
        Near(FalloutNifSurfaceInputs.ParallaxCoordinates(uv, 1, Vector3.UnitZ), uv);
        Near(FalloutNifSurfaceInputs.ParallaxCoordinates(uv, 1, Vector3.Zero), uv);
        Reject(() => FalloutNifSurfaceInputs.ParallaxCoordinates(uv, float.NaN, Vector3.One));
        foreach (var path in new[] { "textures/rubble.dds", "Data/Textures/Rubble.dds", "data\\textures\\rubble.dds" })
            if (FalloutNifSurfaceInputs.TexturePath(path) != "textures\\rubble.dds") throw new Exception("NIF texture source prefix differs.");
        foreach (var path in new[] { "Data/../textures/a.dds", "Data/./textures/a.dds", "C:/Data/textures/a.dds", "/Data/textures/a.dds" })
            Reject(() => FalloutNifSurfaceInputs.TexturePath(path));
        var vector = new FalloutNifVector3(0, 0, 1);
        var data = new FalloutNifMeshData(new(0, "NiTriShapeData", 0, 0), [vector], [vector], [vector], [vector], [],
            [[new(0, 0)]], [], vector, 1, 0, -1, 1, 1);
        FalloutNifSurfaceInputs.RequireParallaxInputs(data, true, true, true);
        Reject(() => FalloutNifSurfaceInputs.RequireParallaxInputs(data, true, true, false));
        Reject(() => FalloutNifSurfaceInputs.RequireParallaxInputs(data with { Tangents = [] }, true, true, true));
        Reject(() => FalloutNifSurfaceInputs.RequireParallaxInputs(data with { TextureCoordinates = [] }, true, true, true));
        Console.WriteLine("OPENNV_NIF_SURFACE_INPUTS_PASS parallaxScale=true signedDirection=true neutralHeight=true sourcePrefix=true missingInputsRejected=true");
    }

    private static void Near(Vector2 value, Vector2 expected)
    {
        if (Vector2.Distance(value, expected) > .000001f) throw new Exception("Source parallax UV differs.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("An invalid surface input was admitted.");
    }
}
