using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class TerminalContracts
{
    internal static void RunZeroExtent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-terminal-zero-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "TerminalBase.esm"), Join(Header(),
                Terminal(0x20,
                    [.. Entry("Empty", "No commands", 0, [Field("SCHR", ScriptHeader(3, 0, 1)),
                        Field("SCTX", Text("MissingActor.MissingCommand")), Field("SCRO", UInt(0x999)), Field("SCRV", UInt(7))]),
                    .. Entry("Active sibling", "Command dispatched", 0, Program("Activate")),
                    .. Entry("Serialized empty body", "", 0, [Field("SCHR", ScriptHeader(0, 0, 0)), Field("SCDA", [])])]),
                Record("CELL", 0x80, Field("DATA", [1])), Group(0x80, Reference("REFR", 0x90, 0x20, "TerminalRef")),
                Terminal(0x30, Entry("Missing header", "", 0, [Field("SCTX", Text("MissingCommand"))])),
                Terminal(0x31, Entry("Contradictory body", "", 0, [Field("SCHR", ScriptHeader(0, 0, 0)), Field("SCDA", [1])])),
                Terminal(0x32, Entry("Duplicate body", "", 0, [Field("SCHR", ScriptHeader(0, 0, 0)), Field("SCDA", []), Field("SCDA", [])])),
                Terminal(0x33, Entry("Truncated reference", "", 0, [Field("SCHR", ScriptHeader(1, 0, 0)), Field("SCRO", [1, 2, 3])])),
                Terminal(0x34, Entry("Active missing references", "", 0, [Field("SCHR", ScriptHeader(1, 1, 0)), Field("SCDA", [1])])),
                Terminal(0x35, Entry("Truncated header", "", 0, [Field("SCHR", new byte[19])])),
                Terminal(0x36, Entry("Duplicate header", "", 0, [Field("SCHR", ScriptHeader(0, 0, 0)), Field("SCHR", ScriptHeader(0, 0, 0))])),
                Terminal(0x37, Entry("Active missing local", "", 0, [Field("SCHR", ScriptHeader(1, 1, 0)), Field("SCDA", [1]), Field("SCRV", UInt(7))])),
                Terminal(0x38, Entry("Truncated local", "", 0, [Field("SCHR", ScriptHeader(0, 0, 1)), Field("SLSD", new byte[23])])),
                Terminal(0x39, Entry("Unterminated local name", "", 0, [Field("SCHR", ScriptHeader(0, 0, 0)), Field("SCVR", [1])]))));
            using var records = FalloutPluginStack.Load(directory, ["TerminalBase.esm"]);
            var page = FalloutTerminal.Read(records, A(0x20));
            var empty = page.Entries[0].Program;
            Require(empty.CompiledSize == 0 && !empty.HasSource && empty.Source.Length == 0 &&
                empty.References.Count == 0 && empty.Locals.Count == 0 &&
                empty.Fields.Count(field => field.Signature is "SCRO" or "SCRV") == 2 &&
                empty.Fields.Any(field => field.Signature == "SCTX") && page.Entries[1].Program.HasSource &&
                page.Entries[2].Program is { CompiledSize: 0, HasSource: false },
                "Explicit empty metadata became bindings, lost its raw fields, or suppressed an active sibling.");
            foreach (var id in Enumerable.Range(0x30, 10)) Reject(() => FalloutTerminal.Read(records, A((uint)id)));
            using var world = World(records);
            var quests = new FalloutQuestState(records);
            var effects = new List<FalloutReferenceScriptEffect>();
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add));
            var calls = 0;
            void Admit(FalloutFormKey reference)
            {
                if (!world.IsResident(reference) || !world.CanActivate(reference) || world.GetLocked(reference) != 0)
                    throw new InvalidOperationException("Terminal fixture is unavailable or locked.");
            }
            var menu = new FalloutTerminalMenu(records, A(0x90), Admit,
                (_, _) => throw new InvalidDataException("Empty fixture has no condition."),
                selection => { ++calls; scripts.ExecuteTerminalResult(selection); });
            var generation = menu.Generation;
            var before = JsonSerializer.Serialize(world.Capture());
            var selected = menu.Select(0, generation);
            Require(calls == 1 && effects.Count == 0 && menu.LastReceipt?.State == FalloutTerminalSelectionState.Succeeded &&
                before == JsonSerializer.Serialize(world.Capture()), "Actual empty result owner evaluated an unused binding or changed world state.");
            Reject(() => menu.Select(0, generation));
            Require(calls == 1 && effects.Count == 0, "Duplicate selection dispatched the empty owner again.");
            world.UnloadCell(A(0x80));
            Reject(() => scripts.ExecuteTerminalResult(selected));
            world.LoadCell(FalloutCellSceneReader.Read(records, A(0x80)));
            Require(menu.Back(), "Empty result lost the normal Back transition.");
            menu.Close();
            var saved = menu.CaptureClosed([]);
            Require(saved.Receipt?.Entry == 0, "Cold fixture did not capture the empty selection's actual receipt.");
            var savedJson = JsonSerializer.Serialize(saved);
            var worldJson = JsonSerializer.Serialize(world.Capture());
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(worldJson)!);
            cold.LoadCell(FalloutCellSceneReader.Read(records, A(0x80)));
            var coldCalls = 0;
            var restored = FalloutTerminalMenu.RestoreClosed(records,
                JsonSerializer.Deserialize<FalloutTerminalClosedSnapshot>(savedJson)!, [],
                _ => throw new InvalidDataException("Closed restore must not admit a menu."),
                (_, _) => throw new InvalidDataException("Closed restore must not query a condition."), _ => ++coldCalls);
            Require(coldCalls == 0 && !restored.Active && savedJson == JsonSerializer.Serialize(restored.CaptureClosed([])) &&
                cold.Capture().Count > 0 && worldJson == JsonSerializer.Serialize(cold.Capture()),
                "Cold closed-result restoration replayed commands or changed the consumed receipt.");
            var sibling = new FalloutTerminalMenu(records, A(0x90), Admit, (_, _) => 1,
                selection => { ++calls; scripts.ExecuteTerminalResult(selection); });
            sibling.Select(1, sibling.Generation);
            Require(calls == 2 && effects.Count == 1 && effects[0] is { Kind: FalloutReferenceEffectKind.DefaultActivate } &&
                effects[0].Source == A(0x90), "Nonempty sibling did not execute with the actual placed caller.");
            sibling.Close();
            using var locked = World(records);
            locked.LockReference(A(0x90), 100);
            Reject(() => new FalloutTerminalMenu(records, A(0x90), reference =>
            {
                if (locked.GetLocked(reference) != 0) throw new InvalidOperationException("Locked source.");
            }, (_, _) => 1, _ => throw new InvalidDataException("Locked result ran.")));
            Console.WriteLine("OPENNV_TERMINAL_ZERO_EXTENT_PASS explicitHeader=true absentHeaderRefused=true " +
                "actualReaderAndResultOwner=true unusedBindingsNotEvaluated=true nonemptySibling=true duplicateNoReplay=true " +
                "coldReceiptExact=true lockedAndNonresidentRefused=true malformedBodyRefused=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static void RunOwnedZeroExtent(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        foreach (var (key, index, refs, locals) in new[]
        {
            (new FalloutFormKey("DeadMoney.esm", 0x923d), 0, 2u, 1u),
            (new FalloutFormKey("OldWorldBlues.esm", 0x4902), 2, 1u, 0u)
        })
        {
            var record = records.GetEffective(key);
            var original = FalloutTerminal.Hash(record);
            var terminal = FalloutTerminal.Read(records, key);
            var program = terminal.Entries[index].Program;
            var header = FalloutScriptHeader.Read(program.Fields.Single(field => field.Signature == "SCHR").Data.Span);
            Require(header.CompiledBytes == 0 && header.ReferenceCount == refs && header.VariableCount == locals &&
                !program.Fields.Any(field => field.Signature == "SCDA") && program.Fields.Any(field => field.Signature == "SCTX") &&
                program.Source.Length == 0 && !program.HasSource && program.References.Count == 0 && program.Locals.Count == 0,
                "Owned terminal zero-extent premise or inactive binding state changed.");
            program.RequireSourceExecution();
            Require(original == terminal.SourceHash && original == FalloutTerminal.Hash(record), "Owned terminal source changed.");
            Console.WriteLine($"OPENNV_OWNED_TERMINAL_ZERO_EXTENT_PASS terminal={key} entry={index} " +
                "wholeReader=true explicitZeroExtent=true rawFieldsRetained=true sourceUnchanged=true " +
                "placedAccessNativeSelectionAndRetail=unverified");
        }
    }
}
