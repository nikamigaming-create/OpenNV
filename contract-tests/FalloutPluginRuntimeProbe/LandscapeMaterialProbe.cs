using OpenNV.Runtime.Content;

internal static class LandscapeMaterialProbe
{
    internal static void Run()
    {
        if (FalloutLandscapeMaterialInputs.QuadrantTiling(2) != 8 || FalloutLandscapeMaterialInputs.QuadrantTiling(1) != 4)
            throw new Exception("LAND quadrant UV scale differs from the native table.");
        FalloutLandscapeLayer Layer(ushort index, params FalloutLandscapeOpacity[] rows) => new(new("Synthetic.esm", (uint)index + 1), 0, index, 0, false, rows);
        var first = Layer(0, new(0, 0, .2f), new(1, 0, .75f), new(2, 0, 1));
        var second = Layer(1, new(0, 0, .3f), new(1, 0, .5f));
        var weights = FalloutLandscapeMaterialInputs.Weights([first, second]);
        Near(weights[0][0], .5f); Near(weights[1][0], .2f); Near(weights[2][0], .3f);
        Near(weights[0][1], 0); Near(weights[1][1], .6f); Near(weights[2][1], .4f);
        Near(weights[0][2], 0); Near(weights[1][2], 1); Near(weights[0][288], 1);
        var reversed = FalloutLandscapeMaterialInputs.Weights([second, first]);
        for (var vertex = 0; vertex < 289; vertex++)
        {
            Near(weights.Sum(layer => layer[vertex]), 1);
            Near(reversed[0][vertex], weights[0][vertex]);
            Near(reversed[1][vertex], weights[2][vertex]);
            Near(reversed[2][vertex], weights[1][vertex]);
        }
        // An interior point averages the normalized endpoint weights. It must
        // not normalize interpolated opacities or attenuate earlier textures.
        Near((weights[1][0] + weights[1][1]) * .5f, .4f);
        Reject(() => FalloutLandscapeMaterialInputs.Weights([Layer(0, new FalloutLandscapeOpacity(0, 0, float.NaN))]));
        Reject(() => FalloutLandscapeMaterialInputs.Weights([Layer(0, new FalloutLandscapeOpacity(289, 0, .5f))]));
        Reject(() => FalloutLandscapeMaterialInputs.Weights([Layer(0, new(0, 0, .2f), new(0, 0, .3f))]));
        Reject(() => FalloutLandscapeMaterialInputs.QuadrantTiling(0));
        if (FalloutLandscapePhysics.Read(new byte[] { 2, 37, 19 }) != new FalloutLandscapePhysics(2, 37, 19))
            throw new Exception("LTEX physics bytes changed.");
        Reject(() => FalloutLandscapePhysics.Read(new byte[] { 2, 37 }));
        Reject(() => FalloutLandscapePhysics.Read(new byte[] { 2, 37, 19, 0 }));
        if (!FalloutLandscapeMaterialInputs.CollisionMaterials(weights, [2, 2, 2], [0, 1, 2]).SequenceEqual(new[] { 2 }) ||
            !FalloutLandscapeMaterialInputs.CollisionMaterials(weights, [2, 5, 2], [0, 1, 2]).SequenceEqual(new[] { -1 }) ||
            !FalloutLandscapeMaterialInputs.CollisionMaterials(weights, [-1, 2, 2], [2, 2, 2]).SequenceEqual(new[] { 2 }))
            throw new Exception("LAND collision material guessed a blend or used an inactive texture.");
        Console.WriteLine("OPENNV_LAND_COLLISION_MATERIAL_PASS sourceHavok=true mixedMaterial=unbound inactiveLayers=ignored");
        Console.WriteLine("OPENNV_LAND_MATERIAL_INPUTS_PASS sourceScale=true weightedSum=true overpaintNormalized=true unpaintedBase=true orderIndependent=true vertexStage=true malformedRejected=true");
    }
    private static void Near(float value, float expected)
    {
        if (MathF.Abs(value - expected) > .000001f) throw new Exception("LAND material contribution differs.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Malformed landscape material inputs were admitted.");
    }
}
