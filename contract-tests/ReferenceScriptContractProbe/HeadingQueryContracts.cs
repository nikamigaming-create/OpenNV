using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class HeadingQueryContracts
{
    internal static void Run()
    {
        CompassPresentation();
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
                Record("CELL", 0x81, Field("DATA", [1])),
                Record("WRLD", 0x82), WorldGroup(0x82,
                    Record("CELL", 0x83, Field("DATA", [0]), Field("XCLC", new byte[8])),
                    Record("CELL", 0x86, Field("DATA", [0]), Field("XCLC", new byte[8]))),
                Record("WRLD", 0x84), WorldGroup(0x84,
                    Record("CELL", 0x85, Field("DATA", [0]), Field("XCLC", new byte[8]))),
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
            world.SetPlacement(goal, new(new("Headings.esm", 0x81), [0, 0, 0], [0, 0, 0]));
            Check(world.Distance(player, goal, pose, 1) == float.MaxValue,
                "Different interiors invented a shared coordinate distance.");
            world.SetPlacement(goal, new(new("Headings.esm", 0x83), [3, 4, 12], [0, 0, 0]));
            Check(world.Distance(player, goal, pose, 1) == float.MaxValue,
                "Interior/exterior distance invented a shared coordinate space.");
            pose = pose with { Cell = new("Headings.esm", 0x86), Position = [0, 0, 0] };
            Check(world.Distance(player, goal, pose, 1) == 13,
                "Adjacent exterior cells lost their common world coordinates.");
            pose = pose with { Cell = new("Headings.esm", 0x85) };
            Check(world.Distance(player, goal, pose, 1) == float.MaxValue,
                "Different worldspaces invented a coordinate distance.");
            Reject(() => world.Distance(player, goal, null, 1));
            world.SetPlacement(goal, new(cell, [10, 0, 100], [0, 0, 0]));
            pose = pose with { Cell = cell };
            var missing = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => { }));
            result = missing.DispatchFrame(trigger, [new("OnTrigger", TriggerReferences: new HashSet<FalloutFormKey> { player })], 0).Single();
            Check(result.Error?.Contains("spatial owner", StringComparison.Ordinal) == true && MathF.Abs((float)world.Get(trigger).Read(1)) < .0001f,
                "Missing heading owner supplied a value or consumed its local write.");
            Console.WriteLine("OPENNV_HEADING_QUERY_PASS sign=true actorFacing=true horizontal=true wrap=true sourceTrigger=true liveChanges=true sharedAndFallback=true typedReference=true engagementFacing=true partialFacingRefused=true nonActorZero=true missingOwnerRefused=true prefixRetained=true distanceDifferentSpacesMax=true distanceSameWorld3D=true");
        }
        finally { directory.Delete(true); }
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid heading input was accepted.");
    }
    private static void CompassPresentation()
    {
        FalloutFormKey Key(uint id) => new("Synthetic.esm", id);
        var candidates = new FalloutCompassAdmissionCandidate[] { new(Key(1), 4, true), new(Key(2), 1, null), new(Key(3), 9, true) };
        var selected = FalloutCompassAdmission.Select(candidates, 2);
        Check(selected.Included.SequenceEqual(new[] { Key(1) }) && selected.UnresolvedCapacity.SequenceEqual(new[] { Key(3) }),
            "Compass capacity invented detection or ignored a possible nearer admission.");
        selected = FalloutCompassAdmission.Select([new(Key(1), 4, true), new(Key(2), 4, true)], 1);
        Check(selected.Included.Count == 0 && selected.UnresolvedCapacity.Count == 2, "Compass invented a native cap tie-breaker.");
        selected = FalloutCompassAdmission.Select([new(Key(1), 9, true), new(Key(2), 1, false), new(Key(3), 4, true)], 1);
        Check(selected.Included.SequenceEqual(new[] { Key(3) }), "Compass capped before admission or failed squared-distance ordering.");
        var settings = new FalloutCompassBlinkSettings(1, 2, 4, 300, 200, 100, 10, 20, 30);
        Check(settings.At(0) == (300, 10) && settings.At(4) == (200, 20) && settings.At(1000) == (100, 30),
            "Compass blink lost clamping, squared source units or piecewise settings.");
        var blink = new FalloutCompassBlinkState(255, 0);
        blink.Advance(settings, 1, 255, 255, .1f, 1);
        Check(blink.Alpha == 255 && blink.Phase == 3, "Compass blink consumed multiple source phases in one frame.");
        blink.Advance(settings, 1, 255, 255, 0, 2);
        Check(blink.Phase == 1, "Compass blink did not leave its expired bright pause.");
        blink.Advance(settings, 1, 255, 255, 1, 100);
        Check(blink.Alpha == 0 && blink.Phase == 0 && blink.PauseDeadlineMilliseconds == 110,
            "Compass blink did not record its source zero-crossing pause deadline.");
        blink.Advance(settings, 1, 255, 255, 1, 101);
        blink.Advance(settings, 1, 255, 255, 0, 105);
        Check(blink.Alpha == 255 && blink.Phase == 3, "Compass reset a retained pause or invented black-hold time.");
        blink.Advance(settings, 1, 255, 255, 0, 111);
        Check(blink.Phase == 1, "Compass retained pause never resumed its falling phase.");
        var transparent = new FalloutCompassBlinkState(0, 0);
        transparent.Advance(settings, 1, 0, 0, 1, 0);
        Check(transparent.Alpha == 0, "Zero source HUD opacity produced nonfinite blink alpha.");
        var code = new byte[2400];
        new byte[] { 0xdb, 0x81, 0x14, 2, 0, 0, 0xdc, 0x0d, 1, 0, 0, 0, 0xdc, 0xc0 }.CopyTo(code, 1000);
        code[1800] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(1801), 100);
        new byte[] { 0xdc, 0x1d, 2, 0, 0, 0 }.CopyTo(code, 2000);
        var declaration = FalloutExecutableStringTable.ReadCompassHudDeclaration(code,
            address => address == 100 ? "Interface/HUD/glow_hud_compass_landmark.dds" : null,
            address => address == 1 ? .625 : address == 2 ? 144 : throw new InvalidDataException());
        Check(declaration == new FalloutCompassHudDeclaration(.625f, 144), "Compass substituted fitted edge/blink declarations.");
        code[2000] = 0;
        Reject(() => FalloutExecutableStringTable.ReadCompassHudDeclaration(code,
            address => address == 100 ? "Interface/HUD/glow_hud_compass_landmark.dds" : null, _ => .625));
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
    private static byte[] Group(uint cell, params byte[][] rows) => GeneralGroup(cell, 6, rows);
    private static byte[] WorldGroup(uint world, params byte[][] rows) => GeneralGroup(world, 1, rows);
    private static byte[] GeneralGroup(uint label, int kind, params byte[][] rows)
    {
        var data = Join(rows); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        U32((uint)bytes.Length).CopyTo(bytes, 4); U32(label).CopyTo(bytes, 8); U32((uint)kind).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
}
