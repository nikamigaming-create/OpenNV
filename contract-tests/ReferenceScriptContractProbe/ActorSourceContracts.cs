using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ActorSourceContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Actors.esm"), Join(Header(),
                Creature(0x800, 0, 0, "creatures/test/skeleton.nif", "body.nif", 1.5f,
                    Field("CNTO", Join(BitConverter.GetBytes(0x840u), BitConverter.GetBytes(3)))),
                Creature(0x801, 65, 0x800, "creatures/local/skeleton.nif", "local.nif", .75f),
                Creature(0x802, 66, 0x802, "creatures/test/skeleton.nif", "body.nif", 1),
                Creature(0x803, 0, 0, "creatures/test/skeleton.nif", "../escape.nif", 1),
                Creature(0x804, 256, 0x800, "creatures/test/skeleton.nif", "body.nif", 1),
                Record("MISC", 0x840, Field("EDID", Text("CreatureLoot")), Field("DATA", new byte[8])),
                Record("STAT", 0x844), Record("ACTI", 0x845, Field("SCRI", BitConverter.GetBytes(0x891u))),
                Cell(0x880, Join(Record("ACRE", 0x900, Field("NAME", BitConverter.GetBytes(0x801u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x901, Field("NAME", BitConverter.GetBytes(0x804u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x902, Field("NAME", BitConverter.GetBytes(0x804u)), Field("DATA", new byte[24])),
                    Marker(0x903, "SourceMapMarker"), Marker(0x905, "OtherMapMarker"),
                    Record("REFR", 0x904, Field("NAME", BitConverter.GetBytes(0x845u)), Field("DATA", new byte[24])))),
                Record("SCPT", 0x890, Field("SCTX", Text("begin OnActivate\nif GetUnconscious\nSetUnconscious 0\nelse\nSetUnconscious 1\nendif\nend"))),
                Record("SCPT", 0x891, Field("SCRO", BitConverter.GetBytes(0x903u)),
                    Field("SCTX", Text("begin OnActivate\nShowMap SourceMapMarker\nif SourceMapMarker.GetMapMarkerVisible == 1\nShowMap SourceMapMarker 1\nendif\nend"))),
                Record("VTYP", 0x850, Field("EDID", Text("TestVoice"))),
                Record("WRLD", 0x8f0, Field("ICON", Text("interface/worldmap/owned.dds")),
                    Field("MNAM", Join(BitConverter.GetBytes(2048), BitConverter.GetBytes(1024),
                        BitConverter.GetBytes((short)-20), BitConverter.GetBytes((short)30),
                        BitConverter.GetBytes((short)40), BitConverter.GetBytes((short)-10)))),
                Record("WRLD", 0x8f2, Field("WNAM", BitConverter.GetBytes(0x8f0u)), Field("PNAM", BitConverter.GetBytes((ushort)4)),
                    Field("ONAM", new float[] { 2, -20, 30 }.SelectMany(BitConverter.GetBytes).ToArray())),
                Record("REGN", 0x8f1, Field("WNAM", BitConverter.GetBytes(0x8f0u)),
                    Field("RPLD", new float[] { 0, 0, 10, 0, 0, 10 }.SelectMany(BitConverter.GetBytes).ToArray())),
                Record("INFO", 0x860, Field("DATA", [0, 0, 0, 0]), Field("QSTI", BitConverter.GetBytes(0x870u)),
                    Field("TRDT", Response()), Field("NAM1", Text("Source response")))));
            // Override keeps the original voice identity; it is not a new line
            // under Patch.esp merely because another plugin wins the INFO.
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Actors.esm"),
                Record("INFO", 0x860, Field("DATA", [0, 0, 0, 0]), Field("QSTI", BitConverter.GetBytes(0x870u)),
                    Field("TRDT", Response()), Field("NAM1", Text("Winning response")))));
            using var records = FalloutPluginStack.Load(directory, ["Actors.esm", "Patch.esp"]);
            FalloutFormKey Key(uint id) => new("Actors.esm", id);
            var map = FalloutWorldMap.Read(records, Key(0x8f0));
            Check(map.Width == 2048 && map.Height == 1024 && map.Project(-20 * 4096, 30 * 4096) == System.Numerics.Vector2.Zero &&
                map.Project(40 * 4096, -10 * 4096) == System.Numerics.Vector2.One &&
                map.Project(10 * 4096, 10 * 4096) == new System.Numerics.Vector2(.5f, .5f),
                "Signed world-map bounds or Y orientation changed.");
            Check(FalloutWorldMap.Read(records, Key(0x8f2)).World == Key(0x8f0) &&
                FalloutWorldMap.Position(records, Key(0x8f2), 15 * 4096, -10 * 4096) == new System.Numerics.Vector2(.5f, .5f),
                "Child-world map inheritance/scale/offset changed.");
            Check(FalloutExteriorClimate.ContainsRegion(records, Key(0x8f1), Key(0x8f0), 1, 1) &&
                !FalloutExteriorClimate.ContainsRegion(records, Key(0x8f1), Key(0x8f0), 9, 9) &&
                !FalloutExteriorClimate.ContainsRegion(records, Key(0x8f1), null, 1, 1) &&
                !FalloutExteriorClimate.ContainsRegion(records, Key(0x8f1), Key(0x8f2), 1, 1),
                "Player region membership ignored the source polygon or world.");
            var appearance = FalloutCreatureAppearanceResolver.Resolve(records, Key(0x801), Key(0x900));
            Check(appearance.ModelOwner == Key(0x800) && appearance.StatsOwner == Key(0x801) && appearance.BaseScale == .75f &&
                appearance.Models.Single() == "meshes/creatures/test/body.nif", "Independent CREA template groups or part directory changed.");
            Reject(() => FalloutCreatureAppearanceResolver.Resolve(records, Key(0x802)));
            Reject(() => FalloutCreatureAppearanceResolver.Resolve(records, Key(0x803)));
            Reject(() => FalloutCreatureAppearanceResolver.Resolve(records, Key(0x800), Key(0x900)));
            Check(FalloutCreatureAppearanceResolver.SelectIdle(appearance, ["meshes/creatures/test/mtidle.kf"]).EndsWith("/mtidle.kf"), "Source idle not selected.");
            Reject(() => FalloutCreatureAppearanceResolver.SelectIdle(appearance, []));
            Reject(() => FalloutCreatureAppearanceResolver.SelectIdle(appearance,
                ["meshes/creatures/test/mtidle.kf", "meshes/creatures/test/locomotion/mtidle.kf"]));
            var speaker = FalloutDialogueSpeaker.Read(records, Key(0x801));
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
            var firstInventory = world.Inventory(Key(0x901), 1).Contents;
            var peerInventory = world.Inventory(Key(0x902), 1).Contents;
            Check(firstInventory.Item(Key(0x840))?.Count == 3 && peerInventory.Item(Key(0x840))?.Count == 3 &&
                world.Inventory(Key(0x900), 1).Contents.Items.Count == 0, "CREA inventory ignored its independent template flag.");
            firstInventory.Remove(Key(0x840), 1, silent: true);
            using (var inventoryRestore = new FalloutReferenceWorld(records))
            {
                inventoryRestore.Restore(world.Capture());
                Check(inventoryRestore.Inventory(Key(0x901), 1).Contents.Item(Key(0x840))?.Count == 2 &&
                    inventoryRestore.Inventory(Key(0x902), 1).Contents.Item(Key(0x840))?.Count == 3,
                    "Creature inventory changes leaked to a peer or were lost in the save.");
            }
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ => { }));
            Check(world.MapMarkerVisibility(Key(0x903)) == 0 && scripts.Activate(Key(0x904), Key(0x14)).Error is null &&
                world.MapMarkerVisibility(Key(0x903)) == 2 && world.MapMarkerVisibility(Key(0x905)) == 0,
                "ShowMap/query did not preserve ordered visibility, travel admission and marker isolation.");
            world.ShowMap(Key(0x903));
            Check(world.MapMarkerVisibility(Key(0x903)) == 2, "Revealing a known map marker revoked fast travel.");
            using (var markerRestore = new FalloutReferenceWorld(records))
            {
                markerRestore.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
                Check(markerRestore.MapMarkerVisibility(Key(0x903)) == 2 && markerRestore.MapMarkerVisibility(Key(0x905)) == 0,
                    "Map marker discovery did not survive a cold save.");
            }
            Reject(() => world.ShowMap(Key(0x900)));
            Check(!world.IsUnconscious(Key(0x900)), "Fresh creature acquired unconscious state.");
            scripts.Activate(Key(0x900), Key(0x14));
            Check(world.IsUnconscious(Key(0x900)), "Source unconscious command did not reach the reference owner.");
            using var restoredWorld = new FalloutReferenceWorld(records);
            restoredWorld.Restore([world.Get(Key(0x900)).Capture()]);
            Check(restoredWorld.IsUnconscious(Key(0x900)), "Unconscious state was lost on restoration.");
            scripts.Activate(Key(0x900), Key(0x14));
            Check(!world.IsUnconscious(Key(0x900)), "Source unconscious condition/clear did not execute.");
            Check(speaker.TraitsOwner == Key(0x800) && speaker.VoiceType == Key(0x850) && speaker.Race is null,
                "Creature dialogue did not inherit its own voice traits.");
            var info = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x860)));
            var serviceInfo = info with { Conditions = [Condition(610, 4), Condition(71, 1)] };
            float Context(FalloutCondition condition) => condition.Function == 71 ? 0 : throw new NotSupportedException("Unrelated casino query.");
            Check(!FalloutDialogueTopic.Eligible(serviceInfo, speaker.Actor, new HashSet<FalloutFormKey>(), _ => 0, Context),
                "A false actor faction restriction evaluated another speaker's service dialogue.");
            Reject(() => FalloutDialogueTopic.Eligible(serviceInfo with { Conditions = [Condition(610, 4, 1), Condition(71, 1)] },
                speaker.Actor, new HashSet<FalloutFormKey>(), _ => 0, Context));
            string[] paths = ["sound/voice/Actors.esm/TestVoice/quest_topic_00000860_1.wav",
                "sound/voice/Actors.esm/OtherVoice/quest_topic_00000860_1.ogg",
                "sound/voice/Other.esm/TestVoice/quest_topic_00000860_1.ogg",
                "sound/voice/Actors.esm/TestVoice/quest_topic_00000860_2.ogg"];
            var binding = new FalloutDialogueVoiceIndex(paths).Resolve(speaker, info, 0);
            Check(binding.AudioPath == paths[0] && binding.WinningPlugin == "Patch.esp" && binding.LipPath.EndsWith("_1.lip"),
                "Voice crossed plugin/speaker/response ownership or rejected WAV.");
            Reject(() => new FalloutDialogueVoiceIndex(paths.Skip(1)).Resolve(speaker, info, 0));
            Reject(() => new FalloutDialogueVoiceIndex(paths.Append(paths[0].Replace(".wav", ".ogg"))).Resolve(speaker, info, 0));
            Reject(() => new FalloutDialogueVoiceIndex(paths).Resolve(speaker, info with { Speaker = Key(0x800) }, 0));
            var clock = new FalloutActorAnimationState(); var hash = new string('a', 64);
            clock.Bind("meshes/creatures/test/mtidle.kf", hash); clock.Advance(.73);
            var snapshot = JsonSerializer.Deserialize<FalloutActorAnimationSnapshot>(JsonSerializer.Serialize(clock.Capture()))!;
            var restored = new FalloutActorAnimationState(); restored.Restore(snapshot);
            restored.Advance(.41); clock.Advance(.41);
            Check(clock.Capture() == restored.Capture() && !restored.StartPending, "Cold actor clock changed or replayed start events.");
            Reject(() => restored.Bind(snapshot.Resource, new string('b', 64)));
            Reject(() => restored.Restore(snapshot with { ElapsedSeconds = double.NaN }));
            Reject(() => restored.Restore(snapshot with { StartPending = true }));
            Console.WriteLine("OPENNV_ACTOR_SOURCE_PASS templates=independent voice=speaker-info-response override=original-identity ambiguous=rejected clock=cold-exact");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Creature(uint id, ushort flags, uint template, string skeleton, string part, float scale, params byte[][] extra)
    {
        var acbs = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(22), flags);
        return Record("CREA", id, Field("ACBS", acbs), Field("TPLT", BitConverter.GetBytes(template)), Field("MODL", Text(skeleton)),
            Field("NIFZ", Text(part)), Field("BNAM", BitConverter.GetBytes(scale)), Field("VTCK", BitConverter.GetBytes(0x850u)),
            Field("SCRI", BitConverter.GetBytes(0x890u)), Join(extra));
    }
    private static byte[] Cell(uint id, byte[] reference)
    {
        var group = new byte[24 + reference.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), id);
        BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); reference.CopyTo(group, 24);
        return Join(Record("CELL", id, Field("EDID", Text("ActorCell")), Field("DATA", [1])), group);
    }
    private static byte[] Marker(uint id, string name) => Record("REFR", id, Field("EDID", Text(name)),
        Field("NAME", BitConverter.GetBytes(0x844u)), Field("DATA", new byte[24]), Field("XMRK", []),
        Field("FNAM", [0]), Field("TNAM", [5, 0xa7]), Field("FULL", Text("Source location")));
    private static byte[] Response() { var bytes = new byte[24]; bytes[12] = 1; return bytes; }
    private static byte[] Condition(ushort function, float comparison, byte flags = 0)
    {
        var bytes = new byte[28]; bytes[0] = flags;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), comparison);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), function); return bytes;
    }
    private static byte[] Header(string? master = null)
    {
        var hedr = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(hedr, 1.34f);
        return master is null ? Record("TES4", 0, Field("HEDR", hedr)) : Record("TES4", 0,
            Field("HEDR", hedr), Field("MAST", Text(master)), Field("DATA", new byte[8]));
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported actor source binding was accepted.");
    }
}
