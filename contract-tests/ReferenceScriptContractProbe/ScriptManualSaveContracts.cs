using System.Buffers.Binary;
using System.Security.Cryptography;
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
        SuspendedAndDistinct();
        BoundFrameClock();
        InvalidSitesAndMissingWriter();
        DiagnosticRequestsRefused();
        QuestOwner();
        Console.WriteLine("OPENNV_SCRIPT_MANUAL_SAVE_PASS actualScda=true deferred=true suffixCaptured=true " +
            "newNormalSlot=true distinctRequests=true suspendedRefused=true phaseAndMenuGates=true " +
            "failureRetained=true unrelatedSourceContinues=true sourceFaultCold=true consumedPrefix=true " +
            "actualQueueCold=true diagnosticRequestsRefused=true sharedCompiledQuest=true missingExecutorRefused=true");
    }

    private static void SuccessfulAndCold()
    {
        using var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once());
        var writes = 0;
        fixture.Bind(id => { ++writes; return fixture.Write(id); });
        Require(fixture.Activate().Error is null && fixture.Value(1) == 1 && fixture.Value(2) == 7 && writes == 0,
            "Compiled ForceSave wrote inline or lost its actual consumed suffix.");
        var requested = fixture.Owner.Order.Requests.Single();
        Require(requested.Script is { Authority: FalloutScriptResultAuthority.CompiledVanilla, CompiledProgram: not null } &&
            requested.Invocation is { Disposition: RuntimeSaveInvocationDisposition.Completed } &&
            requested.RequestedPhase == fixture.Phase && requested.DestinationPath == fixture.Binding.SlotPath(requested.Request),
            "Source request lacks its original compiled instruction, retired invocation or selected destination.");
        Require(!fixture.Owner.Drain(() => throw new InvalidDataException("Same-phase admission ran.")),
            "The requesting engine phase authorized its own writer.");
        fixture.AdvancePhase();
        Require(!fixture.Owner.Drain(() => "message-menu") && fixture.Owner.Pending && fixture.Owner.DeferredBy == "message-menu",
            "Normal save admission discarded an actual compiled request.");
        Require(fixture.Owner.Drain(() => null) && writes == 1 && !fixture.Owner.Pending,
            "Deferred compiled ForceSave did not commit its exact new slot once.");
        var receipt = fixture.Owner.Receipt!;
        Require(receipt is { Disposition: "completed", Sites.Count: 1, Invocations: [{ Ended: true, SourceError: null }] } &&
            receipt.Sites[0].Caller == fixture.Caller && receipt.Sites[0].Program == fixture.Program.Source.FormKey &&
            File.Exists(receipt.SlotPath), "Committed source instruction/destination receipt was lost.");
        using var cold = fixture.Restore(receipt.SlotPath!);
        Require(cold.Owner.Order.Epoch != fixture.Owner.Order.Epoch && cold.Owner.Order.Requests.Single().Disposition == RuntimeSaveRequestDisposition.Completed &&
            cold.Owner.Order.Requests.Single().Request == requested.Request && cold.Activate().Error is null &&
            cold.Value(1) == 1 && cold.Value(2) == 7 && cold.Owner.Order.Requests.Count == 1 &&
            !cold.Owner.Drain(() => throw new InvalidDataException("Loaded committed writer replayed.")) &&
            !fixture.Owner.Drain(() => throw new InvalidDataException("Warm committed writer replayed.")),
            "Actual saved queue/locals replayed ForceSave or lost its consumed prefix in a fresh session.");
        fixture.RequireSourceUnchanged();
    }

    private static void StoppedSuffixAndWriterFailure()
    {
        using (var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Join(
            CompiledManualSaveFixture.Set(1, 1), CompiledManualSaveFixture.Force(),
            CompiledManualSaveFixture.Instruction(0x2f03), CompiledManualSaveFixture.Set(2, 9))))
        {
            fixture.Bind(fixture.Write);
            var result = fixture.Activate();
            Require(result.Error is not null && fixture.Value(1) == 1 && fixture.Value(2) == 0 &&
                fixture.Owner.Receipt is { Invocations: [{ Ended: true, SourceError: not null }] },
                "A genuine compiled failure lost its committed prefix or retired save invocation.");
            fixture.AdvancePhase();
            Require(fixture.Owner.Drain(() => null), "A closed compiled object fault lost its supported world snapshot.");
            using var cold = fixture.Restore(fixture.Owner.Receipt!.SlotPath!);
            Require(cold.Activate().Error == result.Error && cold.Value(1) == 1 && cold.Value(2) == 0 &&
                cold.Owner.Order.Requests.Count == 1, "Cold compiled failure replayed the committed prefix or stopped suffix.");
        }
        using (var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once()))
        {
            var writes = 0; var failures = 0;
            fixture.Bind(_ => { ++writes; throw new IOException("Synthetic writer refusal"); }, _ => ++failures);
            Require(fixture.Activate().Error is null, "Source failed before its genuine writer phase.");
            fixture.AdvancePhase();
            Require(!fixture.Owner.Drain(() => null) && writes == 1 && failures == 1 &&
                fixture.Owner.Receipt is { Disposition: "failed", Error: "Synthetic writer refusal", SlotPath: null },
                "Actual writer refusal disappeared or completed.");
            var failed = JsonSerializer.Serialize(fixture.Owner.Receipt);
            Require(fixture.Activate().Error is null && fixture.Scripts.Dispatch(fixture.Caller, "GameMode").Error is null &&
                fixture.Value(2) == 8 && JsonSerializer.Serialize(fixture.Owner.Receipt) == failed &&
                !fixture.Owner.Drain(() => throw new InvalidDataException("Failed writer retried.")) && writes == 1 && failures == 1,
                "A writer failure replayed its request or poisoned unrelated subsequent source instructions.");
            Reject(fixture.Owner.RequireCapture);
        }
    }

    private static void SuspendedAndDistinct()
    {
        using var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Force());
        var writes = 0;
        fixture.Bind(id => { ++writes; return fixture.Write(id); });
        using (var first = fixture.Steps().GetEnumerator())
        using (var second = fixture.Steps().GetEnumerator())
        {
            Require(first.MoveNext() && second.MoveNext() && fixture.Owner.EnteredInvocations == 2 &&
                fixture.Owner.Order.Requests.Count == 2 && fixture.Owner.Order.Requests.Select(row => row.Request).Distinct().Count() == 2 &&
                fixture.Owner.Order.Requests.Select(row => row.Script!.Invocation).Distinct().Count() == 2,
                "Two actual save instructions coalesced their request or invocation identities.");
            fixture.AdvancePhase();
            Reject(fixture.Owner.RequireCapture);
            Require(!fixture.Owner.Drain(() => null) && writes == 0, "An entered compiled cursor authorized capture.");
            Require(!first.MoveNext() && !second.MoveNext() && fixture.Owner.EnteredInvocations == 0,
                "Actual interleaved source leases did not retire independently.");
            Require(fixture.Owner.Drain(() => null) && writes == 1 && fixture.Owner.Pending &&
                fixture.Owner.Order.Find(1).Disposition == RuntimeSaveRequestDisposition.Completed &&
                fixture.Owner.Order.Find(2).Disposition == RuntimeSaveRequestDisposition.Pending &&
                fixture.Owner.Drain(() => null) && writes == 2 && !fixture.Owner.Pending &&
                fixture.Owner.Order.Requests.Select(row => row.Committed!.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                "Distinct original instructions did not commit two exact slots in their common queue order.");
        }
        using var abandoned = new CompiledManualSaveFixture(CompiledManualSaveFixture.Force());
        abandoned.Bind(abandoned.Write);
        var cursor = abandoned.Steps().GetEnumerator();
        Require(cursor.MoveNext(), "Abandonment fixture did not reach its actual save instruction.");
        cursor.Dispose();
        Require(abandoned.Owner.Receipt is { Disposition: "failed", Invocations: [{ Ended: true, SourceError: not null }] },
            "An abandoned compiled source lease invented completion.");
        Reject(abandoned.Owner.RequireCapture);
    }

    private static void BoundFrameClock()
    {
        using var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Force(), phase: 40);
        fixture.Bind(fixture.Write);
        Require(fixture.Activate().Error is null, "Bound-phase source request failed.");
        fixture.Owner.AdvancePhase();
        Require(fixture.Phase == 40 && !fixture.Owner.Drain(() => null),
            "An unrelated callback counter replaced the actual selected engine phase.");
        fixture.AdvancePhase();
        Require(fixture.Owner.Drain(() => null), "A genuine later bound phase failed to drain its source request.");
    }

    private static void InvalidSitesAndMissingWriter()
    {
        foreach (var force in new[] { CompiledManualSaveFixture.Force(qualified: true), CompiledManualSaveFixture.Force(arguments: true) })
        {
            using var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Join(
                CompiledManualSaveFixture.Set(1, 1), force, CompiledManualSaveFixture.Set(2, 9)));
            fixture.Bind(fixture.Write);
            Require(fixture.Activate().Error is not null && fixture.Value(1) == 1 && fixture.Value(2) == 0 &&
                fixture.Owner.Order.Requests.Count == 0, "Invalid receiver/arguments created a source save or lost its genuine prefix.");
        }
        using var missing = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once());
        Require(missing.Activate().Error is null && missing.Value(1) == 1 && missing.Value(2) == 7 &&
            missing.Owner.Pending, "An absent writer changed source execution instead of retaining its deferred request.");
        missing.AdvancePhase();
        Require(!missing.Owner.Drain(() => null) && missing.Owner.Receipt is { Disposition: "failed", SlotPath: null } &&
            !missing.Owner.Drain(() => throw new InvalidDataException("Absent writer was retried.")),
            "An absent real writer was waived or retried after its retained failure.");
        Reject(missing.Owner.RequireCapture);
        Reject(() => missing.Owner.Request(0));
        using var wrong = new CompiledManualSaveFixture(CompiledManualSaveFixture.Force());
        Reject(() => wrong.Scripts.CompiledSteps(wrong.Quest, wrong.Program, wrong.Block).ToArray());
        Require(wrong.Owner.Order.Requests.Count == 0, "An unattached compiled caller manufactured a source save.");
    }

    private static void DiagnosticRequestsRefused()
    {
        using (var fixture = new ScriptSaveFixture("set saved to 1\nForceSave\nset suffix to 9"))
            Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is not null && fixture.Value(1) == 1 &&
                fixture.Value(2) == 0 && fixture.Owner.Receipt is null,
                "Diagnostic SCTX manufactured a persistent SCDA receipt.");
        using var function = new ScriptSaveFixture("", functionBody: "ForceSave\nSetFunctionValue 9");
        Reject(() => function.Scripts.InvokeFunction(new("Saves.esm", 0x52), null, [], 0));
        Require(function.Owner.Receipt is null, "A diagnostic global function invented an authoritative save instruction.");
    }

    private static void QuestOwner()
    {
        using var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once());
        fixture.Bind(fixture.Write);
        fixture.Quests.SetRunning(fixture.Quest, true);
        fixture.Scheduler.AdvanceClaimed(fixture.Quest, 0, fixture.Host);
        Require(ReferenceEquals(fixture.Scheduler.ScriptManualSaves, fixture.Owner) &&
            fixture.Quests.Variable(fixture.Quest, 1) == 1 && fixture.Quests.Variable(fixture.Quest, 2) == 7 &&
            fixture.Owner.Receipt!.Sites.Single().Caller == fixture.Quest,
            "Actual compiled quest dispatch did not share the queue and original quest local owner.");
        fixture.AdvancePhase();
        Require(fixture.Owner.Drain(() => null), "Retired compiled quest save failed to drain.");
        using var unowned = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once());
        unowned.Bind(unowned.Write);
        unowned.Quests.SetRunning(unowned.Quest, true);
        Reject(() => unowned.Scheduler.AdvanceClaimed(unowned.Quest, 0, unowned.Host with { ExecuteCompiledProgram = null }));
        Require(unowned.Owner.Order.Requests.Count == 0 && unowned.Quests.Variable(unowned.Quest, 1) == 0,
            "Missing compiled executor used a diagnostic source/void fallback.");
    }

    internal static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    internal static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Unsupported source-save ownership was admitted.");
    }
}

internal sealed record CompiledManualSaveSnapshot(RuntimeSaveRequestOrderSnapshot Order,
    FalloutQuestScriptsSnapshot Scripts, IReadOnlyList<FalloutQuestSnapshot> Quests,
    IReadOnlyList<FalloutReferenceSnapshot> References, string Schema = "opennv-authored-manual-save/v1");

// Independently authored ESM bytes pass through the ordinary full reader and
// actual compiled executor. No diagnostic statement is a save authority.
internal sealed class CompiledManualSaveFixture : IDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly string _sourcePath, _sourceHash;
    private readonly bool _ownsRecords;
    private readonly RuntimeSaveSlotCatalog _catalog;
    internal FalloutPluginStack Records { get; }
    internal FalloutReferenceWorld World { get; }
    internal FalloutQuestState Quests { get; }
    internal FalloutQuestScripts Scheduler { get; }
    internal FalloutReferenceScripts Scripts { get; }
    internal FalloutScriptManualSaveRequests Owner => World.ScriptManualSaves;
    internal RuntimeSaveRequestBinding Binding { get; }
    internal ulong Phase { get; private set; }
    internal Guid Session { get; } = Guid.NewGuid();
    internal FalloutFormKey Caller => new("AuthoredSaves.esm", 0x90);
    internal FalloutFormKey Cell => new("AuthoredSaves.esm", 0x80);
    internal FalloutFormKey Quest => new("AuthoredSaves.esm", 0x60);
    internal FalloutFormKey Player => Records.RuntimeFormKey(0x14);
    internal FalloutCompiledScriptProgram Program { get; }
    internal FalloutCompiledEvent Block => Program.Events.Single(block => block.Event == 2);
    internal FalloutQuestScriptHost Host => new((_, _) => throw new InvalidDataException("Diagnostic stage fallback."),
        _ => throw new InvalidDataException("Diagnostic value fallback."),
        (_, _, _, _) => throw new InvalidDataException("Diagnostic executor fallback."), ExecuteCompiledProgram: Scripts.ExecuteProgram);

    internal CompiledManualSaveFixture(byte[] body, string? artifactDirectory = null, ulong phase = 0)
    {
        _directory = artifactDirectory is null ? Directory.CreateTempSubdirectory("opennv-compiled-manual-save-") :
            Directory.CreateDirectory(Path.Combine(artifactDirectory, "compiled-manual-save-" + Guid.NewGuid().ToString("N")));
        _sourcePath = Path.Combine(_directory.FullName, "AuthoredSaves.esm");
        File.WriteAllBytes(_sourcePath, Fixture(body));
        _sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_sourcePath))).ToLowerInvariant();
        Records = FalloutPluginStack.Load(_directory.FullName, ["AuthoredSaves.esm"]); _ownsRecords = true;
        World = new(Records); Quests = new(Records); Phase = phase;
        World.LoadCell(FalloutCellSceneReader.Read(Records, Cell));
        Scheduler = CreateScheduler(); Scripts = CreateScripts();
        Binding = CreateBinding(); Owner.BindSelection(Binding, () => Phase);
        _catalog = CreateCatalog();
        var record = Records.GetEffective(new("AuthoredSaves.esm", 0x50));
        Program = FalloutCompiledScriptProgram.Read(record, FalloutScriptScope.Standalone(record), standalone: true);
    }

    private CompiledManualSaveFixture(CompiledManualSaveFixture original, string path)
    {
        _directory = original._directory; _sourcePath = original._sourcePath; _sourceHash = original._sourceHash;
        Records = original.Records; _ownsRecords = false;
        var saved = JsonSerializer.Deserialize<CompiledManualSaveSnapshot>(File.ReadAllText(path)) ??
            throw new InvalidDataException("Authored checkpoint is absent.");
        World = new(Records); Quests = new(Records); Quests.Restore(saved.Quests); World.Restore(saved.References);
        World.LoadCell(FalloutCellSceneReader.Read(Records, Cell));
        Scheduler = CreateScheduler(); Scheduler.Restore(saved.Scripts); Scripts = CreateScripts();
        Binding = CreateBinding(); Owner.BindSelection(Binding, () => Phase); _catalog = CreateCatalog(); Program = original.Program;
        Owner.RestoreOrder(RuntimeSaveRequestColdLoad.Read(path, Binding.SourceCompatibilityId, Records, saved.Order, saved.Scripts));
        Bind(_ => throw new InvalidDataException("Cold completed request replayed its original writer."));
    }

    private FalloutQuestScripts CreateScheduler() => new(Records, Quests, new HashSet<FalloutFormKey> { Quest }, new(),
        references: World, defaultProcessingDelay: 0);
    private FalloutReferenceScripts CreateScripts() => new(Records, World, Quests, new((_, _) => false,
        _ => throw new InvalidDataException("Unexpected authored save effect."),
        Command: (_, _, _, _) => throw new InvalidDataException("Compiled save called diagnostic command fallback.")));
    private RuntimeSaveRequestBinding CreateBinding()
    {
        var path = Path.Combine(_directory.FullName, "continue.json");
        return new("authored-save-source:" + _sourceHash, path,
            id => Path.Combine(path + RuntimeSaveSlotCatalog.SlotDirectorySuffix, id.ToString("N") + ".json"));
    }
    private RuntimeSaveSlotCatalog CreateCatalog() => new(Binding.ContinuePath, root =>
    {
        var saved = root.Deserialize<CompiledManualSaveSnapshot>() ?? throw new InvalidDataException("Authored save is absent.");
        if (saved.Schema != "opennv-authored-manual-save/v1") throw new InvalidDataException("Wrong authored save schema.");
        FalloutReferenceSnapshot.Validate(saved.References);
        RuntimeSaveRequestOrder.ValidateSnapshot(saved.Order, Binding.SourceCompatibilityId, Records,
            RuntimeSaveRequestColdLoad.SuspendedSlices(saved.Scripts));
        RuntimeSaveRequestOrder.RequirePublishedCapture(saved.Order);
    });
    internal void Bind(Func<Guid, RuntimeSaveSlotMetadata> writer, Action<FalloutScriptManualSaveReceipt>? failed = null) =>
        Owner.Bind(writer, failed ?? (_ => { }), () => Phase, Binding, WriteContinue);
    internal void BindManual(RuntimeManualSaveRequests manual) => manual.Bind(Owner, Session, Binding.SourceCompatibilityId);
    internal RuntimeSaveNativeSite Site(ulong generation) => new(Session, generation, Player, Cell);
    internal void AdvancePhase() => Phase = checked(Phase + 1);
    internal double Value(uint slot) => World.Get(Caller).Read(slot);
    internal FalloutReferenceScriptEventResult Activate() => Scripts.Activate(Caller, Player);
    internal IEnumerable<bool> Steps() => Scripts.CompiledSteps(Caller, Program, Block);
    internal CompiledManualSaveFixture Restore(string path) => new(this, path);
    internal RuntimeSaveSlotMetadata Write(Guid id) => _catalog.Create(id, Capture);
    private RuntimeSaveSlotMetadata WriteContinue(RuntimeSaveRequest request)
    {
        if (Owner.Order.Writing?.Order != request.Order || request.Destination != RuntimeSaveRequestDestination.Continue)
            throw new InvalidDataException("Continue writer lacks its genuine queue head.");
        Capture(); return _catalog.ReadSlot("current");
    }
    private void Capture()
    {
        var order = Owner.CaptureOrder(); var scripts = Scheduler.Capture();
        RuntimeSaveRequestOrder.ValidateSnapshot(order, Binding.SourceCompatibilityId, Records,
            RuntimeSaveRequestColdLoad.SuspendedSlices(scripts));
        RuntimeSaveRequestOrder.RequirePublishedCapture(order);
        File.WriteAllText(Binding.ContinuePath, JsonSerializer.Serialize(new CompiledManualSaveSnapshot(order,
            scripts, Quests.Capture(), World.Capture())));
    }
    internal void RequireSourceUnchanged() => ScriptManualSaveContracts.Require(
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_sourcePath))).Equals(_sourceHash, StringComparison.OrdinalIgnoreCase),
        "Authored immutable source bytes changed during execution/capture.");
    public void Dispose()
    {
        World.Dispose();
        if (_ownsRecords) { Records.Dispose(); _directory.Delete(true); }
    }

    internal static byte[] Once() => Join(IfZero(1), Set(1, 1), Force(), Set(2, 7), Instruction(0x19));
    internal static byte[] Force(bool qualified = false, bool arguments = false) => Join(
        qualified ? Join(U16(0x1c), U16(1)) : [], Instruction(0x1217,
            arguments ? Join(U16(1), [(byte)'n'], BitConverter.GetBytes(1)) : U16(0)));
    internal static byte[] Set(ushort slot, int value) => Assignment(slot, Encoding.ASCII.GetBytes(" " + value));
    internal static byte[] Instruction(ushort opcode, byte[]? payload = null) => Join(U16(opcode), U16(checked((ushort)(payload?.Length ?? 0))), payload ?? []);
    internal static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] IfZero(ushort slot)
    {
        var predicate = Join([(byte)' ', (byte)'f'], U16(slot), Encoding.ASCII.GetBytes(" 0 =="));
        return Instruction(0x16, Join(U16(3), U16(checked((ushort)predicate.Length)), predicate));
    }
    private static byte[] Assignment(ushort slot, byte[] value) => Instruction(0x15,
        Join([(byte)'f'], U16(slot), U16(checked((ushort)value.Length)), value));
    private static byte[] Fixture(byte[] body)
    {
        var main = Join(Instruction(0x1d), BlockBytes(2, body), BlockBytes(0,
            Assignment(2, Join([(byte)' ', (byte)'f'], U16(2), Encoding.ASCII.GetBytes(" 1 +")))));
        var quest = Join(Instruction(0x1d), BlockBytes(0, body));
        return Join(Record("TES4", 0, Field("HEDR", new byte[12])), Record("NPC_", 7),
            Record("ACTI", 1, Field("SCRI", U32(0x50))), Script(0x50, main, 0), Script(0x51, quest, 1),
            Record("QUST", 0x60, Field("DATA", new byte[8]), Field("SCRI", U32(0x51))),
            Record("CELL", 0x80, Field("DATA", [1])), Group(0x80,
                Record("REFR", 0x90, Field("NAME", U32(1)), Field("DATA", new byte[24]))));
    }
    private static byte[] Script(uint id, byte[] code, ushort type)
    {
        var header = new byte[20]; U32(1).CopyTo(header, 4); U32((uint)code.Length).CopyTo(header, 8);
        U32(2).CopyTo(header, 12); U16(type).CopyTo(header, 16); header[18] = 1;
        return Record("SCPT", id, Field("SCHR", header), Field("SCDA", code), Local(1, "saved"), Local(2, "suffix"),
            Field("SCRO", U32(0x90)), Field("SCTX", Encoding.ASCII.GetBytes("Deliberately malformed diagnostic text\0")));
    }
    private static byte[] BlockBytes(ushort type, byte[] body) => Join(
        Instruction(0x10, Join(U16(type), U32((uint)body.Length + 4))), body, Instruction(0x11));
    private static byte[] Local(uint slot, string name)
    {
        var local = new byte[24]; U32(slot).CopyTo(local, 0);
        return Join(Field("SLSD", local), Field("SCVR", Encoding.ASCII.GetBytes(name + '\0')));
    }
    private static byte[] U16(ushort value) => BitConverter.GetBytes(value);
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
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
    private static byte[] Group(uint cell, params byte[][] records)
    {
        var data = Join(records); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        U32((uint)bytes.Length).CopyTo(bytes, 4); U32(cell).CopyTo(bytes, 8); U32(6).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
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
        string gameModeBody = "", string? artifactDirectory = null)
    {
        _directory = artifactDirectory is null ? Directory.CreateTempSubdirectory("opennv-script-save-") :
            Directory.CreateDirectory(Path.Combine(artifactDirectory, "source-save-ordering-" + Guid.NewGuid().ToString("N")));
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
