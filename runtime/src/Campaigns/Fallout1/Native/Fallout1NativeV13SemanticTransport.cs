using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Fallout1.Native;

internal sealed record Fallout1NativeV13SemanticCoverage(
    int SemanticObjects,
    int ScrollBlockers,
    int ExitGrids,
    int SecurityDoors,
    int ScriptBoundObjects,
    int LiveMapScripts,
    int UnboundLiveMapScripts,
    string Buckets,
    Fallout1NativeV13InteractionRuntime Interactions);

internal static class Fallout1NativeV13SemanticTransport
{
    private const int MiscObjectType = 5;
    private const int SceneryObjectType = 2;
    private const int DoorScenerySubtype = 0;
    private const int ScrollBlockerMessage = 1200;
    private const int SecurityDoorMessage = 800;
    private const int ExitGridValueCount = 4;
    private const int DestinationMapValueIndex = 0;
    private const int DestinationTileValueIndex = 1;
    private const int DestinationElevationValueIndex = 2;
    private const int DestinationRotationValueIndex = 3;
    private const int WorldMapIndex = -2;
    private const int AreaExitMapIndex = -1;
    private const int ScriptResolvedTile = -1;
    private const int MapTileCount = 40000;
    private const int MaximumElevation = 2;
    private const int MaximumRotation = 5;

    internal static Fallout1NativeV13SemanticCoverage Build(
        Node3D presentationRoot,
        Fallout1NativeMap map,
        Fallout1NativeObjectGraph graph)
    {
        var root = new Node { Name = "V13ENT_NATIVE_NONVISUAL_METADATA" };
        presentationRoot.AddChild(root);
        var liveScripts = map.LiveScripts.ToDictionary(record => record.ScriptId);
        var boundScriptIds = new HashSet<uint>();
        var scrollBlockers = 0;
        var exitGrids = 0;
        var securityDoors = 0;
        var scriptBoundObjects = 0;
        var scrollBlockerRecords = new List<Fallout1NativeMapObject>();
        var resolvedExitGridRecords = new List<Fallout1NativeMapObject>();
        Fallout1NativeMapObject? securityDoorRecord = null;

        foreach (var placed in graph.TopLevelObjects)
        {
            if (placed.ScriptId != uint.MaxValue)
            {
                if (!liveScripts.TryGetValue(placed.ScriptId, out var script) ||
                    script.ObjectId != placed.ObjectId || !boundScriptIds.Add(placed.ScriptId))
                    throw new InvalidDataException(
                        $"V13ENT object {placed.Serial} does not match one unique live MAP script record.");
                AddScriptMetadata(root, placed, script);
                scriptBoundObjects++;
                continue;
            }
            if (placed.Prototype.ObjectType == MiscObjectType)
            {
                if (placed.Prototype.MessageNumber == ScrollBlockerMessage)
                {
                    if (placed.InstanceValues.Count != 0)
                        throw new InvalidDataException("V13ENT Scroll Blocker has unexpected instance values.");
                    AddPlacedMetadata(root, placed, "scroll-blocker");
                    scrollBlockerRecords.Add(placed);
                    scrollBlockers++;
                    continue;
                }
                if (Fallout1NativeObjectGraphReader.IsExitGrid(placed.Prototype))
                {
                    AddExitGridMetadata(root, placed);
                    if (placed.InstanceValues[DestinationMapValueIndex] >= 0)
                        resolvedExitGridRecords.Add(placed);
                    exitGrids++;
                    continue;
                }
                throw new NotSupportedException(
                    $"V13ENT misc object {placed.Serial} has no evidenced semantic contract.");
            }
            if (placed.Prototype.ObjectType == SceneryObjectType &&
                placed.Prototype.Subtype == DoorScenerySubtype)
            {
                if (placed.Prototype.MessageNumber != SecurityDoorMessage ||
                    placed.InstanceValues.Count != 1 || placed.InstanceValues[0] != 0 ||
                    securityDoorRecord is not null)
                    throw new NotSupportedException(
                        $"V13ENT scenery object {placed.Serial} is not the evidenced Security Door contract.");
                var node = AddPlacedMetadata(root, placed, "security-door");
                node.SetMeta("source_door_instance_word", placed.InstanceValues[0]);
                securityDoorRecord = placed;
                securityDoors++;
            }
        }

        foreach (var script in map.LiveScripts.Where(row => !boundScriptIds.Contains(row.ScriptId)))
        {
            var node = new Node { Name = $"MAP_LIVE_SCRIPT_{script.ScriptId:x8}_UNBOUND" };
            AddScriptRecordMetadata(node, script);
            node.SetMeta("source_binding", "map-live-unbound-to-object");
            root.AddChild(node);
        }

        var semanticObjects = scrollBlockers + exitGrids + securityDoors + scriptBoundObjects;
        var buckets = string.Join(",", new[]
        {
            $"exit-grid-metadata:{exitGrids}",
            $"script-bound-object-metadata:{scriptBoundObjects}",
            $"scroll-blocker-metadata:{scrollBlockers}",
            $"security-door-metadata:{securityDoors}",
        });
        root.SetMeta("semantic_objects", semanticObjects);
        root.SetMeta("semantic_buckets", buckets);
        root.SetMeta("live_map_scripts", map.LiveScripts.Count);
        root.SetMeta("unbound_live_map_scripts", map.LiveScripts.Count - boundScriptIds.Count);
        var interactions = Fallout1NativeV13InteractionRuntime.Create(
            scrollBlockerRecords,
            securityDoorRecord ?? throw new InvalidDataException("V13ENT Security Door is absent."),
            resolvedExitGridRecords);
        presentationRoot.AddChild(interactions);
        return new Fallout1NativeV13SemanticCoverage(
            semanticObjects,
            scrollBlockers,
            exitGrids,
            securityDoors,
            scriptBoundObjects,
            map.LiveScripts.Count,
            map.LiveScripts.Count - boundScriptIds.Count,
            buckets,
            interactions);
    }

    private static Node AddPlacedMetadata(
        Node parent,
        Fallout1NativeMapObject placed,
        string category)
    {
        var node = new Node { Name = $"OBJECT_{placed.Serial:D4}_{category.ToUpperInvariant()}" };
        node.SetMeta("semantic_category", category);
        node.SetMeta("source_serial", placed.Serial);
        node.SetMeta("source_offset", placed.SourceOffset);
        node.SetMeta("source_object_id", placed.ObjectId);
        node.SetMeta("source_tile", placed.Tile);
        node.SetMeta("source_elevation", placed.Elevation);
        node.SetMeta("source_pid", $"{unchecked((uint)placed.Pid):x8}");
        node.SetMeta("source_fid", $"{placed.Fid:x8}");
        node.SetMeta("source_flags", $"{placed.Flags:x8}");
        node.SetMeta("source_instance_flags", $"{placed.InstanceFlags:x8}");
        node.SetMeta("source_pro_message", placed.Prototype.MessageNumber ?? -1);
        parent.AddChild(node);
        return node;
    }

    private static void AddExitGridMetadata(Node parent, Fallout1NativeMapObject placed)
    {
        if (placed.InstanceValues.Count != ExitGridValueCount)
            throw new InvalidDataException("V13ENT Exit Grid does not contain four destination words.");
        var map = placed.InstanceValues[DestinationMapValueIndex];
        var tile = placed.InstanceValues[DestinationTileValueIndex];
        var elevation = placed.InstanceValues[DestinationElevationValueIndex];
        var rotation = placed.InstanceValues[DestinationRotationValueIndex];
        var sentinel = map is WorldMapIndex or AreaExitMapIndex;
        if ((sentinel && tile != ScriptResolvedTile) ||
            (!sentinel && (map < 0 || tile is < 0 or >= MapTileCount)) ||
            elevation is < 0 or > MaximumElevation || rotation is < 0 or > MaximumRotation)
            throw new InvalidDataException("V13ENT Exit Grid destination words are outside the source contract.");
        var node = AddPlacedMetadata(parent, placed, "exit-grid");
        node.SetMeta("destination_map", map);
        node.SetMeta("destination_tile", tile);
        node.SetMeta("destination_elevation", elevation);
        node.SetMeta("destination_rotation", rotation);
        node.SetMeta("destination_kind", map switch
        {
            WorldMapIndex => "world-map",
            AreaExitMapIndex => "area-exit",
            _ => "map",
        });
    }

    private static void AddScriptMetadata(
        Node parent,
        Fallout1NativeMapObject placed,
        Fallout1NativeMapScriptRecord script)
    {
        var node = AddPlacedMetadata(parent, placed, "script-bound-object");
        node.SetMeta("source_script_id", $"{placed.ScriptId:x8}");
        AddScriptRecordMetadata(node, script);
        node.SetMeta("source_binding", "exact-object-id");
    }

    private static void AddScriptRecordMetadata(Node node, Fallout1NativeMapScriptRecord script)
    {
        node.SetMeta("script_list_type", script.ListType);
        node.SetMeta("script_extent", script.ExtentIndex);
        node.SetMeta("script_slot", script.SlotIndex);
        node.SetMeta("script_record_offset", script.SourceOffset);
        node.SetMeta("script_record_bytes", script.RecordBytes);
        node.SetMeta("script_program_index", script.ScriptProgramIndex);
        node.SetMeta("script_object_id", script.ObjectId ?? -1);
    }
}
