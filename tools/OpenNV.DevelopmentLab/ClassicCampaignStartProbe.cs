using System.Text.Json;
using System.Buffers.Binary;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;

internal static class ClassicCampaignStartProbe
{
    internal static int Run(string install, string contractPath)
    {
        using var source = Fo2NativeOwnedSource.LoadInstall(install);
        using var catalog = new ClassicMapCatalog(source, "fallout-2");
        using var contractDocument = JsonDocument.Parse(File.ReadAllBytes(contractPath));
        var contract = ClassicRetailRandomContract.Parse(contractDocument.RootElement);
        var temporary = Path.Combine(Path.GetTempPath(), "opennv-classic-start-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            foreach (var premade in ClassicPremadeReader.Load(catalog.Campaign, path => catalog.Read(path, out _)))
            {
                var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, catalog.Campaign, catalog.ProfileId,
                    premade.Id, premade.GcdSha256, premade.BiographySha256, premade.PortraitSha256, premade.Character, null);
                var player = ClassicPlayerSession.Begin(catalog, choice, contract);
                var initialization = player.Initialization ?? throw new InvalidOperationException("No source campaign initialization.");
                if (player.Tile != 17488 || player.Rotation != 5 || initialization.Light != 100 ||
                    player.Inventory.Carried is not [{ Object.Pid: 7, Amount: 1 }]) throw new InvalidOperationException("Owned Temple start result drifted.");
                var spear = player.Inventory.Carried.Single();
                player.Inventory.Equip(spear.Id, "right");
                player.Inventory.Drop(spear.Id); player.Save(temporary);
                var restored = ClassicPlayerSession.Restore(catalog, temporary, contract);
                if (restored.Inventory.Carried.Count != 0 || restored.Inventory.GroundItems.Count(row => row.Object.Pid == 7 && row.Origin.Serial < 0) != 1)
                    throw new InvalidOperationException("Reload duplicated the source starting grant.");
                var dropped = restored.Inventory.GroundItems.Single(row => row.Origin.Serial < 0);
                restored.Inventory.TakeGround(dropped.Id); restored.Inventory.Equip(dropped.Id, "right"); restored.Save(temporary);
                var cold = ClassicPlayerSession.Restore(catalog, temporary, contract);
                if (cold.Inventory.Carried is not [{ Object.Pid: 7, Amount: 1 }] || cold.Inventory.Held?.Object.Pid != 7)
                    throw new InvalidOperationException("Source-generated equipment did not survive cold restoration.");
                var save = JsonSerializer.Deserialize<ClassicPlayerSave>(File.ReadAllBytes(temporary))!;
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(save with { InitializationHash = new string('0', 64) }));
                try { _ = ClassicPlayerSession.Restore(catalog, temporary, contract); throw new InvalidOperationException("Changed source initialization was accepted."); }
                catch (InvalidDataException) { }
                Console.WriteLine($"PASS {premade.Character.Name}: owned startup/entry, arrival/light/grant, drop, cold restore without duplicate, pickup/equip and source hash rejection.");
            }
            // Verify the native exploration save migration retains position and
            // introduces the missing source grant, without rewriting owned MAP data.
            var first = ClassicPremadeReader.Load(catalog.Campaign, path => catalog.Read(path, out _))[0];
            var oldChoice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, catalog.Campaign, catalog.ProfileId,
                first.Id, first.GcdSha256, first.BiographySha256, first.PortraitSha256, first.Character, null);
            var exploration = new ClassicPlayerSession(catalog, catalog.StartingMap, oldChoice); exploration.Save(temporary);
            var migrated = ClassicPlayerSession.Restore(catalog, temporary, contract);
            if (migrated.Tile != exploration.Tile || migrated.Inventory.Carried is not [{ Object.Pid: 7 }]) throw new InvalidOperationException("Native exploration migration lost position or starting equipment.");
            var sourceProgram = catalog.Read("scripts/artemple.int", out _);
            var decoded = ClassicNativeIntReader.Read(sourceProgram, "scripts/artemple.int");
            if (decoded.InitialVariables[1] != 1 || decoded.StartupProcedure != "start") throw new InvalidOperationException("INT nonzero module initializers were lost.");
            // A synthetic loose-resource variation exercises the same complete
            // entry program; no donor identity or maintained grant list selects items.
            var variation = (byte[])sourceProgram.Clone();
            var grant = decoded.Procedures["Initial_Inven"].Instructions.ToArray();
            var createAt = Array.FindIndex(grant, row => row.Opcode == 0x80b7);
            var quantityAt = Array.FindIndex(grant, row => row.Opcode == 0x8116) - 1;
            var pidArgument = grant.Take(createAt).Single(row => row.Opcode == 0xc001 && row.Operand == 7);
            BinaryPrimitives.WriteInt32BigEndian(variation.AsSpan(pidArgument.Offset + 2), 8);
            BinaryPrimitives.WriteInt32BigEndian(variation.AsSpan(grant[quantityAt].Offset + 2), 2);
            using var changedCatalog = new ClassicMapCatalog(new ScriptVariation(source, variation), catalog.Campaign);
            var changedPlayer = ClassicPlayerSession.Begin(changedCatalog, oldChoice, contract);
            if (changedPlayer.Inventory.Carried is not [{ Object.Pid: 8, Amount: 2 }] ||
                changedPlayer.Initialization!.Identity == migrated.Initialization!.Identity)
                throw new InvalidOperationException("Source INT item/quantity override did not control the actual inventory.");
            foreach (var truncated in new[] { sourceProgram[..42], sourceProgram[..^1] })
            {
                try { _ = ClassicNativeIntReader.Read(truncated, "truncated"); throw new InvalidOperationException("Truncated INT was accepted."); }
                catch (InvalidDataException) { }
            }
            Console.WriteLine("PASS exploration-save migration, source INT item/quantity override, nonzero module initializer and truncated INT rejection.");
            return 0;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class ScriptVariation(IFalloutClassicOwnedSource source, byte[] script) : IFalloutClassicOwnedSource
    {
        public string ProfileId => source.ProfileId;
        public byte[] Read(string path, out int index)
        {
            if (ClassicMapCatalog.Canonical(path) == "scripts/artemple.int") { index = -1; return script; }
            return source.Read(path, out index);
        }
        public IReadOnlyList<string> EffectiveLogicalPaths(string prefix, string extension) => source.EffectiveLogicalPaths(prefix, extension);
        public void Dispose() { }
    }
}
