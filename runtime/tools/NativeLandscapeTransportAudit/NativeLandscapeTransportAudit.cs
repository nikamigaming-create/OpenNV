using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;

public partial class NativeLandscapeTransportAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 0 && args[0] == "--impact-point")
            {
                await RunImpactPointAsync(args);
                GetTree().Quit();
                return;
            }
            if (args.Length != 0 && args[0] == "--portal-capsule")
            {
                await RunPortalCapsuleAsync(args);
                GetTree().Quit();
                return;
            }
            if (args is not [var ownedRoot, var interiorId])
                throw new ArgumentException("Expected owned Data root and source interior CELL editor ID.");
            RuntimeLiveContentSource.Configure(ownedRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            var interior = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", interiorId).FormKey);
            var exit = FalloutDoorTransitionResolver.ResolveInteriorExits(records, interior).Single();
            var entry = exit.SourceDoor.Teleport!.Position;
            var diameter = checked((int)FalloutInstallationSettings.Read(content).Unsigned("General", "uGridsToLoad"));
            var grid = new FalloutExteriorGrid(records).Resolve(exit.DestinationWorldspace, exit.DestinationScene.Cell.FormKey, entry[0], entry[1], diameter);
            if (!grid.Scene.References.Any(reference => reference.FormKey == exit.DestinationDoor.FormKey) ||
                grid.Scene.Cell.Coordinates != ((int)MathF.Floor(entry[0] / 4096), (int)MathF.Floor(entry[1] / 4096)))
                throw new InvalidDataException("XTEL did not resolve its spatial CELL and reciprocal door.");
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(grid.Scene);
            if (world.ResidentInstances.Count() != grid.Scene.References.Count)
                throw new InvalidDataException("Spatial reference residency lost an authored child.");
            var globals = FalloutGlobalState.Read(records);
            var sky = new FalloutSkyLightingState(records, FalloutGameSettingFloats.Read(records, "fDaytimeColorExtension"));
            sky.EnterCell(grid.Scene.Cell, globals, entry);
            var snapshot = sky.Capture();
            var restored = new FalloutSkyLightingState(records, sky.DaytimeExtension);
            restored.Restore(snapshot); restored.EnterCell(grid.Scene.Cell, globals, entry);
            if (JsonSerializer.Serialize(snapshot) != JsonSerializer.Serialize(restored.Capture()))
                throw new InvalidDataException("Cold exterior sky changed its weather or random continuation.");
            var textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            var terrain = new List<RuntimeNativeLandscapeTransport>();
            foreach (var cell in grid.Cells)
            {
                var source = FalloutLandscapeTransportResolver.ResolveCell(records, cell, grid.PersistentCell);
                if (source is null) continue;
                var prepared = await FalloutContentWorkers.Run(() => RuntimeNativeLandscapeTransportBuilder.Prepare(source, units));
                using var assembly = new RuntimeNativeLandscapeTransportBuilder.Assembly(source, units, textureCache);
                var steps = 0;
                var maximumStep = 0d;
                while (true)
                {
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    var complete = assembly.Advance();
                    maximumStep = Math.Max(maximumStep, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    if (complete) break;
                    steps++;
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                if (steps < 6) throw new InvalidOperationException("LAND publication collapsed into an atomic cell callback.");
                var land = assembly.Take();
                // Compare prepared collision with Godot's mesh extraction,
                // independently of the new collision construction path.
                using var extracted = land.Geometry.Mesh.CreateTrimeshShape();
                if (extracted.Data.Length != prepared.Faces.Length || extracted.Data.Where((face, index) => face.DistanceTo(prepared.Faces[index]) > .001f).Any())
                {
                    var faces = extracted.Data;
                    var mismatch = Enumerable.Range(0, Math.Min(faces.Length, prepared.Faces.Length)).FirstOrDefault(i => faces[i] != prepared.Faces[i], -1);
                    var detail = mismatch < 0 ? "length" : $"index={mismatch} native={faces[mismatch]} prepared={prepared.Faces[mismatch]}";
                    land.Free();
                    throw new InvalidDataException($"Worker LAND faces differ native={faces.Length} prepared={prepared.Faces.Length} {detail}");
                }
                GD.Print($"OPENNV_LAND_PUBLICATION_PASS cell={cell.FormKey} steps={steps} maxStepMs={maximumStep:F3} workerFaces=true");
                terrain.Add(land); AddChild(land);
                if (!land.Geometry.Visible || Enumerable.Range(0, 4).Any(surface =>
                    land.Geometry.Mesh.SurfaceGetMaterial(surface) is not ShaderMaterial material ||
                    material.GetShaderParameter("base_texture").AsGodotObject() is not ImageTexture))
                    throw new InvalidDataException("LAND has no visible geometry or owned material binding.");
            }
            var environment = new RuntimeNativeExteriorEnvironment();
            environment.Configure(records, sky, () => 12f, units, shadowDistance: 300); AddChild(environment);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var sun = environment.GetChildren().OfType<DirectionalLight3D>().Single();
            if (!sun.ShadowEnabled || !sun.DirectionalShadowBlendSplits || sun.DirectionalShadowMaxDistance != 300)
                throw new InvalidDataException("Exterior sunlight discarded the resident-world shadow range or cascade blending.");
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var materialRays = 0;
            var blendedRays = 0;
            foreach (var land in terrain)
            {
                var source = land.Source;
                var point = new Vector3(source.ActiveCoordinates.X * 4096 + 16 * 128,
                    source.Heights[16 * 33 + 16], -(source.ActiveCoordinates.Y * 4096 + 16 * 128)) * units;
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 20, point + Vector3.Down * 20, 1));
                if (hit.Count == 0 || hit["normal"].AsVector3().Y <= 0 || Math.Abs(hit["position"].AsVector3().Y - point.Y) > .02f)
                    throw new InvalidDataException($"LAND {source.Landscape} has no upward collision at its authored height.");
                VerifyInteriorTriangleHeights(source, units);
                var shape = land.FindChildren("*", "", true, false).OfType<CollisionShape3D>().Single();
                var materials = shape.GetMeta("opennv_havok_face_materials").AsInt32Array();
                var faces = ((ConcavePolygonShape3D)shape.Shape).Data;
                if (materials.Length * 3 != faces.Length) throw new InvalidDataException("LAND physics/material face order disagrees.");
                var sourceMaterials = source.Textures.Values.Where(texture => texture.Physics.HasValue)
                    .Select(texture => (uint)texture.Physics!.Value.Material).ToHashSet();
                foreach (var material in materials.Where(value => value >= 0).Distinct())
                {
                    var face = Array.IndexOf(materials, material) * 3;
                    var center = shape.ToGlobal((faces[face] + faces[face + 1] + faces[face + 2]) / 3);
                    using var query = PhysicsRayQueryParameters3D.Create(center + Vector3.Up, center + Vector3.Down, 1);
                    var materialHit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                    // A uniform painted face is not an impact-material oracle:
                    // the runtime selector has its own source slot bound.
                    if (materialHit.Count == 0 || !sourceMaterials.Contains(NativeNifCollisionBuilder.HitMaterial(materialHit)))
                        throw new InvalidDataException("LAND ray did not select an owned LTEX material.");
                    materialRays++;
                }
                var blendedFaces = Enumerable.Range(0, materials.Length).Where(index => materials[index] < 0).Take(12);
                foreach (var face in blendedFaces)
                {
                    var offset = face * 3;
                    var center = shape.ToGlobal((faces[offset] + faces[offset + 1] + faces[offset + 2]) / 3);
                    using var query = PhysicsRayQueryParameters3D.Create(center + Vector3.Up, center + Vector3.Down, 1);
                    var materialHit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                    if (materialHit.Count == 0 || !sourceMaterials.Contains(NativeNifCollisionBuilder.HitMaterial(materialHit)))
                        throw new InvalidDataException("Blended LAND hit did not resolve an owned source material.");
                    blendedRays++;
                }
            }
            if (materialRays == 0) throw new InvalidOperationException("No owned LAND material query was exercised.");
            if (blendedRays == 0) throw new InvalidOperationException("No blended owned LAND impact was exercised.");
            GD.Print($"OPENNV_LAND_IMPACT_MATERIAL_PASS rays={materialRays} blendedRays={blendedRays} pointSelection=true retailParity=unverified");
            CheckLateEffects(content, environment, terrain[0].Geometry, units);
            GD.Print($"OPENNV_NATIVE_EXTERIOR_GRID_PASS active={grid.Scene.Cell.FormKey} cells={grid.Cells.Count} references={grid.Scene.References.Count} " +
                $"landCollision={terrain.Count} sharedTextures={textureCache.Count} weather={sky.ActiveWeather.Form} coldSky=true ordinaryDoor=unverified pixels=unverified");
            foreach (var land in terrain) land.Free();
            environment.Free();
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"OPENNV_NATIVE_EXTERIOR_GRID_FAIL {error}");
            GetTree().Quit(1);
        }
    }

    private void CheckLateEffects(RuntimeLiveContentSource content, RuntimeNativeExteriorEnvironment environment, MeshInstance3D terrain, float units)
    {
        var before = environment.RegisteredSurfaces;
        var roots = new List<Node3D>();
        var isolated = new SubViewport { OwnWorld3D = true }; AddChild(isolated);
        try
        {
            foreach (var path in new[] { "meshes/Projectiles/45CalCasing.NIF", "meshes/Effects/ImpactBallisticConcrete01.NIF" })
            {
                if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
                var root = NativeNifMeshBuilder.Build(bytes, units).Root; roots.Add(root); AddChild(root);
                foreach (var surface in root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>())
                {
                    if (surface is not (MeshInstance3D or MultiMeshInstance3D)) continue;
                    if (surface.GetInstanceShaderParameter("source_fog_range").AsVector3() != terrain.GetInstanceShaderParameter("source_fog_range").AsVector3())
                        throw new InvalidOperationException("Late source effect missed the active world fog binding.");
                    if (surface is MeshInstance3D mesh && mesh.GetActiveMaterial(0)?.ResourceName == NativeNifLightingMaterial.ResourceIdentity &&
                        mesh.GetInstanceShaderParameter("source_ambient").AsVector3() != terrain.GetInstanceShaderParameter("source_ambient").AsVector3())
                        throw new InvalidOperationException("Late casing missed the active world ambient binding.");
                }
            }
            var sample = roots[0].FindChildren("*", "", true, false).OfType<MeshInstance3D>().First();
            var privateMesh = new MeshInstance3D { Mesh = sample.Mesh }; isolated.AddChild(privateMesh);
            if (privateMesh.GetInstanceShaderParameter("source_fog_range").VariantType != Variant.Type.Nil)
                throw new InvalidOperationException("World lighting crossed a separate viewport boundary.");
        }
        finally { foreach (var root in roots) root.Free(); isolated.Free(); }
        if (environment.RegisteredSurfaces != before) throw new InvalidOperationException("Expired effects remained in the world lighting registry.");
        GD.Print("OPENNV_LATE_EFFECT_ENVIRONMENT_PASS casings=true particles=true isolatedViewport=preserved teardown=released");
    }

    private void VerifyInteriorTriangleHeights(FalloutLandscapeTransport source, float units)
    {
        const int side = FalloutLandscapeTransportResolver.VertexSide;
        const float u = .25f, v = .5f;
        for (var parity = 0; parity < 2; parity++)
        {
            var selectedX = parity;
            var selectedY = 0;
            var greatestDifference = -1f;
            for (var y = 0; y < side - 1; y++)
            {
                for (var x = 0; x < side - 1; x++)
                {
                    if (((x + y) & 1) != parity) continue;
                    var index = y * side + x;
                    var difference = MathF.Abs(source.Heights[index] + source.Heights[index + side + 1] -
                        source.Heights[index + 1] - source.Heights[index + side]) * u * units;
                    if (difference <= greatestDifference) continue;
                    greatestDifference = difference;
                    selectedX = x;
                    selectedY = y;
                }
            }
            var corner = selectedY * side + selectedX;
            var lowerLeft = source.Heights[corner];
            var lowerRight = source.Heights[corner + 1];
            var upperLeft = source.Heights[corner + side];
            var upperRight = source.Heights[corner + side + 1];
            // Independent native surface equations at u < v and u + v < 1.
            // Authored-vertex rays alone cannot distinguish the two diagonals.
            var expectedHeight = parity == 0
                ? lowerLeft + (upperLeft - lowerLeft) * v + (upperRight - upperLeft) * u
                : lowerLeft + (lowerRight - lowerLeft) * u + (upperLeft - lowerLeft) * v;
            var point = new Vector3(
                source.ActiveCoordinates.X * FalloutLandscapeTransportResolver.ExteriorCellSideGameUnits +
                (selectedX + u) * FalloutLandscapeTransportResolver.VertexSpacingGameUnits,
                expectedHeight,
                -(source.ActiveCoordinates.Y * FalloutLandscapeTransportResolver.ExteriorCellSideGameUnits +
                (selectedY + v) * FalloutLandscapeTransportResolver.VertexSpacingGameUnits)) * units;
            using var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 20, point + Vector3.Down * 20, 1);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count == 0 || hit["normal"].AsVector3().Y <= 0 ||
                MathF.Abs(hit["position"].AsVector3().Y - point.Y) > .02f)
                throw new InvalidDataException($"LAND {source.Landscape} interior triangle height disagrees at parity {parity}.");
            GD.Print($"OPENNV_LAND_INTERIOR_HEIGHT cell={source.ActiveCell} parity={parity} diagonalDifferenceMeters={greatestDifference}");
        }
    }
}
