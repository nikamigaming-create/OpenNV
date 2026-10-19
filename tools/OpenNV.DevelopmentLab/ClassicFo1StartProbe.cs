using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;

internal static class ClassicFo1StartProbe
{
    internal static int Run(string install)
    {
        using var source = Fallout1OwnedContentSource.LoadInstall(install);
        using var catalog = new ClassicMapCatalog(source, "fallout-1");
        var temporary = Path.Combine(Path.GetTempPath(), "opennv-fo1-start-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var signatures = new HashSet<string>();
            foreach (var premade in ClassicPremadeReader.Load(catalog.Campaign, path => catalog.Read(path, out _)))
            {
                var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, catalog.Campaign, catalog.ProfileId,
                    premade.Id, premade.GcdSha256, premade.BiographySha256, premade.PortraitSha256, premade.Character, null);
                var player = ClassicPlayerSession.Begin(catalog, choice);
                var initial = player.Initialization ?? throw new InvalidOperationException("FO1 Begin skipped its source program.");
                if (initial.Arrival is not { } arrival || player.Tile != arrival.TileY * 200 + arrival.TileX ||
                    player.Rotation != arrival.Rotation || player.Inventory.Carried.Count == 0 || initial.ExternalOwners.Count != 6)
                    throw new InvalidOperationException("FO1 source start omitted arrival, items or export ownership.");
                var before = Inventory(player); signatures.Add(before);
                player.Save(temporary);
                var restored = ClassicPlayerSession.Restore(catalog, temporary);
                if (Inventory(restored) != before || restored.Initialization?.Identity != initial.Identity)
                    throw new InvalidOperationException("FO1 starting items changed during restore.");
                var held = restored.Inventory.Carried.First(row => row.Definition.Weapon is not null);
                restored.Inventory.Equip(held.Id, "right"); restored.Inventory.Drop(held.Id); restored.Save(temporary);
                var dropped = ClassicPlayerSession.Restore(catalog, temporary);
                var ground = dropped.Inventory.GroundItems.Single(row => row.Origin == held.Origin);
                if (dropped.Inventory.Carried.Any(row => row.Origin == held.Origin))
                    throw new InvalidOperationException("Restore re-granted an item dropped from starting inventory.");
                dropped.Inventory.TakeGround(ground.Id); dropped.Inventory.Equip(ground.Id, "right"); dropped.Save(temporary);
                var equipped = ClassicPlayerSession.Restore(catalog, temporary);
                if (equipped.Inventory.Held?.Origin != held.Origin || Inventory(equipped) != before)
                    throw new InvalidOperationException("Source grant pickup/equipment failed restoration.");
                var saved = JsonSerializer.Deserialize<ClassicPlayerSave>(File.ReadAllBytes(temporary))!;
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(saved with { InitializationHash = new string('0', 64) }));
                try { _ = ClassicPlayerSession.Restore(catalog, temporary); throw new Exception("Changed initialization identity was accepted."); }
                catch (InvalidDataException) { }

                var path = initial.Program!;
                var script = catalog.Read(path, out _);
                var program = ClassicNativeIntReader.Read(script, path);
                var grants = program.Procedures["base_inventory"].Instructions;
                var firstCreate = grants.TakeWhile(row => row.Opcode != 0x80b7).ToArray();
                var pid = firstCreate.First(row => row.Opcode == 0xc001 && row.Operand == 4);
                var variation = (byte[])script.Clone();
                BinaryPrimitives.WriteInt32BigEndian(variation.AsSpan(pid.Offset + 2), 8);
                using var variant = new ClassicMapCatalog(new Variation(source, path, variation), catalog.Campaign);
                var changed = ClassicPlayerSession.Begin(variant, choice);
                if (changed.Inventory.Carried.Any(row => row.Object.Pid == 4) ||
                    changed.Inventory.Carried.Single(row => row.Object.Pid == 8).Amount != 2 || changed.Initialization!.Identity == initial.Identity)
                    throw new InvalidOperationException("Owned INT source variation did not control actual starting inventory.");
                Console.WriteLine($"OPENNV_FO1_START_PASS character={premade.Id} stacks={player.Inventory.Carried.Count} sourceGrants=true restore=true noDuplicate=true equip=true sourceVariation=true fixture=true gameplay=false");
            }
            if (signatures.Count != 3) throw new InvalidOperationException("Source-dependent premade inventories were collapsed.");
            return 0;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string Inventory(ClassicPlayerSession player) => JsonSerializer.Serialize(player.Inventory.Carried
        .OrderBy(row => row.Object.Pid).ThenBy(row => row.Origin.Serial)
        .Select(row => new { row.Origin, row.Amount, row.Object.InstanceValues }));

    private sealed class Variation(IFalloutClassicOwnedSource source, string path, byte[] bytes) : IFalloutClassicOwnedSource
    {
        public string ProfileId => source.ProfileId;
        public byte[] Read(string logical, out int index)
        {
            if (ClassicMapCatalog.Canonical(logical) == path) { index = -1; return bytes; }
            return source.Read(logical, out index);
        }
        public IReadOnlyList<string> EffectiveLogicalPaths(string prefix, string extension) => source.EffectiveLogicalPaths(prefix, extension);
        public void Dispose() { }
    }
}
