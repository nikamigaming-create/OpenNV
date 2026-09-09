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
            var textureCache = new Dictionary<string, ImageTexture>(StringComparer.OrdinalIgnoreCase);
            var terrain = new List<RuntimeNativeLandscapeTransport>();
            foreach (var cell in grid.Cells)
            {
                var land = RuntimeNativeLandscapeTransportBuilder.Build(FalloutLandscapeTransportResolver.ResolveCell(records, cell, grid.PersistentCell), units, textureCache);
                terrain.Add(land); AddChild(land);
                if (!land.Geometry.Visible || Enumerable.Range(0, 4).Any(surface =>
                    land.Geometry.Mesh.SurfaceGetMaterial(surface) is not ShaderMaterial material ||
                    material.GetShaderParameter("base_texture").AsGodotObject() is not ImageTexture))
                    throw new InvalidDataException("LAND has no visible geometry or owned material binding.");
            }
            var environment = new RuntimeNativeExteriorEnvironment();
            environment.Configure(records, sky, () => 12f, units); AddChild(environment);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var materialRays = 0;
            foreach (var land in terrain)
            {
                var source = land.Source;
                var point = new Vector3(source.ActiveCoordinates.X * 4096 + 16 * 128,
                    source.Heights[16 * 33 + 16], -(source.ActiveCoordinates.Y * 4096 + 16 * 128)) * units;
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 20, point + Vector3.Down * 20, 1));
                if (hit.Count == 0 || hit["normal"].AsVector3().Y <= 0 || Math.Abs(hit["position"].AsVector3().Y - point.Y) > .02f)
                    throw new InvalidDataException($"LAND {source.Landscape} has no upward collision at its authored height.");
                var shape = land.FindChildren("*", "", true, false).OfType<CollisionShape3D>().Single();
                var materials = shape.GetMeta("opennv_havok_face_materials").AsInt32Array();
                var faces = ((ConcavePolygonShape3D)shape.Shape).Data;
                if (materials.Length * 3 != faces.Length) throw new InvalidDataException("LAND physics/material face order disagrees.");
                foreach (var material in materials.Where(value => value >= 0).Distinct())
                {
                    var face = Array.IndexOf(materials, material) * 3;
                    var center = shape.ToGlobal((faces[face] + faces[face + 1] + faces[face + 2]) / 3);
                    using var query = PhysicsRayQueryParameters3D.Create(center + Vector3.Up, center + Vector3.Down, 1);
                    var materialHit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                    if (materialHit.Count == 0 || NativeNifCollisionBuilder.HitMaterial(materialHit) != material)
                        throw new InvalidDataException("LAND ray lost its source texture's Havok material.");
                    materialRays++;
                }
            }
            if (materialRays == 0) throw new InvalidOperationException("No owned LAND material query was exercised.");
            GD.Print($"OPENNV_LAND_IMPACT_MATERIAL_PASS rays={materialRays} mixedMaterialSelection=unbound");
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
}
