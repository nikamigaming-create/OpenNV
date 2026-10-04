using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class HeadingQueryContracts
{
    internal static void Run()
    {
        foreach (var (target, expected) in new[] { (new float[] { 0, 1, 900 }, 0f), (new float[] { 1, 0, -900 }, 90f),
            (new float[] { -1, 0, 0 }, -90f), (new float[] { 0, -1, 0 }, 180f) })
            Check(MathF.Abs(MathF.Abs(FalloutHeadingAngle.Between([0, 0, 0], target, 0)) - MathF.Abs(expected)) < .0001f &&
                (MathF.Abs(expected) == 180 || MathF.Abs(FalloutHeadingAngle.Between([0, 0, 0], target, 0) - expected) < .0001f),
                "Heading sign, horizontal plane or rear wrapping changed.");
        Check(MathF.Abs(FalloutHeadingAngle.Between([0, 0, 0], [1, 0, 100], MathF.PI / 2)) < .0001f &&
            FalloutHeadingAngle.Between([1, 2, 3], [1, 2, 99], 7) == 0, "Heading retained target height or lost actor facing.");
        Reject(() => FalloutHeadingAngle.Between([0, 0, 0], [float.NaN, 0, 0], 0));
        var directory = Directory.CreateTempSubdirectory("opennv-heading-");
        try
        {
            const string source = "float angle\nbegin OnTrigger player\nset angle to player.GetHeadingAngle Goal\nend";
            var local = new byte[24]; U32(1).CopyTo(local, 0);
            var header = new byte[20]; U32(1).CopyTo(header, 12);
            var questHeader = header.ToArray(); questHeader[16] = 1;
            File.WriteAllBytes(Path.Combine(directory.FullName, "Headings.esm"), Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 7), Record("ACTI", 1, Field("SCRI", U32(0x50))), Record("NPC_", 2),
                Record("SCPT", 0x50, Field("SCHR", header), Field("SLSD", local), Field("SCVR", Text("angle")), Field("SCTX", Text(source)),
                    Field("SCRO", U32(0x91)), Field("SCRO", U32(0x14))),
                Record("QUST", 0x60, Field("DATA", [1, 0]), Field("SCRI", U32(0x51))),
                Record("SCPT", 0x51, Field("SCHR", questHeader), Field("SLSD", local), Field("SCVR", Text("angle")),
                    Field("SCRO", U32(0x91)), Field("SCRO", U32(0x14)),
                    Field("SCTX", Text("float angle\nbegin GameMode\nset angle to (player).GetHeadingAngle Goal\nset angle to GetHeadingAngle Goal\nend"))),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                    Record("REFR", 0x90, Field("NAME", U32(1)), Field("DATA", new byte[24])),
                    Record("ACHR", 0x91, Field("EDID", Text("Goal")), Field("NAME", U32(2)), Field("DATA", new byte[24])))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Headings.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var player = records.RuntimeFormKey(0x14);
            var trigger = new FalloutFormKey("Headings.esm", 0x90);
            var goal = new FalloutFormKey("Headings.esm", 0x91);
            var cell = new FalloutFormKey("Headings.esm", 0x80);
            world.LoadCell(FalloutCellSceneReader.Read(records, cell));
            world.SetPlacement(goal, new(cell, [10, 0, 100], [0, 0, 0]));
            var pose = new FalloutReferencePlacement(cell, [0, 0, 0], [0, 0, 0]);
            var callers = new List<FalloutFormKey>();
            float Query(FalloutFormKey caller, FalloutFormKey target)
            {
                callers.Add(caller);
                return world.HeadingAngle(caller, target, pose, 1);
            }
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Heading query emitted an effect."), HeadingAngle: Query));
            var result = scripts.DispatchFrame(trigger, [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { player })], 0).Single();
            Check(result.Error is null && world.Get(trigger).Read(1) == 90,
                $"Authored actor prefix, reference target or trigger query failed: {result.Error}; blocks={result.Blocks}; angle={world.Get(trigger).Read(1)}.");
            pose = pose with { RotationRadians = [0, 0, MathF.PI / 2] };
            result = scripts.DispatchFrame(trigger, [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { player })], .1).Single();
            Check(result.Error is null && MathF.Abs((float)world.Get(trigger).Read(1)) < .0001f,
                "Repeated trigger heading retained the previous actor pose.");
            foreach (var shared in new[] { false, true })
            {
                var quests = new FalloutQuestState(records);
                var executor = new FalloutReferenceScripts(records, world, quests,
                    new((_, _) => false, _ => throw new InvalidDataException("Heading query emitted an effect."), HeadingAngle: Query));
                var questScripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(),
                    defaultProcessingDelay: 0, references: world)
                {
                    Host = new((_, _) => throw new InvalidDataException("Heading query changed a stage."), _ => 0,
                        shared ? executor.ExecuteProgram : null, HeadingAngle: Query)
                };
                pose = pose with { RotationRadians = [0, 0, 0] };
                var firstQuery = callers.Count;
                questScripts.Advance(0);
                Check(quests.Variable(new("Headings.esm", 0x60), 1) == 0 &&
                    callers.Skip(firstQuery).SequenceEqual(new[] { player, new FalloutFormKey("Headings.esm", 0x60) }),
                    $"Typed reference heading differs in the {(shared ? "shared" : "fallback")} quest owner.");
            }
            pose = pose with { RotationRadians = [0, 0, MathF.PI / 2] };
            Check(world.HeadingAngle(trigger, goal, null, 1) == 0, "Nonactor caller acquired actor facing.");
            Reject(() => world.HeadingAngle(player, goal, null, 1));
            world.Get(goal).CaptureEngagement = () => new(player, Position: [10, 100, 0]);
            Check(MathF.Abs(world.Distance(goal, player, pose, 1) - MathF.Sqrt(10100)) < .001f,
                "Position-only engagement lost its supported distance owner.");
            Reject(() => world.HeadingAngle(goal, player, pose, 1));
            world.Get(goal).CaptureEngagement = () => new(player, Position: [10, 100, 0],
                Rotation: [0, -MathF.Sqrt(.5f), 0, MathF.Sqrt(.5f)]);
            Check(MathF.Abs(MathF.Abs(world.HeadingAngle(goal, player, pose, 1)) - 180) < .001f,
                "Shared heading ignored the current engagement rotation or source axes.");
            world.Get(goal).CaptureEngagement = null;
            var missing = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => { }));
            result = missing.DispatchFrame(trigger, [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { player })], 0).Single();
            Check(result.Error?.Contains("spatial owner", StringComparison.Ordinal) == true && MathF.Abs((float)world.Get(trigger).Read(1)) < .0001f,
                "Missing heading owner supplied a value or consumed its local write.");
            Console.WriteLine("OPENNV_HEADING_QUERY_PASS sign=true actorFacing=true horizontal=true wrap=true sourceTrigger=true liveChanges=true sharedAndFallback=true typedReference=true engagementFacing=true partialFacingRefused=true nonActorZero=true missingOwnerRefused=true prefixRetained=true");
        }
        finally { directory.Delete(true); }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid heading input was accepted.");
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
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
