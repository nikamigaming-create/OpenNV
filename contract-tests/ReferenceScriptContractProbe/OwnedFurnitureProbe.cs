using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

internal static class OwnedFurnitureProbe
{
    internal static void Ttw(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var sources = new (uint Actor, uint Package)[]
        {
            (0x01ce25, 0x034200), (0x01ce27, 0x01ce30), (0x01ce29, 0x01ce32), (0x01ce2b, 0x01ce31),
            (0x01d029, 0x0200a8), (0x02b795, 0x075336), (0x034203, 0x075336)
        };
        var seats = new Dictionary<(FalloutFormKey, int), FalloutFormKey>();
        var snapshots = new Dictionary<FalloutFormKey, byte[]>();
        void Retain(FalloutPluginRecord record) => snapshots.TryAdd(record.FormKey, SHA256.HashData(record.ReadData()));
        foreach (var (actorId, packageId) in sources)
        {
            var actor = new FalloutFormKey("Fallout3.esm", actorId);
            var record = records.GetEffective(new("FalloutNV.esm", packageId)); Retain(record);
            Retain(records.GetEffective(actor));
            var source = FalloutFindFurniturePackage.Read(record);
            var cell = FalloutCellSceneReader.Read(records, world.Get(actor).Cell);
            world.LoadCell(cell);
            var enable = actor;
            var enabled = true;
            while (world.Get(enable).EnableParent is { } parent) { enabled ^= parent.Opposite; enable = parent.Reference; }
            world.SetEnabled(enable, enabled); world.AdvanceEnableChanges(0, new(1, 1), _ => false);
            var candidates = source.Candidates(records, world, actor, cell);
            if (candidates.Count == 0) throw new InvalidDataException("Original classroom Find has no eligible source furniture.");
            var selected = false;
            foreach (var candidate in candidates)
            {
                var furniture = records.GetEffective(candidate.Base); Retain(furniture);
                var path = cell.BaseObjects[candidate.Base].ModelPath ?? throw new InvalidDataException("Original classroom furniture has no model.");
                if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
                foreach (var seat in FalloutFurnitureSource.ReadSeats(records, furniture, FalloutNifFile.Read(bytes)))
                {
                    if (!world.ReserveFurnitureSeat(candidate.FormKey, seat.Index, actor)) continue;
                    if (!seats.TryAdd((candidate.FormKey, seat.Index), actor)) throw new InvalidDataException("Two students selected one source seat.");
                    selected = true; break;
                }
                if (selected) break;
            }
            if (!selected) throw new InvalidDataException("Original student has no distinct available source seat.");
            world.UnloadCell(cell.Cell.FormKey);
        }
        var historyActor = new FalloutFormKey("Fallout3.esm", 0x064914);
        Retain(records.GetEffective(historyActor));
        if (world.GetTalkedToPlayer(historyActor)) throw new InvalidDataException("Isolated original guard history started talked-to-player.");
        world.Get(historyActor).TalkedToPlayer = true;
        if (!world.GetTalkedToPlayer(historyActor)) throw new InvalidDataException("Original guard query did not use shared history.");
        foreach (var (id, target) in new (uint, uint)[] { (0x064e3b, 0x064e37), (0x0a2553, 0x0a254f) })
        {
            var caller = new FalloutFormKey("Fallout3.esm", id); Retain(records.GetEffective(caller));
            if (world.GetLinkedRef(caller) != new FalloutFormKey("Fallout3.esm", target))
                throw new InvalidDataException("Original escape linked reference lost its winning source-master adjustment.");
        }
        using var cold = new FalloutReferenceWorld(records); cold.Restore(world.Capture());
        if (!cold.GetTalkedToPlayer(historyActor) || cold.GetLinkedRef(new("Fallout3.esm", 0x064e3b)) != new FalloutFormKey("Fallout3.esm", 0x064e37))
            throw new InvalidDataException("Cold original reference history/link query changed its owner.");
        if (snapshots.Any(snapshot => !snapshot.Value.SequenceEqual(SHA256.HashData(records.GetEffective(snapshot.Key).ReadData()))))
            throw new InvalidDataException("Owned furniture audit changed source records.");
        Console.WriteLine($"OPENNV_OWNED_TTW_FURNITURE_PASS actors={sources.Length} seats={seats.Count} sourcePackages=true " +
            "sourceNifMarkers=true enabled=true ownership=true reservationsDistinct=true linkedSourceMasters=true historyCold=true sourceReadonly=true physicalArrivalAndCampaign=separate");
    }
}
