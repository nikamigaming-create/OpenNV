using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class ClassicPlayerInventory
{
    internal static int Run(string classicRoot, string appearanceRoot)
    {
        var classic = Fallout1OwnedContentSource.LoadInstall(classicRoot);
        Console.WriteLine(JsonSerializer.Serialize(new { hudButtons = classic.EffectiveLogicalPaths("art/intrface/", ".frm")
            .Where(path => new[] { "cha", "pip", "map", "opti", "invbut" }.Any(prefix => Path.GetFileName(path.Replace('\\', '/')).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray() }));
        var names = Fallout1NativeLists.Read(classic.Read("proto/critters/critters.lst").Bytes);
        foreach (var name in names.Take(4))
        {
            var bytes = classic.Read("proto/critters/" + name).Bytes;
            Console.WriteLine(JsonSerializer.Serialize(new { prototype = name, bytes = bytes.Length,
                header = Enumerable.Range(0, Math.Min(12, bytes.Length / 4)).Select(index => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(index * 4))).ToArray() }));
        }
        foreach (var name in new[] { "hmjmps", "hfjmps" })
            foreach (var action in new[] { "aa", "ab" })
            {
                var path = $"art/critters/{name}{action}.frm";
                var bytes = classic.Read(path).Bytes;
                var frame = Fallout1NativeFrmReader.ReadFirstFrame(bytes);
                Console.WriteLine(JsonSerializer.Serialize(new { path, frame.StoredFps, frame.FramesPerDirection, frame.Width, frame.Height,
                    directionX = frame.DirectionX, directionY = frame.DirectionY, frameX = frame.FrameX, frameY = frame.FrameY }));
                if (action == "ab")
                    for (var rotation = 0; rotation < 6; rotation++)
                        Console.WriteLine(JsonSerializer.Serialize(new { path, rotation, offsets = Enumerable.Range(0, frame.FramesPerDirection)
                            .Select(index => Fallout1NativeFrmReader.ReadFrame(bytes, rotation, index)).Select(row => new[] { row.FrameX, row.FrameY }).ToArray() }));
            }
        var mapBytes = classic.Read("maps/v13ent.map").Bytes;
        var map = Fallout1NativeMapReader.Read(mapBytes);
        var graph = Fallout1NativeObjectGraphReader.Read(mapBytes, map, classic);
        Console.WriteLine(JsonSerializer.Serialize(new { map.EnteringTile, map.EnteringRotation, map.EnteringElevation,
            nearby = graph.TopLevelObjects.Where(row => Math.Abs(row.Tile % 200 - map.EnteringTile % 200) < 4 && Math.Abs(row.Tile / 200 - map.EnteringTile / 200) < 4)
                .Select(row => new { row.Tile, row.Pid, row.Flags, row.InstanceFlags, row.InstanceValues, row.Prototype.Subtype }) }));
        RuntimeLiveContentSource.Configure(appearanceRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        foreach (var armor in records.EffectiveRecords("ARMO"))
        {
            string Text(string signature)
            {
                var field = armor.ReadSubrecords().SingleOrDefault(row => row.Signature == signature);
                return field.Data.IsEmpty ? "" : FalloutDialogueTopic.Text(field.Data.Span);
            }
            var id = Text("EDID"); var name = Text("FULL");
            if (!new[] { "Vault", "Tribal", "Sorrow", "WhiteLeg", "DeadHorse" }.Any(term => id.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            Console.WriteLine(JsonSerializer.Serialize(new { armor = armor.FormKey.ToString(), id, name,
                male = Text("MODL"), female = Text("MOD3") }));
        }
        return 0;
    }
}
