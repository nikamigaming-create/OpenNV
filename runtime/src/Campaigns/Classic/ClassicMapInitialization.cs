using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicMapInitializationObject(
    int Order,
    int SourceOffset,
    int Serial,
    int Elevation,
    int InventoryDepth,
    string Sid,
    int ScriptIndex);

internal sealed record ClassicMapScriptSlot(
    int Order,
    int Type,
    int Extent,
    int Slot,
    string Sid);

internal sealed record ClassicMapInitialization(
    IReadOnlyList<ClassicMapInitializationObject> Objects,
    IReadOnlyList<ClassicMapScriptSlot> ScriptSlots)
{
    internal IReadOnlyList<ClassicMapInitializationObject> ScriptedObjects =>
        Objects.Where(row => row.ScriptIndex >= 0).ToArray();
}

internal static class ClassicMapInitializationOwner
{
    internal static ClassicMapInitialization Parse(JsonElement map)
    {
        var objects = ParseObjects(map.GetProperty("objects"));
        var slots = ParseScriptSlots(map.GetProperty("scriptLists"));
        var liveSids = slots.Select(row => row.Sid).ToHashSet(StringComparer.Ordinal);
        var missing = objects
            .Where(row => row.ScriptIndex >= 0 && !liveSids.Contains(row.Sid))
            .Select(row => row.Sid)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException(
                $"Classic MAP scripted objects have no live script slot: {string.Join(", ", missing)}");
        return new ClassicMapInitialization(objects, slots);
    }

    private static IReadOnlyList<ClassicMapInitializationObject> ParseObjects(
        JsonElement source)
    {
        var rows = new List<ClassicMapInitializationObject>();
        var serials = new HashSet<int>();
        var offsets = new HashSet<int>();
        var previousElevation = -1;
        foreach (var elevationRow in source.GetProperty("elevations").EnumerateArray())
        {
            var elevation = elevationRow.GetProperty("elevation").GetInt32();
            if (elevation <= previousElevation)
                throw new InvalidOperationException(
                    "Classic MAP elevations are not in source order.");
            previousElevation = elevation;
            foreach (var obj in elevationRow.GetProperty("objects").EnumerateArray())
                Add(obj, elevation, 0);
        }
        if (rows.Count(row => row.InventoryDepth == 0) !=
            source.GetProperty("totalTopLevelObjects").GetInt32())
            throw new InvalidOperationException(
                "Classic MAP top-level initialization count drifted.");
        if (!rows.Select(row => row.SourceOffset).SequenceEqual(
                rows.Select(row => row.SourceOffset).Order()))
            throw new InvalidOperationException(
                "Classic MAP object source offsets are not in read order.");
        return rows;

        void Add(JsonElement obj, int elevation, int depth)
        {
            var serial = obj.GetProperty("serial").GetInt32();
            var sourceOffset = obj.GetProperty("sourceOffset").GetInt32();
            if (!serials.Add(serial) || !offsets.Add(sourceOffset) ||
                obj.GetProperty("elevation").GetInt32() != elevation)
                throw new InvalidOperationException(
                    $"Classic MAP object initialization identity drifted: {serial}.");
            rows.Add(new ClassicMapInitializationObject(
                rows.Count,
                sourceOffset,
                serial,
                elevation,
                depth,
                RequiredString(obj, "sid"),
                obj.GetProperty("scriptIndex").GetInt32()));
            foreach (var inventory in obj.GetProperty("inventory").EnumerateArray())
                Add(inventory.GetProperty("object"), elevation, checked(depth + 1));
        }
    }

    private static IReadOnlyList<ClassicMapScriptSlot> ParseScriptSlots(JsonElement source)
    {
        var rows = new List<ClassicMapScriptSlot>();
        var sids = new HashSet<string>(StringComparer.Ordinal);
        var previousType = -1;
        foreach (var list in source.EnumerateArray())
        {
            var type = list.GetProperty("type").GetInt32();
            if (type <= previousType)
                throw new InvalidOperationException(
                    "Classic MAP script lists are not in source type order.");
            previousType = type;
            var extents = list.GetProperty("extents").EnumerateArray().ToArray();
            if (extents.Length != list.GetProperty("extentCount").GetInt32())
                throw new InvalidOperationException(
                    $"Classic MAP script extent count drifted for type {type}.");
            var liveCount = 0;
            for (var extentIndex = 0; extentIndex < extents.Length; extentIndex++)
            {
                var extent = extents[extentIndex];
                if (extent.GetProperty("index").GetInt32() != extentIndex)
                    throw new InvalidOperationException(
                        $"Classic MAP script extent order drifted for type {type}.");
                var length = extent.GetProperty("length").GetInt32();
                var slots = extent.GetProperty("slots").EnumerateArray().ToArray();
                if (length < 0 || length > slots.Length)
                    throw new InvalidOperationException(
                        $"Classic MAP live script length drifted for type {type}.");
                for (var slotIndex = 0; slotIndex < length; slotIndex++)
                {
                    var slot = slots[slotIndex];
                    if (slot.GetProperty("slot").GetInt32() != slotIndex)
                        throw new InvalidOperationException(
                            $"Classic MAP live script slot order drifted for type {type}.");
                    var sid = RequiredString(slot, "sid");
                    if (!sids.Add(sid))
                        throw new InvalidOperationException(
                            $"Duplicate Classic MAP live script SID: {sid}.");
                    rows.Add(new ClassicMapScriptSlot(
                        rows.Count, type, extentIndex, slotIndex, sid));
                }
                liveCount += length;
            }
            if (liveCount != list.GetProperty("liveCount").GetInt32())
                throw new InvalidOperationException(
                    $"Classic MAP live script count drifted for type {type}.");
        }
        return rows;
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Classic MAP initialization string is empty: {property}.");
        return value;
    }
}
