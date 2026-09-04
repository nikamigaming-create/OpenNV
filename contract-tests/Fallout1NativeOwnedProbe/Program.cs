using OpenNV.Runtime.Content;
using System.Text;

const int MiscObjectType = 5;
const int SceneryObjectType = 2;
const int GenericScenerySubtype = 5;
const int ExitGridValueCount = 4;
const int ScrollBlockerMessage = 1200;
const string ScrollBlockerSourceMessage = "{1200}{}{Scroll Blocker}";
const string ExitGridSourceMessage = "{1600}{}{Exit Grid}";

if (args.Length == 3 && args[0] == "register")
{
    var json = Fallout1OwnedContentSource.CreateProfileJson(args[1]);
    var destination = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllText(destination, json + Environment.NewLine);
    Console.WriteLine($"OPENNV_FO1_REGISTER_PASS profile={destination} bytes={json.Length}");
    return;
}
if (args.Length != 2 || args[0] != "audit")
    throw new InvalidOperationException("Usage: register <install-root> <profile.json> | audit <profile.json>");

var profilePath = Path.GetFullPath(args[1]);
var profileDirectory = Path.GetDirectoryName(profilePath)!;
var before = Snapshot(profileDirectory);
var source = Fallout1OwnedContentSource.Load(profilePath);
var installBefore = Snapshot(source.InstallRoot);
var mapResource = source.Read("maps\\v13ent.map");
var map = Fallout1NativeMapReader.Read(mapResource.Bytes);
var objectGraph = Fallout1NativeObjectGraphReader.Read(mapResource.Bytes, map, source);
var miscGroups = objectGraph.TopLevelObjects
    .Where(row => row.Prototype.ObjectType == MiscObjectType)
    .GroupBy(row => new
    {
        row.Pid,
        row.Prototype.MessageNumber,
        row.Prototype.Fid,
        row.Prototype.LogicalPath,
        row.Flags,
        row.ScriptId,
    })
    .OrderByDescending(group => group.Count())
    .Select(group => $"{group.Count()}x(pid={unchecked((uint)group.Key.Pid):x8}," +
        $"msg={group.Key.MessageNumber},proFid={group.Key.Fid:x8},pro={group.Key.LogicalPath}," +
        $"flags={group.Key.Flags:x8}," +
        $"script={group.Key.ScriptId:x8})")
    .ToArray();
var exitGridGroups = objectGraph.TopLevelObjects
    .Where(row => row.Prototype.ObjectType == MiscObjectType && row.InstanceValues.Count != 0)
    .GroupBy(row => string.Join(',', row.InstanceValues))
    .OrderBy(group => group.Key, StringComparer.Ordinal)
    .Select(group => $"{group.Count()}x({group.Key})")
    .ToArray();
var scriptedGroups = objectGraph.TopLevelObjects
    .Where(row => row.ScriptId != uint.MaxValue)
    .GroupBy(row => new { row.ScriptId, row.Pid, row.Prototype.ObjectType })
    .OrderBy(group => group.Key.ScriptId)
    .Select(group => $"{group.Count()}x(script={group.Key.ScriptId:x8}," +
        $"pid={unchecked((uint)group.Key.Pid):x8},type={group.Key.ObjectType})")
    .ToArray();
var liveScriptIds = map.LiveScripts.Select(row => row.ScriptId).ToHashSet();
var missingPlacedScriptIds = objectGraph.TopLevelObjects
    .Where(row => row.ScriptId != uint.MaxValue && !liveScriptIds.Contains(row.ScriptId))
    .Select(row => row.ScriptId)
    .Distinct()
    .Order()
    .ToArray();
var placedByScript = objectGraph.TopLevelObjects
    .Where(row => row.ScriptId != uint.MaxValue)
    .ToDictionary(row => row.ScriptId);
var scriptObjectIdMismatches = map.LiveScripts
    .Where(row => placedByScript.TryGetValue(row.ScriptId, out var placed) &&
        row.ObjectId is { } objectId && objectId != placed.ObjectId)
    .Select(row => $"{row.ScriptId:x8}:{row.ObjectId}->{placedByScript[row.ScriptId].ObjectId}")
    .ToArray();
var sceneryGameplayGroups = objectGraph.TopLevelObjects
    .Where(row => row.Prototype.ObjectType == SceneryObjectType &&
        row.Prototype.Subtype != GenericScenerySubtype)
    .GroupBy(row => new
    {
        row.Pid,
        row.Prototype.MessageNumber,
        row.Prototype.Fid,
        row.Prototype.Subtype,
        row.Prototype.LogicalPath,
        row.Flags,
        row.ScriptId,
    })
    .Select(group => $"{group.Count()}x(pid={unchecked((uint)group.Key.Pid):x8}," +
        $"msg={group.Key.MessageNumber},proFid={group.Key.Fid:x8},subtype={group.Key.Subtype}," +
        $"pro={group.Key.LogicalPath},flags={group.Key.Flags:x8},script={group.Key.ScriptId:x8})")
    .ToArray();
var unscriptedSecurityDoors = objectGraph.TopLevelObjects
    .Where(row => row.ScriptId == uint.MaxValue && row.Prototype.ObjectType == SceneryObjectType &&
        row.Prototype.Subtype == 0)
    .Select(row => $"serial={row.Serial},objectId={row.ObjectId},tile={row.Tile}," +
        $"fid={row.Fid:x8},flags={row.Flags:x8},instanceFlags={row.InstanceFlags:x8}," +
        $"instance={string.Join(',', row.InstanceValues)}")
    .ToArray();
var miscMessageNumbers = objectGraph.TopLevelObjects
    .Where(row => row.Prototype.ObjectType == MiscObjectType)
    .Select(row => row.Prototype.MessageNumber)
    .OfType<int>()
    .Distinct()
    .Order()
    .ToArray();
var protoMessageLines = Encoding.ASCII.GetString(source.Read("text\\english\\game\\pro_misc.msg").Bytes)
    .Replace("\r\n", "\n", StringComparison.Ordinal)
    .Split('\n')
    .Where(line => miscMessageNumbers.Any(number => line.StartsWith($"{{{number}}}", StringComparison.Ordinal)))
    .ToArray();
var sceneryMessageNumbers = objectGraph.TopLevelObjects
    .Where(row => row.Prototype.ObjectType == SceneryObjectType)
    .Select(row => row.Prototype.MessageNumber)
    .OfType<int>()
    .Distinct()
    .ToArray();
var sceneryMessageLines = Encoding.ASCII.GetString(source.Read("text\\english\\game\\pro_scen.msg").Bytes)
    .Replace("\r\n", "\n", StringComparison.Ordinal)
    .Split('\n')
    .Where(line => sceneryMessageNumbers.Any(number =>
        line.StartsWith($"{{{number}}}", StringComparison.Ordinal)))
    .ToArray();
if (missingPlacedScriptIds.Length != 0 || scriptObjectIdMismatches.Length != 0)
    throw new InvalidDataException("A placed Fallout 1 object does not match its live MAP script record.");
if (!protoMessageLines.Contains(ScrollBlockerSourceMessage, StringComparer.Ordinal) ||
    !protoMessageLines.Contains(ExitGridSourceMessage, StringComparer.Ordinal) ||
    objectGraph.TopLevelObjects.Any(row => row.Prototype.ObjectType == MiscObjectType &&
        row.Prototype.MessageNumber != ScrollBlockerMessage &&
        (!Fallout1NativeObjectGraphReader.IsExitGrid(row.Prototype) ||
         row.InstanceValues.Count != ExitGridValueCount)))
    throw new InvalidDataException("Fallout 1 misc semantic source labels or instance words differ.");
var entryTiles = map.Elevations[map.EnteringElevation];
var nonDefaultFloors = entryTiles.Count(value => (value & 0x0fffU) != 1U);
var nonDefaultRoofs = entryTiles.Count(value => ((value >> 16) & 0x0fffU) != 1U);
var prototype = Fallout1NativePrototypeReader.Resolve(source, map.FirstObjectPid);
var artPath = Fallout1NativePrototypeReader.ResolveArt(source, prototype.Fid);
var frmResource = source.Read(artPath);
var frame = Fallout1NativeFrmReader.ReadFirstFrame(frmResource.Bytes);
var palette = source.Read("color.pal");
var looseFrame = source.Read("art\\tiles\\grid000.frm");
if (!looseFrame.Source.StartsWith("loose:data:", StringComparison.Ordinal) ||
    Fallout1NativeFrmReader.ReadFirstFrame(looseFrame.Bytes).Width == 0)
    throw new InvalidDataException("Fallout 1 loose DATA precedence was not exercised.");
var critterPath = source.FirstArchiveMember("critter.dat");
var critterResource = source.Read(critterPath);
if (!critterResource.Source.StartsWith("dat1:critter.dat:", StringComparison.Ordinal))
    throw new InvalidDataException("Fallout 1 critter DAT precedence was not exercised.");
if (palette.Bytes.Length < 256 * 3)
    throw new InvalidDataException("Fallout 1 COLOR.PAL is truncated.");
var after = Snapshot(profileDirectory);
var installAfter = Snapshot(source.InstallRoot);
if (!before.SequenceEqual(after) || !installBefore.SequenceEqual(installAfter))
    throw new InvalidOperationException("The Fallout 1 audit changed profile or owned-install files.");
Console.WriteLine(
    $"OPENNV_FO1_NATIVE_PROBE_PASS profile={source.ProfileId} overlay={string.Join('>', source.OverlayOrder)} " +
    $"loose={source.LooseFileCount} map={map.Name} mapSource={mapResource.Source} " +
    $"pid=0x{unchecked((uint)map.FirstObjectPid):x8} pro={prototype.LogicalPath} " +
    $"fid=0x{prototype.Fid:x8} frm={artPath} frmSource={frmResource.Source} " +
    $"looseSource={looseFrame.Source} critterMember={critterPath} critterBytes={critterResource.Bytes.Length} " +
    $"floorPatches={nonDefaultFloors} roofPatches={nonDefaultRoofs} " +
    $"firstObjectTile={map.FirstObject.Tile} firstObjectRotation={map.FirstObject.Rotation} " +
    $"firstObjectScript=0x{map.FirstObject.ScriptId:x8} firstObjectInventory={map.FirstObject.InventoryLength} " +
    $"topLevelObjects={objectGraph.TotalTopLevelObjects} nestedObjects={objectGraph.NestedObjects} " +
    $"frame={frame.Width}x{frame.Height} preparedInputs=0 writes=0 " +
    "blockers=script-execution,destination-map-loading,general-input,palette-effects,gameplay");
Console.WriteLine($"OPENNV_FO1_MISC_GROUPS {string.Join(';', miscGroups)}");
Console.WriteLine($"OPENNV_FO1_EXIT_GRID_GROUPS {string.Join(';', exitGridGroups)}");
Console.WriteLine($"OPENNV_FO1_SCRIPTED_GROUPS {string.Join(';', scriptedGroups)}");
Console.WriteLine(
    $"OPENNV_FO1_SCRIPT_TABLE live={map.LiveScripts.Count} " +
    $"placed={objectGraph.TopLevelObjects.Count(row => row.ScriptId != uint.MaxValue)} " +
    $"missing={string.Join(',', missingPlacedScriptIds.Select(value => $"{value:x8}"))} " +
    $"objectIdMismatches={string.Join(',', scriptObjectIdMismatches)}");
Console.WriteLine($"OPENNV_FO1_SCENERY_GAMEPLAY_GROUPS {string.Join(';', sceneryGameplayGroups)}");
Console.WriteLine($"OPENNV_FO1_SECURITY_DOORS {string.Join(';', unscriptedSecurityDoors)}");
Console.WriteLine($"OPENNV_FO1_MISC_MESSAGES {string.Join(';', protoMessageLines)}");
Console.WriteLine($"OPENNV_FO1_SCENERY_MESSAGES {string.Join(';', sceneryMessageLines)}");

static string[] Snapshot(string root) => Directory.Exists(root)
    ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => $"{Path.GetRelativePath(root, path)}|{new FileInfo(path).Length}|" +
            $"{new FileInfo(path).LastWriteTimeUtc.Ticks}")
        .OrderBy(value => value, StringComparer.Ordinal).ToArray()
    : [];
