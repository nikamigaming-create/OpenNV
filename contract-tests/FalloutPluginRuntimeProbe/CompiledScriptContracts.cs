using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class CompiledScriptContracts
{
    internal static void Run()
    {
        // Synthetic commands deliberately contain opcode-looking payload bytes.
        // The declared extent, never a byte-pattern scan, determines boundaries.
        byte[] bytes = [0x1d, 0, 0, 0, 0x1c, 0, 7, 0, 0x34, 0x12, 4, 0, 0x1e, 0, 0, 0, 0x1e, 0, 0, 0];
        var before = bytes.ToArray();
        var rows = FalloutCompiledScript.Read(bytes);
        Require(rows.Count == 3 && rows[0] == new FalloutCompiledInstruction(0, 4, 0, 0x1d, null) &&
            rows[1] == new FalloutCompiledInstruction(4, 12, 4, 0x1234, 7) &&
            rows[2].Offset == 16 && rows[2].End == bytes.Length && bytes.SequenceEqual(before),
            "Compiled instruction extents, explicit reference or source bytes changed.");
        Require(FalloutCompiledScript.Read([]).Count == 0, "Empty compiled body acquired an instruction.");
        Require(FalloutCompiledScript.Read([0xff, 0xff, 0, 0])[0].Opcode == ushort.MaxValue,
            "Unknown opcode was silently removed from the structural inventory.");
        foreach (var malformed in new byte[][] { [1], [1, 0, 0], [1, 0, 1, 0], [1, 0, 0xff, 0xff],
                     [0x1c, 0], [0x1c, 0, 1, 0], [0x1c, 0, 1, 0, 2, 0], [0x1c, 0, 1, 0, 0x1c, 0, 0, 0],
                     [0x1e, 0, 0, 0, 1] })
        {
            try { FalloutCompiledScript.Read(malformed); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Malformed SCDA framing was admitted.");
        }

        RunSyntheticVmContracts();
        Console.WriteLine("Compiled script framing, operand decoding and VM execution contracts passed.");
    }

    internal static void Owned(string installation)
    {
        var game = NativeGameInstallation.Detect(installation).Game;
        if (game == NativeGame.FalloutNewVegas)
        {
            RunOwnedFnvContracts(installation);
        }
        else if (game == NativeGame.Fallout3)
        {
            RunOwnedFo3Contracts(installation);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported installation for compiled script audit: {game}");
        }
    }

    private static void RunSyntheticVmContracts()
    {
        var root = Path.Combine(Path.GetTempPath(), "opennv-compiled-vm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Build a synthetic plugin stack with:
            // - GLOB 0x10 (initial 5)
            // - SCPT 0x20 attached to QUST 0x30 (short sVar = slot 1, float fVar = slot 2)
            var intSlot = new byte[24]; intSlot[0] = 1; intSlot[16] = 1;
            var floatSlot = new byte[24]; floatSlot[0] = 2; floatSlot[16] = 0;
            var questData = new byte[2]; questData[0] = 1;

            var pluginBytes = Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("GLOB", 0x10, Field("EDID", AsciiZ("TestGlobal")), Field("FNAM", [(byte)'f']), Field("FLTV", FloatBytes(5f))),
                Record("SCPT", 0x20,
                    Field("EDID", AsciiZ("TestQuestScript")),
                    Field("SLSD", intSlot), Field("SCVR", AsciiZ("sVar")),
                    Field("SLSD", floatSlot), Field("SCVR", AsciiZ("fVar")),
                    Field("SCTX", AsciiZ("scn TestQuestScript\nshort sVar\nfloat fVar\nbegin GameMode\nend"))),
                Record("QUST", 0x30,
                    Field("EDID", AsciiZ("TestQuest")),
                    Field("SCRI", UInt32Bytes(0x20)),
                    Field("DATA", questData)));

            File.WriteAllBytes(Path.Combine(root, "Synthetic.esm"), pluginBytes);
            using var records = FalloutPluginStack.Load(root, ["Synthetic.esm"]);
            var quests = new FalloutQuestState(records);
            var globals = new FalloutGlobalState(records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read));
            using var world = new FalloutReferenceWorld(records);
            var scripts = new FalloutReferenceScripts(records, world, quests,
                new((_, _) => false, _ => { }, Globals: globals));

            var questRecord = records.GetEffective(new("Synthetic.esm", 0x30));
            var fields = new[]
            {
                new FalloutPluginSubrecord("SCRO", UInt32Bytes(0x30)), // index 1: TestQuest
                new FalloutPluginSubrecord("SCRO", UInt32Bytes(0x10)), // index 2: TestGlobal
            };

            // Construct a compiled SCDA program:
            // 1. IF (TestGlobal == 5 && 10 > 3) [skipCount = 2, plus a local declaration inside that must NOT decrement skipCount]
            // 2.   short local1 (0x0012, payload: [1, 0]) -> skipped when IF is false, does not count toward skipCount!
            // 3.   SET TestQuest.sVar to 7 + 3 * 2 (13)
            // 4.   ELSE [skipCount = 1]
            // 5.   SET TestQuest.sVar to 99
            // 6. ENDIF
            // 7. SET TestQuest.fVar to 7.5 / 2.0 (3.75)
            // 8. SET TestGlobal to TestQuest.sVar + 1 (14)
            var ifExpr = PostfixBytes(
                GlobalToken(2), TextToken("5"), TextToken("=="),
                TextToken("10"), TextToken("3"), TextToken(">"),
                TextToken("&&"));
            var setSVarTrue = SetForeignInstruction(1, (byte)'s', 1, PostfixBytes(TextToken("7"), TextToken("3"), TextToken("2"), TextToken("*"), TextToken("+")));
            var setSVarFalse = SetForeignInstruction(1, (byte)'s', 1, PostfixBytes(TextToken("99")));
            var setFVar = SetForeignInstruction(1, (byte)'f', 2, PostfixBytes(TextToken("7.5"), TextToken("2.0"), TextToken("/")));
            var setGlobal = SetGlobalInstruction(2, PostfixBytes(ForeignVarToken(1, (byte)'s', 1), TextToken("1"), TextToken("+")));

            var programTrue = Join(
                IfInstruction(skipCount: 2, ifExpr),
                Instruction(0x0012, [1, 0]),
                setSVarTrue,
                ElseInstruction(skipCount: 1),
                setSVarFalse,
                Instruction(0x0019, []),
                setFVar,
                setGlobal);

            var bindings = new FalloutScriptBindings(records, questRecord, questRecord, fields);
            scripts.ExecuteCompiled(questRecord.FormKey, bindings, new FalloutCompiledScriptVm(programTrue), null, 0);

            Require(quests.Variable(questRecord.FormKey, 1) == 13, "Compiled IF true branch or postfix precedence failed.");
            Require(quests.Variable(questRecord.FormKey, 2) == 3.75, "Compiled float division assignment failed.");
            Require(globals.Get(new("Synthetic.esm", 0x10)) == 14f, "Compiled global assignment failed.");

            // Re-run with TestGlobal == 14 (so IF TestGlobal == 5 is false, skipping the local declaration + true SET + ELSE, landing on setSVarFalse!)
            scripts.ExecuteCompiled(questRecord.FormKey, bindings, new FalloutCompiledScriptVm(programTrue), null, 0);
            Require(quests.Variable(questRecord.FormKey, 1) == 99,
                "Compiled IF false branch skipCount did not ignore local declarations or failed to execute ELSE body.");
            Require(globals.Get(new("Synthetic.esm", 0x10)) == 100f, "Compiled global update after ELSE branch failed.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void RunOwnedFnvContracts(string installation)
    {
        var plugins = new List<string> { "FalloutNV.esm" };
        if (File.Exists(Path.Combine(installation, "Data", "LonesomeRoad.esm")))
            plugins.Add("LonesomeRoad.esm");

        using var content = RuntimeLiveContentSource.Open(installation, DevelopmentLabSource.Campaign(installation),
            activePlugins: plugins, includeImplicitPlugins: false);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quests = new FalloutQuestState(records);
        var globals = new FalloutGlobalState(records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read));
        var playerInventory = new FalloutPlayerInventory();
        using var world = new FalloutReferenceWorld(records);
        var inventoryCommands = new FalloutInventoryCommands(records, world, playerInventory, () => 1, globals);
        var speaker = records.EffectiveRecords("ACHR").First().FormKey;
        var scripts = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => false, _ => { },
                Distance: (_, _) => 200f,
                Globals: globals,
                Inventory: inventoryCommands));

        // 1. FalloutNV.esm:0f246d (INFO compiled-only result script)
        var infoRecord = records.GetEffective(new("FalloutNV.esm", 0x0f246d));
        var info = FalloutDialogueTopic.Decode(infoRecord);
        var isBeginCompiledOnly = string.IsNullOrWhiteSpace(info.BeginScript) && !info.BeginCompiled.IsEmpty;
        var compiledBytes = isBeginCompiledOnly ? info.BeginCompiled : info.EndCompiled;
        var decodedInfo = FalloutCompiledScript.Decode(compiledBytes.Span);
        Require(decodedInfo.Count == 1 && decodedInfo[0].Kind == FalloutCompiledStatementKind.Set,
            $"Unexpected FalloutNV.esm:0f246d compiled statements: {decodedInfo.Count}");
        var setTarget = decodedInfo[0].SetTarget!.Value;
        var scroRaw = BinaryPrimitives.ReadUInt32LittleEndian(
            infoRecord.ReadSubrecords()
                .First(row => row.Signature == "SCRO").Data.Span);
        var targetRef = infoRecord.Plugin.AdjustFormId(scroRaw);
        var slotBefore = quests.Variable(targetRef, setTarget.VariableIndex);
        scripts.ExecuteResult(info, speaker, begin: isBeginCompiledOnly);
        var slotAfter = quests.Variable(targetRef, setTarget.VariableIndex);
        Require(slotBefore == 0 && slotAfter == 1,
            $"FalloutNV.esm:0f246d (begin={isBeginCompiledOnly}, var={setTarget.VariableIndex}) did not set {targetRef} from 0 to 1 (observed {slotAfter}).");

        // 2. FalloutNV.esm:1377d1 (REFR XPPA patrol arrival script: MoveToMarker)
        var patrolPoint = records.GetEffective(new("FalloutNV.esm", 0x1377d1));
        var patrolRows = patrolPoint.ReadSubrecords().ToArray();
        var xppaIndex = Array.FindIndex(patrolRows, row => row.Signature == "XPPA");
        var xppaFields = patrolRows.Skip(xppaIndex + 1).TakeWhile(row =>
            row.Signature is "INAM" or "SCHR" or "SCDA" or "SCTX" or "SCRO" or "SCRV" or "SLSD" or "SCVR" or "TNAM").ToArray();
        var arrivalEvent = new FalloutPackageEvent(patrolPoint, "XPPA", xppaFields);
        var decodedPatrol = FalloutCompiledScript.Decode(arrivalEvent.Compiled.Span);
        Require(decodedPatrol.Count == 1 && decodedPatrol[0].Kind == FalloutCompiledStatementKind.Command,
            $"Unexpected FalloutNV.esm:1377d1 statements: {decodedPatrol.Count} (xppaFields={ string.Join(",", xppaFields.Select(f => f.Signature)) }, all={ string.Join(",", patrolRows.Select(f => f.Signature)) })");
        var patrolScroRaw = BinaryPrimitives.ReadUInt32LittleEndian(
            xppaFields.First(row => row.Signature == "SCRO").Data.Span);
        var destinationMarker = patrolPoint.Plugin.AdjustFormId(patrolScroRaw);
        var expectedPlacement = world.EditorPlacement(destinationMarker);
        scripts.ExecutePackageEvent(arrivalEvent, speaker);
        var movedPlacement = world.Get(speaker).Placement;
        var dx = movedPlacement?.Position[0] - expectedPlacement.Position[0] ?? float.PositiveInfinity;
        var dy = movedPlacement?.Position[1] - expectedPlacement.Position[1] ?? float.PositiveInfinity;
        Require(movedPlacement is not null &&
            movedPlacement.Cell == expectedPlacement.Cell &&
            MathF.Sqrt(dx * dx + dy * dy) < 512f,
            $"FalloutNV.esm:1377d1 XPPA MoveToMarker did not move the actor to marker {destinationMarker}.");

        // 3. LonesomeRoad.esm:00d9d5 (PACK POBA & POEA compiled-only event scripts)
        if (plugins.Contains("LonesomeRoad.esm"))
        {
            var packageRecord = records.GetEffective(new("LonesomeRoad.esm", 0x00d9d5));
            var package = FalloutScriptPackage.Read(packageRecord);
            var edeRef = records.EffectiveRecords("ACRE")
                .First(record => record.FormKey.OwnerPlugin == "LonesomeRoad.esm").FormKey;
            scripts.ExecutePackageEvent(package.EventPrograms["POBA"], edeRef);
            Require(world.IsGhost(edeRef), "LonesomeRoad.esm:00d9d5 POBA did not set Ghost=true on the actor.");

            var poeaMarkerKey = package.EventPrograms["POEA"].Fields
                .Where(row => row.Signature == "SCRO")
                .Select(row => packageRecord.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(row.Data.Span)))
                .Last(key => records.RuntimeFormId(key) != 0x14 && records.GetEffective(key).Signature == "REFR");
            scripts.ExecutePackageEvent(package.EventPrograms["POEA"], edeRef);
            var edeInstance = world.Get(edeRef);
            var edeMarkerPlacement = world.EditorPlacement(poeaMarkerKey);
            Require(!world.IsGhost(edeRef) &&
                !edeInstance.PlayerTeammate &&
                edeInstance.Restrained &&
                !world.IsEnabled(edeRef) &&
                edeInstance.Placement is not null &&
                edeInstance.Placement.Cell == edeMarkerPlacement.Cell,
                "LonesomeRoad.esm:00d9d5 POEA did not apply its full compiled state transition.");
        }

        Console.WriteLine("Owned Fallout: New Vegas compiled script execution contracts passed.");
    }

    private static void RunOwnedFo3Contracts(string installation)
    {
        using var content = RuntimeLiveContentSource.Open(installation, DevelopmentLabSource.Campaign(installation),
            activePlugins: ["Fallout3.esm"], includeImplicitPlugins: false);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quests = new FalloutQuestState(records);
        var globals = new FalloutGlobalState(records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read));
        using var world = new FalloutReferenceWorld(records);
        var actor = records.EffectiveRecords("ACHR").First().FormKey;
        var scripts = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => false, _ => { },
                Distance: (_, _) => 200f,
                Globals: globals));

        // 4. Fallout3.esm:063bfc (REFR XPPA patrol arrival script)
        var patrolPoint = records.GetEffective(new("Fallout3.esm", 0x063bfc));
        var patrolRows = patrolPoint.ReadSubrecords().ToArray();
        var xppaIndex = Array.FindIndex(patrolRows, row => row.Signature == "XPPA");
        var xppaFields = patrolRows.Skip(xppaIndex + 1).TakeWhile(row =>
            row.Signature is "INAM" or "SCHR" or "SCDA" or "SCTX" or "SCRO" or "SCRV" or "SLSD" or "SCVR" or "TNAM").ToArray();
        var arrivalEvent = new FalloutPackageEvent(patrolPoint, "XPPA", xppaFields);
        scripts.ExecutePackageEvent(arrivalEvent, actor);
        Console.WriteLine("Owned Fallout 3 compiled script execution contracts passed.");
    }

    private static byte[] AsciiZ(string value) => Encoding.ASCII.GetBytes(value + "\0");
    private static byte[] UInt32Bytes(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
    private static byte[] UInt16Bytes(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }
    private static byte[] FloatBytes(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        return bytes;
    }
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
    private static byte[] Field(string name, byte[] payload)
    {
        var bytes = new byte[payload.Length + 6];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)payload.Length));
        payload.CopyTo(bytes, 6);
        return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var payload = Join(fields);
        var bytes = new byte[payload.Length + 24];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        payload.CopyTo(bytes, 24);
        return bytes;
    }
    private static byte[] Instruction(ushort opcode, byte[] payload) =>
        Join(UInt16Bytes(opcode), UInt16Bytes(checked((ushort)payload.Length)), payload);
    private static byte[] IfInstruction(ushort skipCount, byte[] expr) =>
        Instruction(0x0016, Join(UInt16Bytes(skipCount), UInt16Bytes(checked((ushort)expr.Length)), expr));
    private static byte[] ElseInstruction(ushort skipCount) =>
        Instruction(0x0017, UInt16Bytes(skipCount));
    private static byte[] SetForeignInstruction(ushort refIdx, byte storage, ushort varIdx, byte[] expr) =>
        Instruction(0x0015, Join([(byte)'r'], UInt16Bytes(refIdx), [storage], UInt16Bytes(varIdx), UInt16Bytes(checked((ushort)expr.Length)), expr));
    private static byte[] SetGlobalInstruction(ushort refIdx, byte[] expr) =>
        Instruction(0x0015, Join([(byte)'G'], UInt16Bytes(refIdx), UInt16Bytes(checked((ushort)expr.Length)), expr));
    private static byte[] PostfixBytes(params byte[][] tokens) =>
        tokens.SelectMany((token, index) => index == 0 ? token : new byte[] { (byte)' ' }.Concat(token)).ToArray();
    private static byte[] TextToken(string text) => Encoding.ASCII.GetBytes(text);
    private static byte[] GlobalToken(ushort refIdx) => Join([(byte)'G'], UInt16Bytes(refIdx));
    private static byte[] ForeignVarToken(ushort refIdx, byte storage, ushort varIdx) =>
        Join([(byte)'r'], UInt16Bytes(refIdx), [storage], UInt16Bytes(varIdx));

    private static void Require(bool value, string error)
    {
        if (!value) throw new InvalidOperationException(error);
    }
}
