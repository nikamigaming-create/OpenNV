using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

public partial class NativeActorPerformanceAudit
{
    private static void VerifyPreparedNpc(FalloutNpcPreparedGeometry prepared, RuntimeLiveContentSource content)
    {
        // Compare the handoff against direct source evaluation, including every
        // expression vector, rather than counts or a plausible-looking body.
        foreach (var part in prepared.Parts)
        {
            if (!content.TryRead(part.Part.ModelPath!, null, out var bytes, out _)) throw new FileNotFoundException(part.Part.ModelPath);
            var original = FalloutNifFile.Read(bytes);
            var egm = FalloutNpcAppearanceMorph.Resolve(content, part.Part, part.SelectedShape);
            var geometry = egm is null ? null : new FalloutNpcFaceGeometry(prepared.Appearance, part.Part, egm.Geometry, part.SelectedShape);
            var expressions = FalloutNpcFaceMorph.Resolve(content, prepared.Appearance, part.Part, egm?.Geometry, part.SelectedShape);
            foreach (var index in part.Geometry.Keys.Concat(part.Morphs.Keys).Distinct())
            {
                var shape = original.ReadGeometry(index);
                var mesh = original.ReadMeshData(shape.Data);
                var expected = geometry?.Apply(original, shape, mesh) ?? mesh;
                if (part.Geometry.TryGetValue(index, out var actual) && (actual.Block != expected.Block ||
                    !actual.Vertices.SequenceEqual(expected.Vertices) || !actual.Triangles.SequenceEqual(expected.Triangles)))
                    throw new InvalidDataException("Prepared NPC geometry differs from direct source evaluation.");
                if (part.Morphs.TryGetValue(index, out var morphs))
                {
                    var expectedMorphs = expressions!.Build(original, shape, expected);
                    if (!morphs.Keys.SequenceEqual(expectedMorphs.Keys) || morphs.Any(pair => !pair.Value.SequenceEqual(expectedMorphs[pair.Key])))
                        throw new InvalidDataException("Prepared NPC expression deltas differ from direct source evaluation.");
                }
            }
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            _ = FalloutContentWorkers.Run(() => FalloutNpcPreparedGeometry.Read(prepared.Appearance, content, cancellation.Token), cancellation.Token).GetAwaiter().GetResult();
            throw new InvalidOperationException("Cancelled NPC preparation was admitted.");
        }
        catch (OperationCanceledException) { }

        // Abandoning a partially assembled actor must free its detached native
        // skeleton and parts. It must never be available for publication.
        var nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        using (var incomplete = new RuntimeNativeNpc.Assembly(prepared, content, .0142875f, (_, _, _, _) => new StandardMaterial3D()))
        {
            if (incomplete.Advance() || incomplete.Advance()) throw new InvalidOperationException("Incomplete NPC was admitted.");
            try { incomplete.Take(); throw new InvalidDataException("Partial NPC was published."); }
            catch (InvalidOperationException) { }
        }
        if (Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) != nodes)
            throw new InvalidOperationException("Abandoned NPC assembly leaked native nodes.");
        GD.Print("OPENNV_NPC_PREPARATION_PASS sourceVectors=exact worker=true cancelled=true partialPublication=blocked abandonedNodes=freed");
    }
}
