using Godot;
using System.Diagnostics;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseOwnedStaticAssembly(string game, string model)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException("Owned NIF is absent.", model);
        var source = FalloutNifFile.Read(bytes);
        var addonIndex = source.Blocks.Where(block => block.TypeName == "BSValueNode")
            .Select(block => source.ReadNode(block.Index).Value).FirstOrDefault(value => value is not null);
        if (addonIndex is { } addonValue)
        {
            var addon = FalloutAddonNodes.For(content).Get(addonValue.Value);
            var first = NativeOwnedNifCache.Read(content, addon.Model, out var firstSource);
            var second = NativeOwnedNifCache.Read(content, addon.Model, out var secondSource);
            if (!ReferenceEquals(first, second) || firstSource != secondSource)
                throw new InvalidDataException("Unchanged addon source did not reuse its decoded owner.");
            RuntimeNativeNifMeshBuilder.PrepareStaticGeometry(first, .0142875f, CancellationToken.None);
        }
        var directStarted = Stopwatch.GetTimestamp();
        var direct = RuntimeNativeNifMeshBuilder.Build(source, .0142875f, contentSource: content);
        var directMilliseconds = Stopwatch.GetElapsedTime(directStarted).TotalMilliseconds;
        RuntimeNativeNifScene? staged = null;
        try
        {
            var orphanBaseline = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            using (var cancelled = RuntimeNativeNifMeshBuilder.StaticAssembly.TryCreate(source, .0142875f, content)
                ?? throw new NotSupportedException("Selected model is not eligible for static incremental assembly."))
                _ = cancelled.Advance();
            if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != orphanBaseline)
                throw new InvalidDataException("Cancelled static NIF assembly left detached native nodes.");
            using var assembly = RuntimeNativeNifMeshBuilder.StaticAssembly.TryCreate(source, .0142875f, content, out var refusal)
                ?? throw new NotSupportedException($"Selected model is not eligible for static incremental assembly: {refusal}");
            var steps = 0; double maximumStep = 0;
            while (true)
            {
                var started = Stopwatch.GetTimestamp();
                var complete = assembly.Advance();
                maximumStep = Math.Max(maximumStep, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (complete) break;
                ++steps;
            }
            staged = assembly.Take();
            if (direct.Nodes != staged.Nodes || direct.Surfaces != staged.Surfaces || direct.Vertices != staged.Vertices ||
                direct.Triangles != staged.Triangles || direct.CollisionBodies != staged.CollisionBodies ||
                direct.CollisionShapes != staged.CollisionShapes || direct.CollisionTriangles != staged.CollisionTriangles)
                throw new InvalidDataException("Incremental NIF assembly changed source geometry or collision counts.");
            void Compare(Node first, Node second)
            {
                var generatedNames = first.Name.ToString().StartsWith('@') && second.Name.ToString().StartsWith('@');
                if (first.GetType() != second.GetType() || !generatedNames && first.Name != second.Name || first.GetChildCount() != second.GetChildCount() ||
                    first.GetMeta("opennv_nif_source_name", "").AsString() != second.GetMeta("opennv_nif_source_name", "").AsString() ||
                    first.GetMeta("opennv_source_model", "").AsString() != second.GetMeta("opennv_source_model", "").AsString() ||
                    first is Node3D a && second is Node3D b && (!a.Transform.IsEqualApprox(b.Transform) || a.Visible != b.Visible) ||
                    first is MeshInstance3D x && second is MeshInstance3D y && x.Mesh?.GetSurfaceCount() != y.Mesh?.GetSurfaceCount() ||
                    first is CollisionObject3D ca && second is CollisionObject3D cb &&
                        (ca.CollisionLayer != cb.CollisionLayer || ca.CollisionMask != cb.CollisionMask) ||
                    first is CollisionShape3D c && second is CollisionShape3D d && c.Shape?.GetClass() != d.Shape?.GetClass())
                    throw new InvalidDataException($"Incremental NIF hierarchy differs at {first.Name}.");
                if (first is MeshInstance3D left && second is MeshInstance3D right && left.Mesh is { } leftMesh && right.Mesh is { } rightMesh)
                    for (var surface = 0; surface < leftMesh.GetSurfaceCount(); ++surface)
                    {
                        var la = leftMesh.SurfaceGetArrays(surface); var ra = rightMesh.SurfaceGetArrays(surface);
                        if (!la[(int)Mesh.ArrayType.Vertex].AsVector3Array().SequenceEqual(ra[(int)Mesh.ArrayType.Vertex].AsVector3Array()) ||
                            !la[(int)Mesh.ArrayType.Index].AsInt32Array().SequenceEqual(ra[(int)Mesh.ArrayType.Index].AsInt32Array()))
                            throw new InvalidDataException($"Incremental NIF mesh differs at {first.Name} surface {surface}.");
                    }
                if (first is CollisionShape3D leftShape && second is CollisionShape3D rightShape)
                {
                    if (leftShape.Shape is ConcavePolygonShape3D lc && rightShape.Shape is ConcavePolygonShape3D rc &&
                        !lc.Data.SequenceEqual(rc.Data) || leftShape.Shape is ConvexPolygonShape3D lv && rightShape.Shape is ConvexPolygonShape3D rv &&
                        !lv.Points.SequenceEqual(rv.Points))
                        throw new InvalidDataException($"Incremental NIF collision data differs at {first.Name}.");
                }
                for (var index = 0; index < first.GetChildCount(); ++index) Compare(first.GetChild(index), second.GetChild(index));
            }
            Compare(direct.Root, staged.Root);
            var directParticles = direct.Root.FindChildren("*", "", true, false).OfType<RuntimeNifParticleSystem>().ToArray();
            var stagedParticles = staged.Root.FindChildren("*", "", true, false).OfType<RuntimeNifParticleSystem>().ToArray();
            var directControllers = direct.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
            var stagedControllers = staged.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
            if (directParticles.Length != stagedParticles.Length || directControllers.Length != stagedControllers.Length ||
                directParticles.Zip(stagedParticles).Any(pair => ReferenceEquals(pair.First, pair.Second) ||
                    pair.First.ActiveCount != pair.Second.ActiveCount || pair.First.BirthCount != pair.Second.BirthCount ||
                    pair.First.DeathCount != pair.Second.DeathCount || pair.First.EmissionEnabled != pair.Second.EmissionEnabled) ||
                directControllers.Zip(stagedControllers).Any(pair => ReferenceEquals(pair.First, pair.Second) ||
                    pair.First.SourceController != pair.Second.SourceController || pair.First.ActiveSequence != pair.Second.ActiveSequence ||
                    !pair.First.SequenceNames.Order(StringComparer.Ordinal).SequenceEqual(pair.Second.SequenceNames.Order(StringComparer.Ordinal))))
                throw new InvalidDataException("Incremental NIF changed or shared its particle/controller owners.");
            var placement = new Transform3D(Basis.Identity, new Vector3(7, 3, -2));
            _ = RuntimeNativeNifPrototype.PlaceOwnedRoot(direct.Root, source, placement);
            _ = RuntimeNativeNifPrototype.PlaceOwnedRoot(staged.Root, source, placement);
            Compare(direct.Root, staged.Root);
            if (steps == 0) throw new InvalidDataException("Selected NIF did not yield between source nodes.");
            var freshOwners = direct.Root.FindChildren("*", "", true, false)
                .Any(node => node is RuntimeNifControllerPlayer or RuntimeNifParticleSystem or NativeNifBillboard or RuntimeNifHingeJoint);
            GD.Print($"OPENNV_OWNED_STATIC_ASSEMBLY_PASS steps={steps} nodes={direct.Nodes} surfaces={direct.Surfaces} collisionShapes={direct.CollisionShapes} particles={directParticles.Length} controllers={directControllers.Length} freshOwners={freshOwners} directMs={directMilliseconds:F3} maxStepMs={maximumStep:F3} stagedCacheWarm=true gpuAndGameplayTiming=unverified");
        }
        finally { direct.Root.Free(); staged?.Root.Free(); }
    }
}
