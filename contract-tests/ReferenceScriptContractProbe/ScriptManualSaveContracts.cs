using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ScriptManualSaveContracts
{
    internal static void Run()
    {
        SuccessfulAndCold();
        StoppedSuffixAndWriterFailure();
        SuspendedAndCoalesced();
        BoundFrameClock();
        InvalidSites();
        GlobalFunction();
        foreach (var shared in new[] { false, true }) QuestOwner(shared);
        Console.WriteLine("OPENNV_SCRIPT_MANUAL_SAVE_PASS deferred=true suffixCaptured=true newNormalSlot=true " +
            "coalesced=true suspendedRefused=true phaseAndMenuGates=true failureRetained=true unrelatedSourceContinues=true " +
            "sourceFaultCold=true consumedPrefix=true sharedAndFallback=true globalFunction=true noActiveCursorCapture=true");
    }

    private static void SuccessfulAndCold()
    {
        using var fixture = new ScriptSaveFixture("if saved == 0\nset saved to 1\nForceSave\nset suffix to 7\nendif");
        var writes = 0;
        fixture.Owner.Bind(id => { ++writes; return fixture.Write(id); }, _ => { });
        Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null &&
            fixture.Value(1) == 1 && fixture.Value(2) == 7 && writes == 0,
            "ForceSave wrote inline or consumed the source suffix incorrectly.");
        Reject(() => fixture.World.Capture());
        Require(!fixture.Owner.Drain(() => null) && writes == 0, "ForceSave captured in its requesting phase.");
        fixture.Owner.AdvancePhase();
        Require(!fixture.Owner.Drain(() => "message-menu") && fixture.Owner.Pending &&
            fixture.Owner.DeferredBy == "message-menu", "A normal message save gate discarded the request.");
        Require(fixture.Owner.Drain(() => null) && writes == 1 && !fixture.Owner.Pending,
            "Deferred ForceSave did not commit exactly one new normal slot.");
        var receipt = fixture.Owner.Receipt!;
        Require(receipt.Disposition == "completed" && receipt.Sites.Count == 1 &&
            receipt.Sites[0].Caller == fixture.Caller && receipt.Sites[0].Program == fixture.Script.FormKey &&
            receipt.Sites[0].RecordSha256.Length == 64 && receipt.Sites[0].ProgramSha256.Length == 64 &&
            receipt.Invocations is [{ Ended: true, SourceError: null }] &&
            File.Exists(receipt.SlotPath), "Committed slot lost its source instruction receipt.");
        Require(!fixture.Owner.Drain(() => null) && writes == 1, "A completed request replayed its writer.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(receipt.SlotPath!));
        using var cold = new FalloutReferenceWorld(fixture.Records);
        cold.Restore(document.RootElement.GetProperty("references").Deserialize<FalloutReferenceSnapshot[]>()!);
        cold.LoadCell(FalloutCellSceneReader.Read(fixture.Records, fixture.Cell));
        var scripts = new FalloutReferenceScripts(fixture.Records, cold, new(fixture.Records),
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected cold save effect.")));
        Require(scripts.Activate(fixture.Caller, fixture.Player).Error is null &&
            cold.Get(fixture.Caller).Read(1) == 1 && cold.Get(fixture.Caller).Read(2) == 7 &&
            cold.ScriptManualSaves.Receipt is null,
            "Cold saved prefix/suffix replayed ForceSave or lost a completed source write.");
    }

    private static void StoppedSuffixAndWriterFailure()
    {
        using (var fixture = new ScriptSaveFixture("set saved to 1\nForceSave\nUnownedSuffix\nset suffix to 9"))
        {
            fixture.Owner.Bind(fixture.Write, _ => { });
            var result = fixture.Scripts.Activate(fixture.Caller, fixture.Player);
            Require(result.Error is not null && fixture.Value(1) == 1 && fixture.Value(2) == 0 &&
                fixture.Owner.Receipt!.Invocations is [{ Ended: true, SourceError: not null }],
                "Stopped suffix lost its consumed ForceSave invocation.");
            fixture.Owner.AdvancePhase();
            Require(fixture.Owner.Drain(() => null), "A settled source fault could not use its existing fault snapshot.");
            using var document = JsonDocument.Parse(File.ReadAllBytes(fixture.Owner.Receipt!.SlotPath!));
            using var cold = new FalloutReferenceWorld(fixture.Records);
            cold.Restore(document.RootElement.GetProperty("references").Deserialize<FalloutReferenceSnapshot[]>()!);
            cold.LoadCell(FalloutCellSceneReader.Read(fixture.Records, fixture.Cell));
            var scripts = new FalloutReferenceScripts(fixture.Records, cold, new(fixture.Records), new((_, _) => false, _ => { }));
            Require(scripts.Dispatch(fixture.Caller, "GameMode").Error == result.Error &&
                cold.Get(fixture.Caller).Read(1) == 1 && cold.Get(fixture.Caller).Read(2) == 0,
                "Cold stopped invocation reran its prefix or suffix.");
        }
        using (var fixture = new ScriptSaveFixture("if saved == 0\nset saved to 1\nForceSave\nset suffix to 7\nendif",
            gameModeBody: "set suffix to suffix + 1"))
        {
            var writes = 0; var failed = 0;
            fixture.Owner.Bind(_ => { ++writes; throw new IOException("Synthetic writer refusal"); }, _ => ++failed);
            Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null, "Source request failed before the writer phase.");
            fixture.Owner.AdvancePhase();
            Require(!fixture.Owner.Drain(() => null) && writes == 1 && failed == 1 &&
                fixture.Owner.Receipt is { Disposition: "failed", Error: "Synthetic writer refusal" },
                "Writer refusal cleared or completed the request.");
            var failedReceipt = fixture.Owner.Receipt;
            Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null &&
                fixture.Scripts.Dispatch(fixture.Caller, "GameMode").Error is null && fixture.Value(2) == 8 &&
                fixture.Owner.EnteredInvocations == 0 && ReferenceEquals(fixture.Owner.Receipt, failedReceipt),
                "A deferred writer failure poisoned later source instructions or replaced its failed receipt.");
            fixture.Owner.AdvancePhase();
            Require(!fixture.Owner.Drain(() => throw new InvalidDataException("A failed request reentered save admission.")),
                "A failed deferred request replayed its writer.");
            Reject(() => fixture.World.Capture());
            Require(fixture.Value(1) == 1 && fixture.Value(2) == 8 && writes == 1 && failed == 1 &&
                fixture.Owner.Receipt is { Disposition: "failed", SlotPath: null, Invocations: [{ Ended: true, SourceError: null }] },
                "Failed deferred save replayed a source effect or writer.");
        }
    }

    private static void SuspendedAndCoalesced()
    {
        using var fixture = new ScriptSaveFixture("ForceSave");
        var writes = 0;
        fixture.Owner.Bind(id => { ++writes; return fixture.Write(id); }, _ => { });
        var program = FalloutGameModeProgram.Read("begin GameMode\nForceSave\nend");
        IEnumerable<bool> Steps() => fixture.Owner.Execute(fixture.Caller, fixture.Script, program,
            program.Steps(_ => 0, (_, _) => { }, (_, _) => fixture.Owner.Request(program.LastStatement)));
        using (var first = Steps().GetEnumerator())
        using (var second = Steps().GetEnumerator())
        {
            Require(first.MoveNext() && second.MoveNext() && fixture.Owner.EnteredInvocations == 2,
                "Interleaved source invocations lost their independent receipts.");
            fixture.Owner.AdvancePhase();
            Require(!fixture.Owner.Drain(() => null), "A suspended source cursor was captured.");
            Require(!first.MoveNext() && !second.MoveNext() && fixture.Owner.EnteredInvocations == 0 &&
                fixture.Owner.Receipt!.Sites.Count == 2 && fixture.Owner.Drain(() => null) && writes == 1,
                "Queued requests failed to coalesce after both source cursors retired.");
        }
        using var cancelled = new ScriptSaveFixture("ForceSave");
        cancelled.Owner.Bind(cancelled.Write, _ => { });
        var abandoned = cancelled.Owner.Execute(cancelled.Caller, cancelled.Script, program,
            program.Steps(_ => 0, (_, _) => { }, (_, _) => cancelled.Owner.Request(program.LastStatement))).GetEnumerator();
        Require(abandoned.MoveNext(), "Cancellation fixture did not enter ForceSave.");
        abandoned.Dispose();
        Require(cancelled.Owner.Receipt is { Disposition: "failed" }, "Abandoned source cursor falsely completed.");
        Reject(() => cancelled.World.Capture());
    }

    private static void InvalidSites()
    {
        foreach (var command in new[] { "ForceSave 1", "player.ForceSave" })
        {
            using var fixture = new ScriptSaveFixture($"set saved to 1\n{command}\nset suffix to 9");
            fixture.Owner.Bind(fixture.Write, _ => { });
            Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is not null &&
                fixture.Value(1) == 1 && fixture.Value(2) == 0 && fixture.Owner.Receipt is null,
                "Qualified/parameterized ForceSave was admitted or lost its prefix.");
        }
        using var missing = new ScriptSaveFixture("set saved to 1\nForceSave\nset suffix to 9");
        Require(missing.Scripts.Activate(missing.Caller, missing.Player).Error is not null &&
            missing.Value(1) == 1 && missing.Value(2) == 0 && missing.Owner.Receipt is { Disposition: "failed" },
            "An absent manual writer was silently waived.");
        Require(missing.Scripts.Activate(missing.Caller, missing.Player).Error is not null &&
            missing.Value(1) == 1 && missing.Value(2) == 0,
            "An absent-writer source fault was cleared or replayed.");
        Reject(() => missing.World.Capture());
        Reject(() => missing.Owner.Request(0));
        using var wrongOwner = new ScriptSaveFixture("ForceSave");
        wrongOwner.Owner.Bind(wrongOwner.Write, _ => { });
        var program = FalloutGameModeProgram.Read("begin GameMode\nForceSave\nend");
        Reject(() => wrongOwner.Owner.Execute(wrongOwner.Caller, wrongOwner.Records.GetEffective(new("Saves.esm", 1)),
            program, program.Steps(_ => 0, (_, _) => { }, (_, _) => { })).ToArray());
    }

    private static void BoundFrameClock()
    {
        using var fixture = new ScriptSaveFixture("ForceSave");
        ulong frame = 40;
        fixture.Owner.Bind(fixture.Write, _ => { }, () => frame);
        Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null, "Bound-frame request failed.");
        fixture.Owner.AdvancePhase();
        Require(!fixture.Owner.Drain(() => null), "A later driver callback in the same engine frame wrote the slot.");
        ++frame;
        Require(fixture.Owner.Drain(() => null), "A completed later engine frame did not drain the slot.");
    }

    private static void GlobalFunction()
    {
        using var fixture = new ScriptSaveFixture("", functionBody:
            "set saved to GetSelf\nForceSave\nset suffix to 7\nSetFunctionValue saved + suffix");
        var function = new FalloutFormKey("Saves.esm", 0x52);
        var writes = 0;
        fixture.Owner.Bind(id => { ++writes; return fixture.Write(id); }, _ => { });
        Require(fixture.Scripts.InvokeFunction(function, null, [], 0) == 7 && writes == 0 &&
            fixture.Owner.Receipt is { Invocations: [{ Ended: true, SourceError: null }] } &&
            fixture.Owner.Receipt.Sites.Single() is { } site && site.Caller == function && site.Program == function,
            "Global function invented a calling actor, wrote inline or lost its completed suffix.");
        fixture.Owner.AdvancePhase();
        Require(fixture.Owner.Drain(() => null) && writes == 1 && !fixture.Owner.Drain(() => null),
            "Global function's deferred save did not commit exactly one ordinary slot.");
    }

    private static void QuestOwner(bool shared)
    {
        using var fixture = new ScriptSaveFixture("", "if saved == 0\nset saved to 1\nForceSave\nset suffix to 5\nendif");
        var quests = new FalloutQuestState(fixture.Records);
        var scripts = new FalloutQuestScripts(fixture.Records, quests, new HashSet<FalloutFormKey>(), new(),
            references: fixture.World, defaultProcessingDelay: 0);
        var executor = new FalloutReferenceScripts(fixture.Records, fixture.World, quests, new((_, _) => false, _ => { }));
        scripts.Host = new((_, _) => throw new InvalidDataException("Unexpected stage"), _ => 0,
            shared ? executor.ExecuteProgram : null);
        scripts.ScriptManualSaves.Bind(fixture.Write, _ => { });
        scripts.Advance(0);
        Require(quests.Variable(fixture.Quest, 1) == 1 && quests.Variable(fixture.Quest, 2) == 5 &&
            scripts.ScriptManualSaves.Pending, "Quest execution did not share the deferred source-save owner.");
        Reject(() => scripts.Capture());
        scripts.ScriptManualSaves.AdvancePhase();
        Require(scripts.ScriptManualSaves.Drain(() => null), "Settled shared/fallback quest save did not drain.");
    }

    internal static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    internal static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Unsupported source-save state was admitted.");
    }
}

internal sealed class ScriptSaveFixture : IDisposable
{
    private readonly DirectoryInfo _directory;
    internal FalloutPluginStack Records { get; }
    internal FalloutReferenceWorld World { get; }
    internal FalloutReferenceScripts Scripts { get; }
    internal FalloutScriptManualSaveRequests Owner => World.ScriptManualSaves;
    internal FalloutFormKey Caller => new("Saves.esm", 0x90);
    internal FalloutFormKey Cell => new("Saves.esm", 0x80);
    internal FalloutFormKey Quest => new("Saves.esm", 0x60);
    internal FalloutFormKey Player => Records.RuntimeFormKey(0x14);
    internal FalloutPluginRecord Script => Records.GetEffective(new("Saves.esm", 0x50));

    internal ScriptSaveFixture(string body, string questBody = "", Func<bool>? hardcore = null, string? functionBody = null,
        string gameModeBody = "")
    {
        _directory = Directory.CreateTempSubdirectory("opennv-script-save-");
        var header = new byte[20]; U32(2).CopyTo(header, 12);
        var questHeader = header.ToArray(); questHeader[16] = 1;
        byte[][] Scope(byte[] scriptHeader, string source) => [Field("SCHR", scriptHeader),
            Field("SLSD", Local(1)), Field("SCVR", Text("saved")), Field("SLSD", Local(2)), Field("SCVR", Text("suffix")),
            Field("SCRO", U32(0x14)), Field("SCRO", U32(0x91)), Field("SCTX", Text(source))];
        File.WriteAllBytes(Path.Combine(_directory.FullName, "Saves.esm"), Join(
            Record("TES4", 0, Field("HEDR", new byte[12])), Record("NPC_", 7), Record("NPC_", 2),
            Record("ACTI", 1, Field("SCRI", U32(0x50))),
            Record("SCPT", 0x50, Scope(header, "float saved\nfloat suffix\nbegin OnActivate\n" + body +
                "\nend\nbegin GameMode\n" + gameModeBody + "\nend")),
            Record("QUST", 0x60, Field("DATA", [1, 0]), Field("SCRI", U32(0x51))),
            Record("SCPT", 0x51, Scope(questHeader, "float saved\nfloat suffix\nbegin GameMode\n" + questBody + "\nend")),
            functionBody is null ? [] : Record("SCPT", 0x52, Scope(header,
                "float saved\nfloat suffix\nbegin Function {}\n" + functionBody + "\nend")),
            Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                Record("REFR", 0x90, Field("NAME", U32(1)), Field("DATA", new byte[24])),
                Record("ACHR", 0x91, Field("EDID", Text("Other")), Field("NAME", U32(2)), Field("DATA", new byte[24])))));
        Records = FalloutPluginStack.Load(_directory.FullName, ["Saves.esm"]);
        World = new(Records);
        World.LoadCell(FalloutCellSceneReader.Read(Records, Cell));
        Scripts = new(Records, World, new(Records), new((_, _) => false,
            _ => throw new InvalidDataException("Unexpected fixture effect."), IsHardcore: hardcore));
    }

    internal double Value(uint index) => World.Get(Caller).Read(index);
    internal RuntimeSaveSlotMetadata Write(Guid id)
    {
        var canonical = Path.Combine(_directory.FullName, "save.json");
        var catalog = new RuntimeSaveSlotCatalog(canonical, root =>
        {
            if (root.GetProperty("schema").GetString() != "opennv-script-save-contract-v1")
                throw new InvalidDataException("Wrong fixture schema");
            FalloutReferenceSnapshot.Validate(root.GetProperty("references").Deserialize<FalloutReferenceSnapshot[]>()!);
        });
        return catalog.Create(id, () => File.WriteAllText(canonical,
            JsonSerializer.Serialize(new { schema = "opennv-script-save-contract-v1", references = World.Capture() })));
    }
    public void Dispose() { World.Dispose(); Records.Dispose(); _directory.Delete(true); }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Local(uint index) { var local = new byte[24]; U32(index).CopyTo(local, 0); return local; }
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
