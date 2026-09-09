using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;

internal static class ClassicMovementProbe
{
    internal static int Run(string root)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "opennv-classic-movement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
            void Reject(Action action, string reason)
            {
                try { action(); } catch (InvalidDataException) { return; }
                throw new InvalidOperationException(reason);
            }
            for (var tile = 0; tile < ClassicHexGrid.Count; tile++)
                foreach (var neighbor in ClassicHexGrid.Neighbors(tile))
                {
                    Require(ClassicHexGrid.Distance(tile, neighbor) == 1, "Hex direction crosses an edge incorrectly.");
                    Require(ClassicHexGrid.Neighbor(neighbor, (ClassicHexGrid.Rotation(tile, neighbor) + 3) % 6) == tile, "Opposite hex directions do not return to origin.");
                }
            var start = 20100; var first = ClassicHexGrid.Neighbor(start, 0); var second = ClassicHexGrid.Neighbor(first, 0);
            Require(ClassicHexGrid.Path(start, second, new HashSet<int> { start, first, second }).SequenceEqual(new[] { first, second }), "Straight path differs from the only legal corridor.");
            Require(ClassicHexGrid.Path(start, second, new HashSet<int> { start, second }).Length == 0, "Path crosses a missing intermediate tile.");

            var source = Fallout1OwnedContentSource.LoadInstall(root);
            var premades = ClassicPremadeReader.Load("fallout-1", path => source.Read(path).Bytes);
            foreach (var premade in premades)
            {
                var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, "fallout-1", source.ProfileId,
                    premade.Id, premade.GcdSha256, premade.BiographySha256, premade.PortraitSha256, premade.Character, null);
                var player = new ClassicPlayerSession(source, "maps/v13ent.map", choice);
                var origin = player.Tile;
                var reachable = player.Walkable.Where(tile => ClassicHexGrid.Distance(origin, tile) is >= 4 and <= 8)
                    .Select(tile => (Tile: tile, Path: ClassicHexGrid.Path(origin, tile, player.Walkable)))
                    .Where(row => row.Path.Length >= 4).OrderBy(row => row.Path.Length).ThenBy(row => row.Tile).First();
                Require(player.RequestMove(reachable.Tile), "Owned player could not enter a legal path.");
                var clock = new ClassicPlayerAnimation(path => source.Read(path).Bytes, premade.Character.Female);
                var rotations = new HashSet<int>(); var before = origin;
                for (var frame = 0; player.Moving && frame < 2000; frame++)
                {
                    clock.Tick(player, 0.01); rotations.Add(player.Rotation);
                    Require(player.Walkable.Contains(player.Tile), "Player entered an authored blocker.");
                    if (before != player.Tile) Require(ClassicHexGrid.Distance(before, player.Tile) == 1, "Source animation skipped an intermediate hex.");
                    before = player.Tile;
                }
                Require(!player.Moving && player.Tile == reachable.Tile && player.StepFraction == 0 && player.CompletedSteps == reachable.Path.Length,
                    "Player failed to finish the source path and stop exactly at its destination.");
                var save = Path.Combine(temporary, premade.Id + ".json"); player.Save(save);
                var restored = ClassicPlayerSession.Restore(source, save);
                Require(JsonSerializer.Serialize(restored.Choice) == JsonSerializer.Serialize(choice) && restored.Tile == player.Tile &&
                    restored.Rotation == player.Rotation && restored.CompletedSteps == player.CompletedSteps && restored.HitPoints == player.HitPoints,
                    "Cold restoration changed character or player state.");
                Require(restored.RequestMove(origin), "Return path was lost after cold load."); clock = new(path => source.Read(path).Bytes, premade.Character.Female);
                clock.Tick(restored, 0.1); Require(restored.Moving && restored.StepFraction > 0, "Source walk clock did not advance fractional motion.");
                var stop = restored.NextTile; restored.Stop();
                for (var frame = 0; restored.Moving && frame < 100; frame++) clock.Tick(restored, 0.01);
                Require(!restored.Moving && restored.Tile == stop, "Stop teleported the player or walked past the next hex.");
                var state = JsonSerializer.Deserialize<ClassicPlayerSave>(File.ReadAllBytes(save))!;
                File.WriteAllBytes(save, JsonSerializer.SerializeToUtf8Bytes(state with { MapSha256 = new string('0', 64) }));
                Reject(() => ClassicPlayerSession.Restore(source, save), "Changed owned MAP hash was admitted.");
                File.WriteAllBytes(save, JsonSerializer.SerializeToUtf8Bytes(state with { Campaign = "fallout-2" }));
                Reject(() => ClassicPlayerSession.Restore(source, save), "Fallout 2 state was admitted to Fallout 1.");
                var bytes = source.Read(premade.GcdPath).Bytes;
                Console.WriteLine(JsonSerializer.Serialize(new { premade.Character.Name, premade.Character.Female,
                    start = origin, destination = player.Tile, steps = player.CompletedSteps, rotations = rotations.Order().ToArray(),
                    stats = player.Stats, savedGcdStats = Enumerable.Range(8, 4).Select(index => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(index * 4))).ToArray(),
                    coldRestore = true, stopAtNextHex = true }));
            }
            foreach (var path in new[] { "iface", "numbers", "hlgrn", "sattkbup", "chaup", "chadn", "invbutup", "invbutdn", "pipup", "pipdn", "mapup", "mapdn", "optiup", "optidn" })
                _ = Fallout1NativeFrmReader.ReadFirstFrame(source.Read("art/intrface/" + path + ".frm").Bytes);
            var font = source.Read("font1.aaf").Bytes;
            Require(font.Length > 2060, "Owned HUD font is absent.");
            Console.WriteLine("PASS classic hex adjacency, blocked corridors, owned movement, stop, complete identity persistence, cross-campaign rejection and HUD source files.");
            return 0;
        }
        finally
        {
            var target = Path.GetFullPath(temporary);
            if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new IOException("Temporary probe cleanup escaped its root.");
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }
}
