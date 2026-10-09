using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void ConsumerAdmission()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-consumers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var code in new[] { Array.Empty<byte>(), Instruction(0x1e), Instruction(0x2f03) })
            foreach (var diagnostic in new[] { "ContradictorySourceMustNeverRun", "malformed", "duplicate" })
            {
                var selected = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(selected);
                var input = Path.Combine(selected, "Bytecode.esm");
                File.WriteAllBytes(input, ConsumerFixture(code, diagnostic));
                var sourceHash = SHA256.HashData(File.ReadAllBytes(input));
                using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
                var package = FalloutScriptPackage.Read(records.GetEffective(Key(0x400)));
                var program = package.EventPrograms["POBA"];
                var sourceCalls = 0;
                var lifecycle = new FalloutPackageEvents((owner, kind) => owner.EventPrograms[kind].ExecuteScript(_ => ++sourceCalls));
                ConsumerRefusal(() => lifecycle.Change(package));
                Require(sourceCalls == 0 && lifecycle is { Done: false, Error: not null, Revision: 1, LastEvent: "POBA" },
                    "Unsupported package bytecode ran diagnostic effects or concealed its failed admission.");
                ConsumerRefusal(lifecycle.Complete);
                Require(sourceCalls == 0 && lifecycle.Revision == 1, "Failed package admission retried or completed its source prefix.");
                ConsumerRefusal(() => _ = program.Source);

                var terminal = FalloutTerminal.Read(records, Key(0x401));
                Require(terminal.Entries.Single().Program.Source.Length == 0,
                    "Compiled terminal parsed optional diagnostic text before byte authority.");
                ConsumerRefusal(terminal.Entries.Single().Program.RequireSourceExecution);
                var selectionCalls = 0;
                using var world = new FalloutReferenceWorld(records);
                world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
                var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                    _ => throw new InvalidDataException("Bytecode fixture dispatched a diagnostic effect.")));
                FalloutTerminalMenu? menu = null;
                menu = new FalloutTerminalMenu(records, Key(0x900), _ => { },
                    (_, _) => throw new InvalidDataException("Unconditioned fixture evaluated a predicate."), selection =>
                    {
                        ++selectionCalls;
                        menu!.BindResult(selection, executor.ExecuteTerminalResult(selection));
                    });
                var generation = menu.Generation;
                Require(menu.VisibleEntries.Single() is { Selectable: true, Error: null },
                    "A structurally valid compiled terminal row disappeared before its actual reached execution.");
                if (code.SequenceEqual(Instruction(0x2f03)))
                {
                    Reject(() => menu.Select(0, generation));
                    Require(menu.Generation == generation + 1 && menu.LastReceipt is
                        { State: FalloutTerminalSelectionState.Failed, Error: not null } && selectionCalls == 1,
                        "Reached unknown terminal bytecode lost its actual failed selection or invoked diagnostic source.");
                    Reject(() => menu.Select(0, menu.Generation));
                    menu.Close();
                    Reject(() => menu.CaptureClosed([]));
                }
                else
                {
                    menu.Select(0, generation);
                    Require(menu.LastReceipt is { State: FalloutTerminalSelectionState.Succeeded,
                        ResultReceipt.Authority: FalloutScriptResultAuthority.CompiledVanilla } && selectionCalls == 1,
                        "Empty/Return terminal bytecode did not bind its actual shared compiled receipt.");
                    menu.Close();
                    var compiledClosed = menu.CaptureClosed([]);
                    FalloutTerminalMenu.ValidateClosed(records, [compiledClosed], []);
                }
                var unopened = new FalloutTerminalMenu(records, Key(0x900), _ => { }, (_, _) => 0,
                    _ => throw new InvalidDataException("Closed rejection fixture executed a row."));
                unopened.Close();
                var closed = unopened.CaptureClosed([]);
                // An intentional invalid old source receipt cannot be promoted
                // to a binary result. This is rejection input, never execution.
                var forgedSourceReceipt = new FalloutTerminalSavedReceipt(terminal.Record.FormKey, terminal.SourceHash, 0,
                    terminal.Entries[0].Program.Identity, generation, FalloutTerminalSelectionState.Succeeded, null, null);
                Reject(() => FalloutTerminalMenu.ValidateClosed(records, [closed with { Receipt = forgedSourceReceipt }], []));

                var function = records.GetEffective(Key(0x402));
                ConsumerRefusal(() => FalloutUserFunction.Read(function));
                ConsumerRefusal(() => FalloutCompiledScriptProgram.RequireDiagnosticOnly(function,
                    function.ReadSubrecords().ToArray(), "ScriptEffectStart"));
                LegacyResultAdmission(records);
                Require(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "Consumer admission changed its authored input.");
            }

            var control = Path.Combine(directory, "source-diagnostic-control"); Directory.CreateDirectory(control);
            File.WriteAllBytes(Path.Combine(control, "Bytecode.esm"), ConsumerFixture(null, "Return"));
            using var sourceRecords = FalloutPluginStack.Load(control, ["Bytecode.esm"]);
            var sourcePackage = FalloutScriptPackage.Read(sourceRecords.GetEffective(Key(0x400)));
            var diagnosticCalls = 0; sourcePackage.EventPrograms["POBA"].ExecuteScript(_ => ++diagnosticCalls);
            Require(diagnosticCalls == 1 && !sourcePackage.EventPrograms["POBA"].Fields.Any(field => field.Signature == "SCDA"),
                "SCDA-absent source-diagnostic control lost its explicit authority.");
            FalloutTerminal.Read(sourceRecords, Key(0x401)).Entries.Single().Program.RequireSourceExecution();
            Require(FalloutUserFunction.Read(sourceRecords.GetEffective(Key(0x402))).Parameters.Count == 0,
                "SCDA-absent function diagnostic parser lost its source control.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("OPENNV_COMPILED_CONSUMER_ADMISSION_CONTRACT_PASS presentAndEmpty=true beforeDiagnostic=true " +
            "packagePrefixRefused=true terminalRowsVisible=true terminalLegacyRefused=true stageLegacyRefused=true " +
            "recordedPackageLegacyRefused=true futureEventNotExecuted=true functionRefused=true magicRefused=true " +
            "sourceDiagnosticControl=true originalInputUnchanged=true compiledTerminalExecution=true campaign=false");
    }

    private static void ConsumerRefusal(Action action)
    {
        try { action(); }
        catch (NotSupportedException error) when (error.Message.Contains("diagnostic SCTX fallback is refused", StringComparison.Ordinal)) { return; }
        throw new InvalidDataException("Unsupported original compiled family reached diagnostic parsing/effects or lost its named refusal.");
    }

    private static void LegacyResultAdmission(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidDataException("Admission fixture invented an effect.")));
        var stages = new FalloutQuestStages(records, quests, executor.StageSteps,
            _ => throw new InvalidDataException("Unconditioned admission fixture evaluated a predicate."));
        try { stages.Enter(Key(0x20), 10); }
        catch (NotSupportedException error) when (error.Message.Contains("2f03", StringComparison.Ordinal)) { }
        // A genuine closed byte result is labelled with the old schema only as
        // rejection input. The old aggregate shape cannot prove its authority.
        var result = stages.CaptureResults();
        foreach (var schema in new[] { "opennv-native-fnv-campaign-save/v48", "opennv-native-fnv-campaign-save/v49" })
        {
            var state = new FalloutNativeCampaignState(schema, "", default, "", 0, "", null!, null!, [], [], [], [], [], [], [],
                QuestStageResults: result);
            var rejected = false;
            try { FalloutNativeCampaignSave.ValidateResultAuthorityVersion(state); }
            catch (NotSupportedException error) when (error.Message == "Result authority requires the current campaign state schema.")
            { rejected = true; }
            Require(rejected, "Old closed quest-stage aggregate was promoted to compiled result authority.");
            FalloutNativeCampaignSave.ValidateResultAuthorityVersion(state with { Schema = FalloutNativeCampaignSave.ExpectedSchema });

            var absentStage = new FalloutQuestStages(records, new FalloutQuestState(records), executor.StageSteps,
                _ => throw new InvalidDataException("Absent stage result evaluated a predicate."));
            absentStage.Enter(Key(0x20), 20);
            FalloutNativeCampaignSave.ValidateResultAuthorityVersion(state with
                { Schema = FalloutNativeCampaignSave.ExpectedSchema, QuestStageResults = absentStage.CaptureResults() });
        }

        var source = records.GetEffective(Key(0x400));
        var hash = Convert.ToHexString(SHA256.HashData(source.ReadData()));
        // These intentionally invalid legacy consumed histories must fail at
        // byte authority before a cold world allocates or changes an owner.
        foreach (var done in new[] { false, true })
        {
            var saved = new FalloutReferenceSnapshot(Key(0x900), Key(0x800), Key(0x401), null, null,
                new Dictionary<uint, double>(), null, PackageAssignment: new(source.FormKey, hash, done));
            using var cold = new FalloutReferenceWorld(records);
            ConsumerRefusal(() => cold.Restore([saved]));
            Require(cold.InstanceCount == 0, "Unsupported consumed package history allocated a cold reference.");
        }
        var future = records.GetEffective(Key(0x403));
        var declared = new FalloutReferenceSnapshot(Key(0x900), Key(0x800), Key(0x401), null, null,
            new Dictionary<uint, double>(), null,
            PackageAssignment: new(future.FormKey, Convert.ToHexString(SHA256.HashData(future.ReadData())), false));
        FalloutReferenceWorld.ValidateRecordedPackageResults(records, [declared]);
        ConsumerRefusal(() => FalloutReferenceWorld.ValidateRecordedPackageResults(records,
            [declared with { PackageAssignment = declared.PackageAssignment! with { Done = true } }]));
    }

    private static byte[] ConsumerFixture(byte[]? code, string diagnostic)
    {
        byte[][] Body(bool function = false)
        {
            var text = diagnostic == "malformed" ? new byte[] { 65, 0, 66, 0 } :
                Text(function ? "begin Function {}\n" + diagnostic + "\nend" : diagnostic);
            return [Field("SCHR", Header(code ?? [], 0)),
                .. (code is null ? Array.Empty<byte[]>() : new[] { Field("SCDA", code) }), Field("SCTX", text),
                .. (diagnostic == "duplicate" ? new[] { Field("SCTX", text) } : Array.Empty<byte[]>())];
        }
        var packageData = new byte[12]; packageData[4] = 6;
        var reference = Record("REFR", 0x900, Field("NAME", U32(0x401)), Field("DATA", new byte[24]));
        var group = new byte[24 + reference.Length];
        System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6);
        reference.CopyTo(group, 24);
        return Join(Tes4(),
            Record("PACK", 0x400, Field("EDID", Text("ConsumerPackage")), Field("PKDT", packageData), Field("POBA", []), Join(Body())),
            Record("PACK", 0x403, Field("EDID", Text("FutureEventPackage")), Field("PKDT", packageData),
                Field("POBA", []), Field("SCHR", Header([], 0)), Field("POEA", []),
                Field("SCHR", Header(code ?? [], 0)), Field("SCDA", code ?? [])),
            Record("QUST", 0x20, Field("DATA", new byte[8]), Field("INDX", U16(10)), Field("QSDT", [0]), Join(Body()),
                Field("INDX", U16(20)), Field("QSDT", [0]), Field("SCHR", Header([], 0))),
            Record("TERM", 0x401, Field("EDID", Text("ConsumerTerminal")), Field("DESC", Text("")), Field("DNAM", [0, 2, 0, 0]),
                Field("ITXT", Text("Original row")), Field("RNAM", Text("")), Field("ANAM", [0]), Join(Body())),
            Record("SCPT", 0x402, Join(Body(function: true))),
            Record("CELL", 0x800, Field("DATA", [1])), group);
    }
}
