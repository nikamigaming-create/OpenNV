using OpenNV.Runtime.Campaigns.Fallout1.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;

if (args.Length != 1)
    throw new InvalidOperationException("Provide one registered Fallout 1 owned profile.");
using var source = Fo1NativeOwnedSource.Load(args[0]);
var ledger = Fo2NativePopulationLedger.Build(source);
var unsupported = ledger.Maps.Where(row => row.Unsupported is not null).ToArray();
Console.WriteLine(
    $"OPENNV_FO1_NATIVE_POPULATION_LEDGER profile={ledger.SourceProfileId} " +
    $"maps={ledger.Maps.Count} elevations={ledger.PresentElevations} " +
    $"scriptSlots={ledger.ScriptRecords} liveScripts={ledger.LiveScripts} " +
    $"topLevel={ledger.TopLevelObjects} inventory={ledger.InventoryObjects} " +
    $"full={ledger.FullLayoutObjects} compact={ledger.CompactLayoutObjects} " +
    $"uniquePids={ledger.UniquePids} validatedPros={ledger.ValidatedPros} " +
    $"nonProPids={ledger.NonProPids} " +
    $"unsupported={ledger.UnsupportedMaps} " +
    $"types={string.Join(',', ledger.ObjectsByType.OrderBy(row => row.Key).Select(row => $"{row.Key}:{row.Value}"))} " +
    "preparedInputs=0 writes=0");
foreach (var row in ledger.Maps)
{
    if (row.Unsupported is not null)
    {
        Console.WriteLine(
            $"OPENNV_FO1_NATIVE_POPULATION_UNSUPPORTED map={row.LogicalPath} reason={row.Unsupported}");
        continue;
    }
    Console.WriteLine(
        $"OPENNV_FO1_NATIVE_POPULATION_MAP path={row.LogicalPath} index={row.MapIndex} " +
        $"name={row.Name} elevations={row.PresentElevations} " +
        $"objects={string.Join(',', row.TopLevelObjectsByElevation.OrderBy(value => value.Key).Select(value => $"{value.Key}:{value.Value}"))} " +
        $"inventory={row.InventoryObjects} scriptSlots={row.ScriptRecords} " +
        $"liveScripts={row.LiveScripts} pids={row.UniquePids} pros={row.ValidatedPros}");
}
if (ledger.Maps.Count == 0 || ledger.PresentElevations == 0)
    throw new InvalidOperationException("The owned Fallout 1 population denominator is empty.");
if (unsupported.Length != 0)
    throw new NotSupportedException(
        $"{unsupported.Length} Fallout 1 MAP layouts remain unsupported; see ledger rows above.");
