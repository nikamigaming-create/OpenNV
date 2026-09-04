using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Fallout1.Native;

internal static class Fallout1NativeV13PresentationNumericContracts
{
    // Product presentation policy. Owned MAP/FRM coordinates and palette indexes
    // remain authoritative; these values only map classic pixels into Godot metres.
    internal const float PresentationPixelsPerMeter = 43.11464576045433f;
    internal const float PresentationGroundAnchorMeters = 0.015f;
    internal const float PresentationCameraHeightMeters = 34.0f;
    internal const float PresentationCameraDepthMeters = 24.0f;
    internal const float PresentationCameraFovDegrees = 38.0f;
    internal const float PresentationAlphaScissorThreshold = 0.01f;
    internal const int PaletteEntries = 256;
    internal const int PaletteChannels = 3;
    internal const int OutputChannels = 4;
    internal const int MaximumSixBitChannel = 63;
    internal const int SixBitScale = 4;
    internal const float StatusMarginPixels = 18.0f;
    internal const int StatusFontSizePixels = 18;
}

internal sealed record Fallout1NativeV13Coverage(
    Node3D Root,
    string ProfileId,
    string MapSha256,
    int Elevation,
    int ArrivalTile,
    int FloorPatches,
    int FloorMeshes,
    int FloorFrmResources,
    int ObjectFrmResources,
    int AdmittedObjects,
    int DeferredObjects,
    int SemanticObjects,
    int UnclassifiedObjects,
    int NestedInventoryObjects,
    int LiveMapScripts,
    int UnboundLiveMapScripts,
    string SemanticBuckets,
    string FirstObjectFrm,
    Fallout1NativeV13InteractionRuntime Interactions);

internal static class Fallout1NativeV13Presentation
{
    private const string MapLogicalPath = "maps\\v13ent.map";
    private const string TilesListLogicalPath = "art\\tiles\\tiles.lst";
    private const string PaletteLogicalPath = "color.pal";
    private const string ExpectedMapName = "V13ENT.MAP";
    private const int DefaultTileId = 1;
    private const uint TileIdMask = 0x0fffU;
    private const int RoofTileShift = 16;
    private const uint ObjectHiddenFlag = 0x00000001U;
    private const int ObjectTypeShift = 24;
    private const uint ObjectTypeMask = 0x0fU;
    private const int ItemObjectType = 0;
    private const int CritterObjectType = 1;
    private const int SceneryObjectType = 2;
    private const int WallObjectType = 3;
    private const int TileObjectType = 4;
    private const int MiscObjectType = 5;
    private const int GenericScenerySubtype = 5;

    internal static Fallout1NativeV13Coverage Build(
        Node parent,
        Fallout1OwnedContentSource source,
        string? isolatedSavePath = null)
    {
        var mapResource = source.Read(MapLogicalPath);
        var map = Fallout1NativeMapReader.Read(mapResource.Bytes);
        if (!map.Name.Equals(ExpectedMapName, StringComparison.OrdinalIgnoreCase) ||
            !map.Elevations.TryGetValue(map.EnteringElevation, out var entries) ||
            entries.Length != Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight)
            throw new InvalidDataException("The native Fallout 1 presentation requires exact V13ENT entry elevation.");
        var floorIds = entries.Select(value => (int)(value & TileIdMask)).ToArray();
        var roofIds = entries.Select(value => (int)((value >> RoofTileShift) & TileIdMask)).ToArray();
        if (roofIds.Any(id => id != DefaultTileId))
            throw new NotSupportedException("V13ENT roof placement requires an exact height/cutaway contract.");
        var tileNames = Fallout1NativeLists.Read(source.Read(TilesListLogicalPath).Bytes);
        var palette = source.Read(PaletteLogicalPath).Bytes;
        if (palette.Length <
            Fallout1NativeV13PresentationNumericContracts.PaletteEntries *
            Fallout1NativeV13PresentationNumericContracts.PaletteChannels)
            throw new InvalidDataException("Fallout 1 COLOR.PAL is truncated.");

        var root = new Node3D { Name = "FO1_NATIVE_V13ENT_SOURCE_PRESENTATION" };
        root.SetMeta("source_profile_id", source.ProfileId);
        root.SetMeta("source_map_sha256",
            Convert.ToHexString(SHA256.HashData(mapResource.Bytes)).ToLowerInvariant());
        root.SetMeta("source_elevation", map.EnteringElevation);
        root.SetMeta("source_arrival_tile", map.EnteringTile);
        root.SetMeta("prepared_inputs", 0);
        root.SetMeta("content_writes", 0);
        root.SetMeta("presentation_scope", "exact-floor-plus-evidenced-static-object-subset");
        parent.AddChild(root);

        var floorRoot = new Node3D { Name = "V13ENT_NATIVE_FLOOR_FRM" };
        root.AddChild(floorRoot);
        var floorPatches = 0;
        var floorMeshes = 0;
        var floorFrmResources = 0;
        foreach (var group in Enumerable.Range(0, floorIds.Length)
                     .Where(index => floorIds[index] != DefaultTileId)
                     .GroupBy(index => floorIds[index])
                     .OrderBy(group => group.Key))
        {
            if (group.Key < 0 || group.Key >= tileNames.Count)
                throw new InvalidDataException($"V13ENT floor tile {group.Key} exceeds tiles.lst.");
            var logicalPath = $"art\\tiles\\{tileNames[group.Key]}";
            var frame = Fallout1NativeFrmReader.ReadFirstFrame(source.Read(logicalPath).Bytes);
            var material = Material(frame, palette);
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
            floorRoot.AddChild(new MultiMeshInstance3D
            {
                Name = $"V13ENT_FLOOR_FRM_{group.Key:D4}_{indices.Length}",
                Multimesh = multimesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            floorPatches += indices.Length;
            floorMeshes++;
            floorFrmResources++;
        }
        if (floorPatches == 0)
            throw new InvalidDataException("V13ENT has no admitted non-default floor patches.");

        var graph = Fallout1NativeObjectGraphReader.Read(mapResource.Bytes, map, source);
        var semantics = Fallout1NativeV13SemanticTransport.Build(root, map, graph);
        if (isolatedSavePath is not null)
            semantics.Interactions.BindOwnedDestinationSource(source, isolatedSavePath);
        var objectRoot = new Node3D { Name = "V13ENT_NATIVE_STATIC_OBJECT_FRM" };
        root.AddChild(objectRoot);
        var decodedFrames = new Dictionary<string, (ImageTexture Texture, Fallout1NativeFrmFrame Frame)>(
            StringComparer.OrdinalIgnoreCase);
        var deferred = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var admittedObjects = 0;
        string? firstObjectFrm = null;
        foreach (var placed in graph.TopLevelObjects)
        {
            var reason = AdmissionBlocker(placed, map.EnteringElevation);
            if (reason is not null)
            {
                Increment(deferred, reason);
                continue;
            }
            var fidType = (int)((placed.Fid >> ObjectTypeShift) & ObjectTypeMask);
            if (fidType != placed.Prototype.ObjectType)
                throw new InvalidDataException(
                    $"V13ENT object {placed.Serial} MAP FID type differs from its PRO type.");
            var logicalPath = Fallout1NativePrototypeReader.ResolveArt(source, placed.Fid);
            var frameKey = $"{placed.Rotation}:{logicalPath}";
            if (!decodedFrames.TryGetValue(frameKey, out var decoded))
            {
                var frame = Fallout1NativeFrmReader.ReadFirstFrame(
                    source.Read(logicalPath).Bytes,
                    placed.Rotation);
                if (frame.FramesPerDirection != 1)
                {
                    Increment(deferred, "animated-frm");
                    continue;
                }
                decoded = (Texture(frame, palette), frame);
                decodedFrames.Add(frameKey, decoded);
            }
            var sprite = new Sprite3D
            {
                Name = $"V13ENT_OBJECT_{placed.Serial:D4}_SOURCE_FRM",
                Texture = decoded.Texture,
                PixelSize = 1.0f /
                    Fallout1NativeV13PresentationNumericContracts.PresentationPixelsPerMeter,
                Position = Fo1HexMath.Center(placed.Tile) + Vector3.Up *
                    Fallout1NativeV13PresentationNumericContracts.PresentationGroundAnchorMeters,
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
            sprite.SetMeta("source_pid", $"{unchecked((uint)placed.Pid):x8}");
            sprite.SetMeta("source_fid", $"{placed.Fid:x8}");
            sprite.SetMeta("source_rotation", placed.Rotation);
            sprite.SetMeta("source_frm", logicalPath);
            objectRoot.AddChild(sprite);
            firstObjectFrm ??= logicalPath;
            admittedObjects++;
        }
        var deferredObjects = deferred.Values.Sum();
        if (admittedObjects + deferredObjects != graph.TotalTopLevelObjects || admittedObjects == 0 ||
            deferredObjects != semantics.SemanticObjects)
            throw new InvalidDataException("V13ENT object coverage accounting is incomplete.");
        var deferredBuckets = semantics.Buckets;
        root.SetMeta("admitted_objects", admittedObjects);
        root.SetMeta("deferred_objects", deferredObjects);
        root.SetMeta("semantic_objects", semantics.SemanticObjects);
        root.SetMeta("unclassified_objects", 0);
        root.SetMeta("nested_inventory_objects", graph.NestedObjects);
        root.SetMeta("deferred_buckets", deferredBuckets);

        var arrival = Fo1HexMath.Center(map.EnteringTile);
        var camera = new Camera3D
        {
            Name = "V13ENT_NATIVE_SOURCE_CAMERA",
            Position = arrival + new Vector3(
                0.0f,
                Fallout1NativeV13PresentationNumericContracts.PresentationCameraHeightMeters,
                Fallout1NativeV13PresentationNumericContracts.PresentationCameraDepthMeters),
            Fov = Fallout1NativeV13PresentationNumericContracts.PresentationCameraFovDegrees,
            Current = true,
        };
        root.AddChild(camera);
        camera.LookAt(arrival, Vector3.Up);
        AddStatus(
            root, floorPatches, floorMeshes, floorFrmResources, decodedFrames.Count,
            admittedObjects, deferredObjects, graph.NestedObjects, semantics.LiveMapScripts,
            semantics.UnboundLiveMapScripts, deferredBuckets);

        return new Fallout1NativeV13Coverage(
            root,
            source.ProfileId,
            root.GetMeta("source_map_sha256").AsString(),
            map.EnteringElevation,
            map.EnteringTile,
            floorPatches,
            floorMeshes,
            floorFrmResources,
            decodedFrames.Count,
            admittedObjects,
            deferredObjects,
            semantics.SemanticObjects,
            0,
            graph.NestedObjects,
            semantics.LiveMapScripts,
            semantics.UnboundLiveMapScripts,
            deferredBuckets,
            firstObjectFrm ?? string.Empty,
            semantics.Interactions);
    }

    private static string? AdmissionBlocker(Fallout1NativeMapObject placed, int elevation)
    {
        if (placed.Elevation != elevation) return "other-elevation";
        if ((placed.Flags & ObjectHiddenFlag) != 0) return "source-hidden";
        if (placed.Tile < 0) return "off-grid";
        if (placed.ScriptId != uint.MaxValue) return "scripted-state";
        if (placed.Frame != 0) return "nonzero-frame";
        return placed.Prototype.ObjectType switch
        {
            ItemObjectType => null,
            CritterObjectType => "critter-animation",
            SceneryObjectType when placed.Prototype.Subtype == GenericScenerySubtype => null,
            SceneryObjectType => "scenery-gameplay-subtype",
            WallObjectType => null,
            TileObjectType => null,
            MiscObjectType => "misc-gameplay-marker",
            _ => "unsupported-object-type",
        };
    }

    private static void Increment(IDictionary<string, int> buckets, string name) =>
        buckets[name] = buckets.TryGetValue(name, out var value) ? value + 1 : 1;

    private static StandardMaterial3D Material(Fallout1NativeFrmFrame frame, byte[] palette) => new()
    {
        AlbedoTexture = Texture(frame, palette),
        AlbedoColor = Colors.White,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
        AlphaScissorThreshold =
            Fallout1NativeV13PresentationNumericContracts.PresentationAlphaScissorThreshold,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        TextureRepeat = false,
    };

    private static ImageTexture Texture(Fallout1NativeFrmFrame frame, byte[] palette) =>
        ImageTexture.CreateFromImage(Image.CreateFromData(
            frame.Width,
            frame.Height,
            false,
            Image.Format.Rgba8,
            ExpandPalette(frame.PaletteIndexes, palette)));

    private static byte[] ExpandPalette(byte[] indexes, byte[] palette)
    {
        var output = new byte[checked(indexes.Length *
            Fallout1NativeV13PresentationNumericContracts.OutputChannels)];
        for (var index = 0; index < indexes.Length; ++index)
        {
            var paletteIndex = indexes[index];
            var paletteOffset = paletteIndex *
                Fallout1NativeV13PresentationNumericContracts.PaletteChannels;
            var outputOffset = index * Fallout1NativeV13PresentationNumericContracts.OutputChannels;
            for (var channel = 0;
                 channel < Fallout1NativeV13PresentationNumericContracts.PaletteChannels;
                 ++channel)
            {
                var value = palette[paletteOffset + channel];
                output[outputOffset + channel] = value <=
                    Fallout1NativeV13PresentationNumericContracts.MaximumSixBitChannel
                    ? checked((byte)(value *
                        Fallout1NativeV13PresentationNumericContracts.SixBitScale))
                    : (byte)0;
            }
            output[outputOffset + Fallout1NativeV13PresentationNumericContracts.PaletteChannels] =
                paletteIndex == 0 ? (byte)0 : byte.MaxValue;
        }
        return output;
    }

    private static void AddStatus(
        Node3D root,
        int floorPatches,
        int floorMeshes,
        int floorFrmResources,
        int objectFrmResources,
        int admittedObjects,
        int deferredObjects,
        int nestedInventoryObjects,
        int liveMapScripts,
        int unboundLiveMapScripts,
        string deferredBuckets)
    {
        var layer = new CanvasLayer { Name = "V13ENT_NATIVE_SCOPE_STATUS" };
        root.AddChild(layer);
        var label = new Label
        {
            Text = $"FALLOUT 1 — DIRECT OWNED V13ENT\n" +
                $"Floor: {floorPatches} patches / {floorMeshes} source FRMs ({floorFrmResources} decoded)\n" +
                $"Static objects: {admittedObjects} admitted / {deferredObjects} deferred " +
                $"({objectFrmResources} reusable FRMs)\n" +
                $"Nonvisual metadata: {deferredObjects}; unclassified: 0\n" +
                $"Nested inventory records: {nestedInventoryObjects} (not world placements)\n" +
                $"MAP scripts: {liveMapScripts} live / {unboundLiveMapScripts} unbound to objects\n" +
                $"Semantic buckets: {deferredBuckets}\n" +
                "Scripts, animated art, interactive scenery, critters, and gameplay remain fail-closed.",
            Position = new Vector2(
                Fallout1NativeV13PresentationNumericContracts.StatusMarginPixels,
                Fallout1NativeV13PresentationNumericContracts.StatusMarginPixels),
        };
        label.AddThemeFontSizeOverride(
            "font_size",
            Fallout1NativeV13PresentationNumericContracts.StatusFontSizePixels);
        layer.AddChild(label);
    }
}
