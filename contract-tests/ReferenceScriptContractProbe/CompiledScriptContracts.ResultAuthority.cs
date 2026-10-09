using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void ResultAuthority()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var assignment = Set('f', 1, " 17", 1);
            var fault = Join(Set('f', 1, " 31", 1), Instruction(0x2f03), Set('f', 2, " 99", 1));
            var malformed = Join(Set('f', 1, " 43", 1), [0x15, 0, 16, 0, 0, 0]);
            byte[] Scope(byte[] code) => Join(Field("SCHR", Header(code, 1)), Field("SCDA", code), Field("SCRO", U32(0x60)));
            byte[] Info(uint id, params byte[][] fields) => Record("INFO", id,
                Join(Field("DATA", [0, 0, 0, 0]), Field("QSTI", U32(0x60)), Join(fields)));
            var path = Path.Combine(directory, "Bytecode.esm");
            File.WriteAllBytes(path, Join(Tes4(),
                Script(0x70, Instruction(0x1d), [Field("SCRV", U32(3))], "string_var value", quest: true),
                Record("QUST", 0x60, Field("EDID", Text("ResultQuest")), Field("DATA", new byte[8]), Field("SCRI", U32(0x70)),
                    Field("INDX", U16(10)), Field("QSDT", [0]), Scope(assignment), Field("QSDT", [0]), Scope(assignment)),
                Info(0x80, Scope(assignment), Field("NEXT", []), Scope(assignment), Field("SCTX", [65, 0, 66, 0])),
                Info(0x81, Scope(fault)), Info(0x82, Scope(malformed)),
                Info(0x83, Field("SCRO", U32(0x60)), Field("SCTX", Text("let ResultQuest.value := \"unowned handle\""))),
                Info(0x84, Field("NEXT", []), Field("NEXT", [])),
                Info(0x85, Field("NEXT", []), Scope([]), Field("SCTX", [65, 0, 66, 0]))));
            var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
            var effects = 0;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => { ++effects; throw new InvalidDataException("Numeric fixture dispatched an effect."); }));
            var info = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x80)));
            Require(info.BeginScript.Length == 0 && info.EndScript.Length == 0,
                "Compiled scope parsed malformed diagnostic SCTX before selecting authority.");
            var begin = FalloutScriptScope.Dialogue(info.Record, true); var end = FalloutScriptScope.Dialogue(info.Record, false);
            var beginProgram = FalloutCompiledScriptProgram.Read(info.Record, begin, false);
            var endProgram = FalloutCompiledScriptProgram.Read(info.Record, end, false);
            Require(begin.ScopeSha256 != end.ScopeSha256 && beginProgram.ProgramSha256 != endProgram.ProgramSha256,
                "Equal compiled bytes collapsed different original begin/end ranges.");
            Reject(() => end.DiagnosticSource());
            var first = scripts.ExecuteResultOwned(info, Key(0x60), true);
            var second = scripts.ExecuteResultOwned(info, Key(0x60), false);
            Require(first.Authority == FalloutScriptResultAuthority.CompiledVanilla && first.CommittedSteps == 1 &&
                first.Invocation != 0 && second.Invocation > first.Invocation && second.CommittedSteps == 1 &&
                quests.Variable(Key(0x60), 1) == 17 && world.ScriptManualSaves.EnteredInvocations == 0,
                "Shared compiled execution failed to issue distinct real completed receipts.");
            var persisted = Copy(first); persisted.Require(begin, Key(0x60));
            foreach (var invalid in new[] { first with { Caller = Key(0x70) }, first with { Invocation = 0 },
                first with { Completed = false }, first with { Authority = FalloutScriptResultAuthority.SourceDiagnostic },
                first with { CommittedSteps = 2 }, first with { ScopeKind = FalloutScriptScopeKind.DialogueEnd },
                first with { FieldStart = first.FieldStart + 1 }, first with { RecordSha256 = new('a', 64) } })
                Reject(() => invalid.Require(begin, Key(0x60)));
            Reject(() => first.Require(end, Key(0x60)));
            Reject(() => FalloutScriptScope.Dialogue(records.GetEffective(Key(0x84)), true));
            var questRecord = records.GetEffective(Key(0x60)); var fields = questRecord.ReadSubrecords().ToArray();
            var stageField = Array.FindIndex(fields, field => field.Signature == "INDX");
            var entries = Enumerable.Range(0, fields.Length).Where(index => fields[index].Signature == "QSDT").ToArray();
            var a = FalloutScriptScope.QuestEntry(questRecord, stageField, entries[0]);
            var b = FalloutScriptScope.QuestEntry(questRecord, stageField, entries[1]);
            Require(a.StageOrdinal == 0 && a.ResultOrdinal == 0 && b.ResultOrdinal == 1 && a.ScopeSha256 != b.ScopeSha256 &&
                FalloutCompiledScriptProgram.Read(questRecord, a, false).ProgramSha256 !=
                FalloutCompiledScriptProgram.Read(questRecord, b, false).ProgramSha256,
                "Identical QSDT result bytes lost reader-owned log ordinals.");
            Reject(() => FalloutScriptScope.QuestEntry(questRecord, stageField, entries[0] + 1));
            var emptyInfo = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x85)));
            var empty = scripts.ExecuteResultOwned(emptyInfo, Key(0x60), false);
            Require(empty.Authority == FalloutScriptResultAuthority.CompiledVanilla && empty.Invocation > second.Invocation &&
                empty.CommittedSteps == 0 && quests.Variable(Key(0x60), 1) == 17,
                "Present empty SCDA was skipped or executed malformed diagnostic source.");
            var physical = FalloutScriptLocals.ReadDeclarations(records.GetEffective(Key(0x70)));
            Require(physical["value"].Kind == FalloutScriptLocalKind.Number && physical["target"].Kind == FalloutScriptLocalKind.Form,
                "SCTX changed physical compiled scalar/reference storage.");
            FalloutScriptLocals.RequireCompiledValue(records.GetEffective(Key(0x70)), 4, int.MinValue);
            FalloutScriptLocals.RequireCompiledValue(records.GetEffective(Key(0x70)), 4, int.MaxValue);
            Reject(() => FalloutScriptLocals.RequireCompiledValue(records.GetEffective(Key(0x70)), 4, .5));
            Reject(() => FalloutScriptLocals.RequireCompiledValue(records.GetEffective(Key(0x70)), 4, (double)int.MaxValue + 1));
            Reject(() => FalloutScriptLocals.RequireCompiledValue(records.GetEffective(Key(0x70)), 3, -1));
            var savedQuest = quests.Capture().Single(value => value.Quest == Key(0x60));
            var invalidValues = new Dictionary<uint, double>(savedQuest.Variables) { [4] = .5 };
            var rejectedCold = new FalloutQuestState(records);
            var coldBefore = JsonSerializer.Serialize(rejectedCold.Capture());
            Reject(() => rejectedCold.Restore([savedQuest with { Variables = invalidValues }]));
            Require(coldBefore == JsonSerializer.Serialize(rejectedCold.Capture()), "Rejected compiled quest storage mutated the cold owner.");
            var valueState = JsonSerializer.Serialize(world.ScriptValues.Capture());
            Reject(() => scripts.ExecuteResultOwned(FalloutDialogueTopic.Decode(records.GetEffective(Key(0x83))), Key(0x60), true));
            Require(quests.Variable(Key(0x60), 1) == 17 && valueState == JsonSerializer.Serialize(world.ScriptValues.Capture()),
                "Diagnostic handle-class drift allocated or changed canonical state before refusal.");
            var before = JsonSerializer.Serialize(quests.Capture());
            Reject(() => scripts.ExecuteResultOwned(FalloutDialogueTopic.Decode(records.GetEffective(Key(0x82))), Key(0x60), true));
            Require(before == JsonSerializer.Serialize(quests.Capture()) && world.ScriptManualSaves.EnteredInvocations == 0,
                "Structural compiled framing failure committed an instruction or retained a lease.");
            Reject(() => scripts.ExecuteResultOwned(FalloutDialogueTopic.Decode(records.GetEffective(Key(0x81))), Key(0x60), true));
            Require(quests.Variable(Key(0x60), 1) == 31 && quests.Variable(Key(0x60), 2) == 0 && effects == 0 &&
                world.ScriptManualSaves.EnteredInvocations == 0,
                "Reached instruction failure lost its genuine prefix or executed the suffix.");
            Reject(() => scripts.ExecuteResultOwned(FalloutDialogueTopic.Decode(records.GetEffective(Key(0x81))), Key(0x60), true));
            Require(quests.Variable(Key(0x60), 1) == 31 && quests.Variable(Key(0x60), 2) == 0,
                "Repeated failed result replayed its committed prefix.");
            Reject(() => world.Capture());
            Require(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "Synthetic input changed.");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("OPENNV_COMPILED_RESULT_AUTHORITY_CONTRACT_PASS originalRanges=true identicalBytesDistinct=true " +
            "stageLogOrdinals=true SCDASelectedBeforeSCTX=true emptySCDAOwned=true actualLeaseReceipt=true " +
            "receiptDriftRefused=true physicalTypedLocals=true coldTypedStorageAtomic=true handleClassDriftRefused=true structuralAtomic=true " +
            "genuineFailurePrefix=true failedColdCaptureRefused=true inputAndNative=false");
    }
}
