using System.Globalization;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeLandscapeTransportAudit
{
    private async Task RunImpactPointAsync(string[] args)
    {
        if (args.Length != 7)
            throw new ArgumentException("Expected --impact-point ownedRoot cellRuntimeHex nativeX nativeY nativeZ expectedRawMaterial.");
        if (DisplayServer.GetName() != "headless")
            throw new ArgumentException("Impact-point audit requires the headless display server.");
        var point = new Vector3(Number(args[3]), Number(args[4]), Number(args[5]));
        if (!point.IsFinite()) throw new ArgumentException("Impact point must be finite.");
        var expected = uint.Parse(args[6], CultureInfo.InvariantCulture);
        RuntimeLiveContentSource.Configure(args[1], RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var cell = FalloutCellSceneReader.Read(records,
            records.RuntimeFormKey(uint.Parse(args[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture))).Cell;
        var world = cell.Worldspace ?? throw new ArgumentException("Impact audit requires an exterior CELL.");
        var persistent = new FalloutExteriorGrid(records).PersistentCell(world);
        var source = FalloutLandscapeTransportResolver.ResolveCell(records, cell, persistent);
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        var sourceX = point.X / units; var sourceY = -point.Z / units;
        var (quadrant, vertex) = FalloutLandscapeImpactMaterial.Vertex(source.ActiveCoordinates, sourceX, sourceY);
        var basis = source.BaseLayers.Single(layer => layer.Quadrant == quadrant);
        var layers = source.AlphaLayers.Where(layer => layer.Quadrant == quadrant).OrderBy(layer => layer.LayerIndex).ToArray();
        var weights = FalloutLandscapeMaterialInputs.Weights(layers);
        if (!basis.UsesQuadrantDefault || basis.Texture.ObjectId != 0 ||
            weights.Skip(1).Any(weight => weight[vertex] >= weights[0][vertex]))
            throw new InvalidDataException("Selected owned point does not uniquely exercise the default LAND base.");
        if (source.Textures[basis.Texture].Physics != new FalloutLandscapePhysics(2, 30, 30))
            throw new InvalidDataException("Default LAND lost its qualified physical declaration.");

        RuntimeNativeLandscapeTransport? land = null;
        var textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var assembly = new RuntimeNativeLandscapeTransportBuilder.Assembly(source, units, textures);
            while (!assembly.Advance()) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            land = assembly.Take(); AddChild(land);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            using var query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 2, point + Vector3.Down * 2, 1);
            using var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0 || !hit.TryGetValue("position", out var position) ||
                position.AsVector3().DistanceTo(point) > .02f)
                throw new InvalidDataException("Recorded owned LAND point did not reproduce its native collision height.");
            var actual = NativeNifCollisionBuilder.HitMaterial(hit);
            var direct = land.ImpactMaterials.At(sourceX, sourceY);
            if (actual != expected || direct != expected)
                throw new InvalidDataException($"Owned default LAND material differs: native={actual}, direct={direct}, expected={expected}.");
            var renderedFrames = Engine.GetFramesDrawn();
            if (renderedFrames != 0) throw new InvalidDataException("Headless impact audit unexpectedly rendered frames.");
            GD.Print("OPENNV_OWNED_DEFAULT_LAND_IMPACT_PASS " + JsonSerializer.Serialize(new
            {
                cell = cell.FormKey.ToString(),
                landscape = source.Landscape.ToString(),
                quadrant,
                vertex,
                baseWeight = weights[0][vertex],
                actual,
                direct,
                physical = source.Textures[basis.Texture].Physics,
                point = new[] { point.X, point.Y, point.Z },
                collisionPoint = new[] { position.AsVector3().X, position.AsVector3().Y, position.AsVector3().Z },
                sources = records.Plugins.Select(plugin => new { plugin.Plugin.Name, plugin.Sha256 }),
                nativeCollision = true,
                ordinaryShotEffects = "unverified",
                renderedFrames,
                retailParity = "unverified",
            }));
        }
        finally
        {
            land?.Free();
            foreach (var texture in textures.Values.Distinct()) texture.Dispose();
        }

        static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    }
}
