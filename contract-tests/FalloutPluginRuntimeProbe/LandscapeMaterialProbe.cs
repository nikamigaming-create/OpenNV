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
        var impactMaterials = FalloutLandscapeImpactMaterial.VertexMaterials(weights, [2, 5, 9]);
        if (impactMaterials[0] != 2 || impactMaterials[1] != 5 || impactMaterials[2] != 5 || impactMaterials[288] != 2)
            throw new Exception("LAND impact did not select the strongest source vertex layer.");
        var tieWeights = FalloutLandscapeMaterialInputs.Weights([Layer(0, new FalloutLandscapeOpacity(0, 0, .5f))]);
        if (FalloutLandscapeImpactMaterial.VertexMaterials(tieWeights, [2, 5])[0] != 2)
            throw new Exception("LAND impact weight tie did not retain the base slot.");
        var fullSlots = FalloutLandscapeMaterialInputs.Weights(Enumerable.Range(0, 6)
            .Select(index => Layer((ushort)index, new FalloutLandscapeOpacity(0, 0, index == 5 ? 1 : 0))).ToArray());
        if (FalloutLandscapeImpactMaterial.VertexMaterials(fullSlots, [2, 3, 4, 5, 6, 7, 19])[0] != 2)
            throw new Exception("LAND impact selector exceeded the observed six runtime slots.");
        void Vertex(float x, float y, int quadrant, int vertex)
        {
            if (FalloutLandscapeImpactMaterial.Vertex((0, 0), x, y) != (quadrant, vertex))
                throw new Exception($"LAND impact vertex differs at {x},{y}.");
        }
        Vertex(64, 64, 0, 17); // Even diagonal: first corner wins the three-way tie.
        Vertex(192, 64, 0, 1); // Odd diagonal uses the other corner order.
        Vertex(66, 62, 0, 1);
        Vertex(128.5f, .5f, 0, 1); // Source integer conversion before grid-boundary test.
        Vertex(2048, 2048, 0, 288);
        Vertex(2049, 2049, 0, 288); // Snapping crosses back into the preceding quadrant.
        Vertex(4096, 4096, 3, 288);
        if (FalloutLandscapeImpactMaterial.Vertex((-2, -3), -8192 + 64, -12288 + 64) != (0, 17))
            throw new Exception("Negative world coordinates changed the LAND vertex selection.");
        Reject(() => FalloutLandscapeImpactMaterial.Vertex((0, 0), float.NaN, 0));
        Reject(() => FalloutLandscapeImpactMaterial.Vertex((0, 0), 4097, 0));
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
        Console.WriteLine("OPENNV_LAND_COLLISION_MATERIAL_PASS sourceHavok=true mixedFaceRequiresPointQuery=true inactiveLayers=ignored");
        Console.WriteLine("OPENNV_LAND_IMPACT_POINT_PASS strongestWeight=true tieOrder=true alternatingDiagonals=true snappedQuadrant=true negativeWorld=true sourceSlotBound=true");
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
