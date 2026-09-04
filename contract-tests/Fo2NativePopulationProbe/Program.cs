using OpenNV.Runtime.Campaigns.Fallout2.Native;

if (args.Length != 1)
    throw new InvalidOperationException("Provide one registered Fallout 2 owned profile.");
using var source = Fo2NativeOwnedSource.Load(args[0]);
var ledger = Fo2NativePopulationLedger.Build(source);
var map3Bytes = source.Read("maps\\arcaves.map", out var map3Archive);
var map3 = Fo2NativeMapReader.Read(map3Bytes);
var map3Graph = Fo2NativeMap3ObjectGraphReader.Read(map3Bytes, map3, source);
var unsupported = ledger.Maps.Where(row => row.Unsupported is not null).ToArray();
Console.WriteLine(
    $"OPENNV_FO2_NATIVE_POPULATION_LEDGER profile={ledger.SourceProfileId} " +
    $"maps={ledger.Maps.Count} elevations={ledger.PresentElevations} " +
    $"scriptSlots={ledger.ScriptRecords} liveScripts={ledger.LiveScripts} topLevel={ledger.TopLevelObjects} " +
    $"inventory={ledger.InventoryObjects} full={ledger.FullLayoutObjects} " +
    $"compact={ledger.CompactLayoutObjects} uniquePids={ledger.UniquePids} " +
    $"validatedPros={ledger.ValidatedPros} nonProPids={ledger.NonProPids} " +
    $"unsupported={ledger.UnsupportedMaps} " +
    $"types={string.Join(',', ledger.ObjectsByType.OrderBy(row => row.Key).Select(row => $"{row.Key}:{row.Value}"))} " +
    "preparedInputs=0 writes=0");
foreach (var row in unsupported)
    Console.WriteLine($"OPENNV_FO2_NATIVE_POPULATION_UNSUPPORTED map={row.LogicalPath} reason={row.Unsupported}");
foreach (var row in ledger.Maps.Where(row => row.Unsupported is null))
    Console.WriteLine(
        $"OPENNV_FO2_NATIVE_POPULATION_MAP path={row.LogicalPath} index={row.MapIndex} " +
        $"name={row.Name} elevations={row.PresentElevations} " +
        $"objects={string.Join(',', row.TopLevelObjectsByElevation.OrderBy(value => value.Key).Select(value => $"{value.Key}:{value.Value}"))} " +
        $"inventory={row.InventoryObjects} scriptSlots={row.ScriptRecords} liveScripts={row.LiveScripts} " +
        $"pids={row.UniquePids} pros={row.ValidatedPros}");
Console.WriteLine(
    $"OPENNV_FO2_NATIVE_MAP3_OBJECT_GRAPH archive={map3Archive} " +
    $"topLevel={map3Graph.TotalTopLevelObjects} inventory={map3Graph.NestedObjects} " +
    $"scriptSlots={map3Graph.ScriptSlots} liveScripts={map3Graph.LiveScripts} " +
    $"types={string.Join(',', map3Graph.TopLevelObjects.GroupBy(row => row.Prototype.ObjectType).OrderBy(row => row.Key).Select(row => $"{row.Key}:{row.Count()}"))} " +
    "scripts=fail-closed interactions=fail-closed preparedInputs=0 writes=0");
if (ledger.Maps.Count == 0 || ledger.PresentElevations == 0)
    throw new InvalidOperationException("The owned Fallout 2 population denominator is empty.");
if (unsupported.Length != 0)
    throw new NotSupportedException(
        $"{unsupported.Length} Fallout 2 MAP layouts remain unsupported; see ledger rows above.");
var map3Row = ledger.Maps.Single(row => row.MapIndex == 3 && row.Unsupported is null);
if (map3Graph.TotalTopLevelObjects != map3Row.TopLevelObjects ||
    map3Graph.NestedObjects != map3Row.InventoryObjects ||
    map3Graph.ScriptSlots != map3Row.ScriptRecords ||
    map3Graph.LiveScripts != map3Row.LiveScripts ||
    map3Graph.EndOffset != map3Bytes.Length)
    throw new InvalidOperationException("The detailed Map 3 graph differs from the population denominator.");
