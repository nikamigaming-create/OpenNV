using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class CellQueryContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-cell-query-");
        try
        {
            var header = new byte[20]; U32(2).CopyTo(header, 12);
            var questHeader = header.ToArray(); questHeader[16] = 1;
            byte[][] Scope(byte[] scriptHeader, string source) => [Field("SCHR", scriptHeader),
                Field("SLSD", Local(1)), Field("SCVR", Text("inside")), Field("SLSD", Local(2)), Field("SCVR", Text("prefix")),
                Field("SCRO", U32(0x80)), Field("SCRO", U32(0x91)), Field("SCRO", U32(0x14)), Field("SCRO", U32(0x85)),
                Field("SCRO", U32(0x83)), Field("SCTX", Text(source))];
            File.WriteAllBytes(Path.Combine(directory.FullName, "Cells.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])), Record("NPC_", 7), Record("NPC_", 2),
                Record("ACTI", 1, Field("SCRI", U32(0x50))),
                Record("SCPT", 0x50, Scope(header, "float inside\nfloat prefix\nbegin OnActivate\n" +
                    "set inside to inside + 1\nset prefix to player.GetInCell Region\nset inside to inside + 10\nend")),
                Record("QUST", 0x60, Field("DATA", [1, 0]), Field("SCRI", U32(0x51))),
                Record("SCPT", 0x51, Scope(questHeader, "float inside\nfloat prefix\nbegin GameMode\n" +
                    "set inside to (player).GetInCell (Region)\nset prefix to Other.GetInCell Region\nend")),
                Cell(0x80, "Region"), Cell(0x81, "RegionHouse"), Cell(0x82, "Different"),
                Cell(0x83, "REGIONExterior", false), Cell(0x84, "", false), Cell(0x85, "Unrelated"),
                Group(0x81, Record("REFR", 0x90, Field("NAME", U32(1)), Field("DATA", new byte[24])),
                    Record("ACHR", 0x91, Field("EDID", Text("Other")), Field("NAME", U32(2)), Field("DATA", new byte[24])))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Override.esp"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12]), Field("MAST", Text("Cells.esm")), Field("DATA", new byte[8])),
                Cell(0x82, "regionAnnex")));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Cells.esm", "Override.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var player = records.RuntimeFormKey(0x14);
            var caller = Key(0x90); var other = Key(0x91); var region = Key(0x80);
            var script = records.GetEffective(Key(0x50));
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x81)));
            var playerCell = Key(0x81);
            bool Query(FalloutFormKey actor, FalloutFormKey cell) => world.IsInCell(actor, cell, playerCell);
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Cell query emitted an effect."), IsInCell: Query));
            void Expression(string expression, bool expected)
            {
                scripts.ExecuteProgram(records.GetEffective(caller), script,
                    FalloutGameModeProgram.Read($"begin GameMode\nset prefix to {expression}\nend"), 0);
                Require(world.Get(caller).Read(2) == (expected ? 1 : 0), $"Cell expression failed: {expression}.");
            }
            Expression("GetInCell Region", true);
            Expression("(GetSelf).GetInCell (Region)", true);
            Expression("player.GetInCell Region", true);
            Expression("Other.GetInCell Unrelated", false);
            Expression("Other.GetInCell REGIONExterior", false);
            Require(world.IsInCell(other, region), "Interior prefix did not match a child cell.");
            world.SetPlacement(other, new(Key(0x82), [0, 0, 0], [0, 0, 0]));
            Require(world.IsInCell(other, region), "Cell query ignored the winning override EDID.");
            world.SetPlacement(other, new(Key(0x85), [0, 0, 0], [0, 0, 0]));
            Expression("Other.GetInCell Region", false);
            foreach (var shared in new[] { false, true })
            {
                var quests = new FalloutQuestState(records);
                var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                    _ => throw new InvalidDataException("Cell query emitted an effect."), IsInCell: Query));
                var questScripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(),
                    defaultProcessingDelay: 0, references: world)
                {
                    Host = new((_, _) => throw new InvalidDataException("Cell query changed a stage."), _ => 0,
                        shared ? executor.ExecuteProgram : null, IsInCell: Query)
                };
                questScripts.Advance(0);
                Require(quests.Variable(Key(0x60), 1) == 1 && quests.Variable(Key(0x60), 2) == 0,
                    "Shared/fallback quest query confused player and explicit reference cells.");
                playerCell = Key(0x85);
                world.SetPlacement(other, new(Key(0x83), [0, 0, 0], [0, 0, 0]));
                questScripts.Advance(.1);
                Require(quests.Variable(Key(0x60), 1) == 0 && quests.Variable(Key(0x60), 2) == 1,
                    "Shared/fallback quest query retained a stale cell or rejected a named exterior.");
                playerCell = Key(0x81);
                world.SetPlacement(other, new(Key(0x85), [0, 0, 0], [0, 0, 0]));
            }
            world.SetPlacement(other, new(Key(0x83), [0, 0, 0], [0, 0, 0]));
            world.UnloadCell(Key(0x81));
            Require(world.IsInCell(other, region), "Unloaded reference query lost retained placement.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            Require(cold.IsInCell(other, region), "Cold cell query lost a moved exterior placement.");
            world.SetPlacement(other, new(Key(0x84), [0, 0, 0], [0, 0, 0]));
            Require(!world.IsInCell(other, region), "Unnamed exterior invented an EDID match.");
            Reject(() => world.IsInCell(player, region));
            Reject(() => world.IsInCell(other, Key(2)));
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x81)));
            world.Get(caller).Write(1, 0); world.Get(caller).Write(2, 42);
            var missing = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => { }));
            var result = missing.Activate(caller, player);
            Require(result.Error?.Contains("live cell owner", StringComparison.Ordinal) == true &&
                world.Get(caller).Read(1) == 1 && world.Get(caller).Read(2) == 42,
                "Missing player owner returned false or consumed the failed write/suffix.");
            Require(missing.Dispatch(caller, "GameMode").Error is not null && world.Get(caller).Read(1) == 1,
                "Latched cell fault replayed its consumed prefix.");
            Console.WriteLine("OPENNV_CELL_QUERY_PASS typedArguments=true implicitAndPostfix=true winningPrefix=true " +
                "playerAndNpcIndependent=true sharedAndFallback=true liveCell=true namedExterior=true " +
                "exteriorArgumentFalse=true unloadedAndCold=true missingOwnerRefused=true consumedPrefixRetained=true");
        }
        finally { directory.Delete(true); }
    }

    private static FalloutFormKey Key(uint id) => new("Cells.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid cell query was accepted.");
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Local(uint index) { var local = new byte[24]; U32(index).CopyTo(local, 0); return local; }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Cell(uint id, string name, bool interior = true) => Record("CELL", id,
        Field("EDID", Text(name)), Field("DATA", [interior ? (byte)1 : (byte)0]),
        interior ? [] : Field("XCLC", new byte[8]));
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        U32((uint)data.Length).CopyTo(bytes, 4); U32(id).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Group(uint cell, params byte[][] rows)
    {
        var data = Join(rows); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        U32((uint)bytes.Length).CopyTo(bytes, 4); U32(cell).CopyTo(bytes, 8); U32(6).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
}
