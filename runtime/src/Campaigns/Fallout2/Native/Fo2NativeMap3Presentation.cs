using System.Security.Cryptography;
using System.Text;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Native;

internal sealed record Fo2NativeMap3Coverage(
    Node3D Root,
    string SourceProfileId,
    string MapSha256,
    int MapIndex,
    int Elevation,
    int ArrivalTile,
    int FloorPatches,
    int FloorMeshes,
    int FloorFrmResources,
    int ObjectFrmResources,
    int PresentedObjects,
    int SemanticObjects,
    int UnclassifiedObjects,
    int NestedInventoryObjects,
    int ScriptSlots,
    int LiveScripts,
    string SemanticBuckets)
{
    internal int FrmResources => FloorFrmResources;
}

internal static class Fo2NativeMap3Presentation
{
    private const string MapLogicalPath = "maps\\arcaves.map";
    private const string TilesListLogicalPath = "art\\tiles\\tiles.lst";
    private const string PaletteLogicalPath = "color.pal";
    private const int Map3Index = 3;
    private const int Fallout2MapVersion = 20;
    private const int Map3Elevation = 0;
    private const int Map3TempleArrivalTile = 28707;
    private const int DefaultFloorTileId = 1;
    private const uint FloorTileIdMask = 0x0fffU;
    private const int RoofTileIdShift = 16;
    private const int PaletteEntries = 256;
    private const int PaletteChannels = 3;
    private const int OutputChannels = 4;
    private const int MaximumSixBitChannel = 63;
    private const int SixBitScale = 4;
    private const float CameraHeightMeters = 34.0f;
    private const float CameraDepthOffsetMeters = 24.0f;
    private const float CameraFovDegrees = 38.0f;
    private const float AlphaScissorThreshold = 0.01f;
    private const float PixelsPerMeter = 43.11464576045433f;
    private const float GroundAnchorMeters = 0.015f;
    private const uint HiddenObjectFlag = 0x00000001U;
    private const int ObjectTypeShift = 24;
    private const uint ObjectTypeMask = 0x0fU;
    private const int ItemObjectType = 0;
    private const int CritterObjectType = 1;
    private const int SceneryObjectType = 2;
    private const int WallObjectType = 3;
    private const int TileObjectType = 4;
    private const int MiscObjectType = 5;
    private const int GenericScenerySubtype = 5;

    internal static Fo2NativeMap3Coverage Build(Node3D parent, Fo2NativeOwnedSource source)
    {
        var mapBytes = source.Read(MapLogicalPath, out var mapArchive);
        var map = Fo2NativeMapReader.Read(mapBytes);
        if (map.Version != Fallout2MapVersion || map.MapIndex != Map3Index ||
            !map.Name.Equals("ARCAVES.MAP", StringComparison.OrdinalIgnoreCase) ||
            !map.Elevations.TryGetValue(Map3Elevation, out var entries) ||
            entries.Length != Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight)
            throw new InvalidDataException("The native Fallout 2 launch requires exact Map 3 elevation 0.");
        var floorIds = entries.Select(value => (int)(value & FloorTileIdMask)).ToArray();
        var roofIds = entries.Select(value =>
            (int)((value >> RoofTileIdShift) & FloorTileIdMask)).ToArray();
        if (roofIds.Any(id => id != DefaultFloorTileId))
            throw new NotSupportedException(
                "Native Map 3 roof placement requires an exact height/cutaway contract.");

        var tileNames = Encoding.ASCII.GetString(source.Read(TilesListLogicalPath, out _))
            .Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var palette = source.Read(PaletteLogicalPath, out _);
        if (palette.Length < PaletteEntries * PaletteChannels)
            throw new InvalidDataException("Fallout 2 COLOR.PAL is truncated.");

        var root = new Node3D { Name = "FO2_NATIVE_MAP_003_SOURCE_ROOT" };
        root.SetMeta("source_profile_id", source.ProfileId);
        root.SetMeta("source_map_sha256", Convert.ToHexString(SHA256.HashData(mapBytes)).ToLowerInvariant());
        root.SetMeta("source_map_archive_precedence_index", mapArchive);
        root.SetMeta("source_elevation", Map3Elevation);
        root.SetMeta("source_arrival_tile", Map3TempleArrivalTile);
        root.SetMeta("prepared_inputs", 0);
        root.SetMeta("content_writes", 0);
        root.SetMeta("object_pro_script_semantics", "unsupported-presentation-only");
        parent.AddChild(root);

        var floorRoot = new Node3D { Name = "MAP_3_ELEVATION_0_NATIVE_FLOOR_FRM" };
        root.AddChild(floorRoot);
        var floorPatches = 0;
        var floorMeshes = 0;
        var frmResources = 0;
        foreach (var group in Enumerable.Range(0, floorIds.Length)
                     .Where(index => floorIds[index] != DefaultFloorTileId)
                     .GroupBy(index => floorIds[index])
                     .OrderBy(group => group.Key))
        {
            if (group.Key < 0 || group.Key >= tileNames.Length ||
                string.IsNullOrWhiteSpace(tileNames[group.Key]))
                throw new InvalidDataException($"Map 3 floor tile {group.Key} is absent from tiles.lst.");
            var logicalPath = $"art\\tiles\\{tileNames[group.Key].Trim()}";
            var frame = Fo2NativeFrmReader.ReadFirstFrame(source.Read(logicalPath, out _));
            if (frame.Width == 0 || frame.Height == 0)
                throw new InvalidDataException($"Map 3 floor FRM has no first frame: {logicalPath}.");
            var image = Image.CreateFromData(
                frame.Width,
                frame.Height,
                false,
                Image.Format.Rgba8,
                ExpandPalette(frame.PaletteIndexes, palette));
            var texture = ImageTexture.CreateFromImage(image);
            var material = new StandardMaterial3D
            {
                AlbedoTexture = texture,
                AlbedoColor = Colors.White,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                AlphaScissorThreshold = AlphaScissorThreshold,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                TextureRepeat = false,
            };
            var indices = group.ToArray();
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new PlaneMesh
                {
                    Size = new Vector2(
                        Fo1HexMath.ColumnSpacingMeters * 2.0f,
                        Fo1HexMath.FlatToFlatMeters * 2.0f),
                    Material = material,
                },
                InstanceCount = indices.Length,
            };
            for (var instance = 0; instance < indices.Length; ++instance)
                multimesh.SetInstanceTransform(instance, new Transform3D(
                    Basis.Identity,
                    Fo1HexMath.FloorPatchCenter(indices[instance])));
            var mesh = new MultiMeshInstance3D
            {
                Name = $"NATIVE_FLOOR_FRM_{group.Key:D4}_{indices.Length}",
                Multimesh = multimesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            mesh.SetMeta("source_floor_tile_id", group.Key);
            mesh.SetMeta("source_logical_path", logicalPath);
            floorRoot.AddChild(mesh);
            floorPatches += indices.Length;
            floorMeshes++;
            frmResources++;
        }
        if (floorPatches == 0 || floorMeshes == 0)
            throw new InvalidDataException("Map 3 contains no supported non-default floor presentation.");

        var graph = Fo2NativeMap3ObjectGraphReader.Read(mapBytes, map, source);
        var objectRoot = new Node3D { Name = "MAP_3_NATIVE_STATIC_OBJECT_FRM" };
        root.AddChild(objectRoot);
        var decodedObjects = new Dictionary<string, (ImageTexture Texture, Fo2NativeFrmFrame Frame)>(
            StringComparer.OrdinalIgnoreCase);
        var semanticBuckets = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var presentedObjects = 0;
        foreach (var placed in graph.TopLevelObjects)
        {
            var blocker = AdmissionBlocker(placed);
            if (blocker is not null)
            {
                Increment(semanticBuckets, blocker);
                continue;
            }
            var fidType = (int)((placed.Fid >> ObjectTypeShift) & ObjectTypeMask);
            if (fidType != placed.Prototype.ObjectType)
                throw new InvalidDataException(
                    $"Map 3 object {placed.Serial} MAP FID type differs from its PRO type.");
            var logicalPath = Fo2NativeMap3ObjectGraphReader.ResolveArt(source, placed.Fid);
            var frameKey = $"{placed.Rotation}:{logicalPath}";
            if (!decodedObjects.TryGetValue(frameKey, out var decoded))
            {
                var frame = Fo2NativeFrmReader.ReadFirstFrame(
                    source.Read(logicalPath, out _), placed.Rotation);
                if (frame.FramesPerDirection != 1)
                {
                    Increment(semanticBuckets, "animated-frm");
                    continue;
                }
                var image = Image.CreateFromData(
                    frame.Width,
                    frame.Height,
                    false,
                    Image.Format.Rgba8,
                    ExpandPalette(frame.PaletteIndexes, palette));
                decoded = (ImageTexture.CreateFromImage(image), frame);
                decodedObjects.Add(frameKey, decoded);
            }
            var sprite = new Sprite3D
            {
                Name = $"MAP_3_OBJECT_{placed.Serial:D4}_SOURCE_FRM",
                Texture = decoded.Texture,
                PixelSize = 1.0f / PixelsPerMeter,
                Position = Fo1HexMath.Center(placed.Tile) + Vector3.Up * GroundAnchorMeters,
                Offset = new Vector2(
                    placed.PixelX + decoded.Frame.DirectionX + decoded.Frame.FrameX,
                    -(placed.PixelY + decoded.Frame.DirectionY + decoded.Frame.FrameY) +
                        decoded.Frame.Height / 2.0f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Shaded = false,
                DoubleSided = true,
                AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            };
            sprite.SetMeta("source_pid", $"{placed.Pid:x8}");
            sprite.SetMeta("source_fid", $"{placed.Fid:x8}");
            sprite.SetMeta("source_rotation", placed.Rotation);
            sprite.SetMeta("source_frm", logicalPath);
            objectRoot.AddChild(sprite);
            presentedObjects++;
        }
        var semanticObjects = semanticBuckets.Values.Sum();
        if (presentedObjects == 0 || presentedObjects + semanticObjects != graph.TotalTopLevelObjects)
            throw new InvalidDataException("Map 3 object classification is incomplete.");
        var buckets = string.Join(',', semanticBuckets.Select(row => $"{row.Key}:{row.Value}"));
        root.SetMeta("presented_objects", presentedObjects);
        root.SetMeta("semantic_objects", semanticObjects);
        root.SetMeta("unclassified_objects", 0);
        root.SetMeta("nested_inventory_objects", graph.NestedObjects);
        root.SetMeta("script_slots", graph.ScriptSlots);
        root.SetMeta("live_scripts", graph.LiveScripts);
        root.SetMeta("semantic_buckets", buckets);
        root.SetMeta("object_pro_script_semantics", "classified-fail-closed");

        var arrival = Fo1HexMath.Center(Map3TempleArrivalTile);
        var marker = new Node3D
        {
            Name = "MAP_3_NATIVE_TEMPLE_ARRIVAL_PRESENTATION_MARKER",
            Position = arrival,
        };
        root.AddChild(marker);
        var camera = new Camera3D
        {
            Name = "MAP_3_NATIVE_PRESENTATION_CAMERA",
            Position = arrival + new Vector3(0.0f, CameraHeightMeters, CameraDepthOffsetMeters),
            Fov = CameraFovDegrees,
            Current = true,
        };
        root.AddChild(camera);
        camera.LookAt(arrival, Vector3.Up);

        return new Fo2NativeMap3Coverage(
            root,
            source.ProfileId,
            root.GetMeta("source_map_sha256").AsString(),
            map.MapIndex,
            Map3Elevation,
            Map3TempleArrivalTile,
            floorPatches,
            floorMeshes,
            frmResources,
            decodedObjects.Count,
            presentedObjects,
            semanticObjects,
            0,
            graph.NestedObjects,
            graph.ScriptSlots,
            graph.LiveScripts,
            buckets);
    }

    private static string? AdmissionBlocker(Fo2NativeMapObject placed)
    {
        if (placed.Elevation != Map3Elevation) return "other-elevation";
        if ((placed.Flags & HiddenObjectFlag) != 0) return "source-hidden";
        if (placed.Tile < 0) return "off-grid";
        if (placed.ScriptId != uint.MaxValue) return "scripted-state";
        if (placed.Frame != 0) return "nonzero-frame";
        return placed.Prototype.ObjectType switch
        {
            ItemObjectType => null,
            CritterObjectType => "critter-animation-ai",
            SceneryObjectType when placed.Prototype.Subtype == GenericScenerySubtype => null,
            SceneryObjectType => "interactive-scenery",
            WallObjectType => null,
            TileObjectType => null,
            MiscObjectType => "misc-gameplay-marker",
            _ => "unsupported-object-type",
        };
    }

    private static void Increment(IDictionary<string, int> buckets, string name) =>
        buckets[name] = buckets.TryGetValue(name, out var count) ? count + 1 : 1;

    private static byte[] ExpandPalette(byte[] indexes, byte[] palette)
    {
        var output = new byte[checked(indexes.Length * OutputChannels)];
        for (var index = 0; index < indexes.Length; ++index)
        {
            var paletteIndex = indexes[index];
            var paletteOffset = paletteIndex * PaletteChannels;
            var outputOffset = index * OutputChannels;
            for (var channel = 0; channel < PaletteChannels; ++channel)
            {
                var value = palette[paletteOffset + channel];
                output[outputOffset + channel] = value <= MaximumSixBitChannel
                    ? checked((byte)(value * SixBitScale))
                    : (byte)0;
            }
            output[outputOffset + PaletteChannels] = paletteIndex == 0 ? (byte)0 : byte.MaxValue;
        }
        return output;
    }
}
