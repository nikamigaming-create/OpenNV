using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class TerminalContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-terminal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "TerminalBase.esm"), Base());
            File.WriteAllBytes(Path.Combine(directory, "TerminalNotes.esm"), Notes());
            File.WriteAllBytes(Path.Combine(directory, "TerminalPatch.esp"), Patch());
            using var records = FalloutPluginStack.Load(directory, ["TerminalBase.esm", "TerminalNotes.esm", "TerminalPatch.esp"]);
            VerifyReader(records);
            VerifySuccessAndCold(records);
            VerifyFailure(records);
            VerifyPresentationFailure(records);
            VerifyScopedAndNestedFailure(records);
            VerifyClosedStageResult(records);
            VerifyAdmissionAndConditions(records);
            VerifyMasterContext(directory);
            VerifyOutputGuard(directory);
            var brightness = System.Xml.Linq.XElement.Parse("<value><copy>11</copy><onlyif>entity_HighDef</onlyif>" +
                "<add><copy>7</copy><onlyifnot>entity_highdef</onlyifnot></add></value>");
            Require(FalloutMenuXml.Number(brightness, (_, _) => throw new InvalidOperationException()) == 11,
                "The case-insensitive desktop highdef shortcut selected the wrong source brightness arm.");
            Console.WriteLine("OPENNV_TERMINAL_CONTRACT_PASS orderedSourceEntries=true emptyResults=true " +
                "declaringMasterScope=true actualCaller=true sourceEffects=true conditionScope=true " +
                "prefixFailureNoReplay=true coldSettledEffects=true textNote=true " +
                "embeddedLocalsAndCompiledExecution=unbound hackingAndMatchedMenuTiming=unverified");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void VerifyReader(FalloutPluginStack records)
    {
        var terminal = FalloutTerminal.Read(records, A(0x20));
        Require(terminal.Record.Plugin.Name == "TerminalPatch.esp" && terminal.Entries.Count == 8 &&
            terminal.Entries.Select(entry => entry.Index).SequenceEqual(Enumerable.Range(0, 8)),
            "Winning terminal or original ordered entry ordinals were lost.");
        Require(terminal.Entries[1].Submenu == B(0x31) && terminal.Password == B(0x30) &&
            terminal.Entries[0].Program.References.Select(reference => reference.Form)
                .SequenceEqual(new FalloutFormKey?[] { A(0x91), A(0x92), A(0x60) }),
            "Terminal links/result references were adjusted through load order instead of declaring masters.");
        Require(terminal.Entries[1].ResultText == "" && terminal.Entries[1].Program.CompiledSize == 0 &&
            !terminal.Entries[1].Program.HasSource && terminal.Entries[1].Program.Fields.Count == 1,
            "An authored empty, SCHR-only result or padded empty RNAM was rejected.");
        terminal.Entries[1].Program.RequireSourceExecution();
        Require(terminal.Entries.Select(entry => entry.Program.Identity).Distinct().Count() == 8,
            "Different source ordinals shared a fragment identity.");
        foreach (var id in Enumerable.Range(0x70, 10)) Reject(() => FalloutTerminal.Read(records, A((uint)id)));
        Reject(() => FalloutTerminal.Read(records, A(0x10)));
        var compiledOnly = FalloutTerminal.Read(records, A(0x7a)).Entries.Single().Program;
        Reject(compiledOnly.RequireSourceExecution);
        var locals = FalloutTerminal.Read(records, A(0x7b)).Entries.Single().Program;
        Require(locals.Locals.Single() == new FalloutTerminalLocal(1, 0, "transient") &&
            locals.References.Single().LocalIndex == 1, "Embedded local metadata was discarded.");
        Reject(locals.RequireSourceExecution);
        Require(FalloutNote.Read(records, B(0x30)).RequireText() == "A first-party synthetic report.",
            "Source text NOTE was not decoded through its declared DATA/TNAM variant.");
        foreach (var id in new uint[] { 0x32, 0x33, 0x34 }) Reject(() => FalloutNote.Read(records, B(id)).RequireText());
        Reject(() => FalloutNote.Read(records, B(0x35)));
        Reject(() => FalloutNote.Read(records, B(0x36)));
    }

    private static void VerifySuccessAndCold(FalloutPluginStack records)
    {
        using var world = World(records);
        var quests = new FalloutQuestState(records);
        var inventory = new FalloutPlayerInventory();
        var effects = new List<FalloutReferenceScriptEffect>();
        var states = new Dictionary<FalloutFormKey, int> { [A(0x91)] = 3, [A(0x92)] = 3, [A(0x90)] = 0 };
        var scripts = Scripts(records, world, quests, effects, states);
        var conditions = new FalloutTerminalConditions(records, quests, world, reference => states[reference]);
        var linked = new FalloutCondition(records.GetEffective(A(0x20)), 0, 3, 157, 0, 0, 4, 0);
        Require(world.GetLinkedRef(A(0x90)) == A(0x91) && conditions.Evaluate(A(0x90), linked) == 3,
            "Terminal linked predicate borrowed the terminal pivot or a raw declaring-plugin FormID.");
        Reject(() => conditions.Evaluate(A(0x90), linked with { Reference = 0x01000092 }));
        Reject(() => conditions.Evaluate(A(0x90), linked with { RunOn = 3 }));
        Reject(() => conditions.Evaluate(A(0x93), linked));
        Require(conditions.Evaluate(A(0x90), linked with { Function = 5 }) == world.GetLocked(A(0x91)) &&
            conditions.Evaluate(A(0x90), linked with { Function = 65 }) == world.GetLockLevel(A(0x91)),
            "Linked terminal lock predicates did not use the current shared access owner.");
        var menu = Menu(records, world, conditions, scripts, inventory);
        Reject(menu.RequireSaveable);
        var generation = menu.Generation;
        var selected = menu.Select(0, generation);
        Require(selected.Reference == A(0x90) && selected.Page.Record.FormKey == A(0x20) &&
            quests.Stage(A(0x60)) == 10 && effects.Count == 3 &&
            effects[0] is { Kind: FalloutReferenceEffectKind.DoorOpenState, Enable: true } && effects[0].Target == A(0x91) &&
            effects[1].Target == A(0x92) && effects.All(effect => effect.Source == A(0x90)) &&
            menu.LastReceipt?.State == FalloutTerminalSelectionState.Succeeded && menu.HasResult,
            "Terminal source result did not forward ordered effects with the actual placed calling reference.");
        Reject(() => menu.Select(0, generation));
        Require(effects.Count == 3 && menu.DisplayText == "Gate command accepted.", "Duplicate input replayed a result.");
        Require(menu.Back() && menu.VisibleEntries.All(value => value.Entry.Index != 0),
            "ForceRedraw did not reevaluate actual source guards after the result.");
        menu.Select(1, menu.Generation);
        Require(menu.CurrentPage.Record.FormKey == B(0x31) && menu.Reference == A(0x90) && menu.CanBack,
            "A source submenu replaced the actual placed calling reference.");
        menu.Select(0, menu.Generation);
        Require(menu.DisplayNote == B(0x30) && inventory.Item(B(0x30))?.Count == 1 &&
            FalloutNote.Read(records, menu.DisplayNote!.Value).RequireText().Length != 0,
            "AddNote/display-note metadata did not join the shared player inventory.");
        Require(menu.Back() && menu.Back() && menu.CurrentPage.Record.FormKey == A(0x20), "Submenu back stack was lost.");
        menu.Select(5, menu.Generation); Require(menu.Back(), "Repeatable result did not return to its page.");
        menu.Select(5, menu.Generation); Require(menu.Back(), "Second explicit selection did not return to its page.");
        Require(quests.Variable(A(0x60), 1) == 2, "Successful distinct selections were incorrectly made once-per-entry.");
        menu.Close(); menu.RequireSaveable();
        var snapshot = world.Capture(); var questSnapshot = quests.Capture(); var inventorySnapshot = inventory.Capture();
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(snapshot); cold.LoadCell(FalloutCellSceneReader.Read(records, A(0x80)));
        var restoredQuests = new FalloutQuestState(records); restoredQuests.Restore(questSnapshot);
        var restoredInventory = new FalloutPlayerInventory();
        restoredInventory.Restore(inventorySnapshot.Inventory, inventorySnapshot.EquippedRuntimeFormIds.ToArray(), inventorySnapshot.InventoryRandomState);
        var coldEffects = new List<FalloutReferenceScriptEffect>();
        var coldStates = new Dictionary<FalloutFormKey, int> { [A(0x91)] = 3, [A(0x92)] = 3, [A(0x90)] = 0 };
        var coldScripts = Scripts(records, cold, restoredQuests, coldEffects, coldStates);
        var coldConditions = new FalloutTerminalConditions(records, restoredQuests, cold, reference => coldStates[reference]);
        Require(coldConditions.Evaluate(A(0x90), linked) == 3,
            "Cold terminal linked predicate lost the unchanged authored reference binding.");
        var restoredMenu = Menu(records, cold, coldConditions, coldScripts, restoredInventory);
        Require(restoredQuests.Stage(A(0x60)) == 10 && restoredQuests.Variable(A(0x60), 1) == 2 &&
            restoredInventory.Item(B(0x30))?.Count == 1 && coldEffects.Count == 0 &&
            restoredMenu.VisibleEntries.All(value => value.Entry.Index != 0),
            "Cold settled state lost existing persisted effects or replayed source initialization/results.");
        restoredMenu.Close(); restoredMenu.RequireSaveable();
    }

    private static void VerifyFailure(FalloutPluginStack records)
    {
        using var world = World(records);
        var quests = new FalloutQuestState(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        var states = new Dictionary<FalloutFormKey, int> { [A(0x91)] = 3, [A(0x92)] = 3 };
        var scripts = Scripts(records, world, quests, effects, states);
        var conditions = new FalloutTerminalConditions(records, quests, world, reference => states[reference]);
        var menu = Menu(records, world, conditions, scripts, new());
        var generation = menu.Generation;
        Reject(() => menu.Select(3, generation));
        var receipt = menu.LastReceipt;
        Require(receipt is { State: FalloutTerminalSelectionState.Failed } && receipt.Selection.Entry.Index == 3 &&
            quests.Variable(A(0x60), 1) == 1 && world.GetLocked(A(0x91)) == 1 && world.GetLockLevel(A(0x91)) == 100,
            "Failed terminal suffix discarded its source receipt or already consumed prefix.");
        Reject(() => menu.Select(3, generation)); Reject(menu.Refresh); Reject(() => menu.Back()); Reject(menu.RequireSaveable);
        menu.Close(); Reject(menu.RequireSaveable); Reject(menu.Refresh);
        Require(menu.Error is not null && menu.LastReceipt == receipt && quests.Variable(A(0x60), 1) == 1,
            "Closing or repainting acknowledged a source failure or replayed its prefix.");
    }

    private static void VerifyScopedAndNestedFailure(FalloutPluginStack records)
    {
        using (var world = World(records))
        {
            var quests = new FalloutQuestState(records); var effects = new List<FalloutReferenceScriptEffect>();
            var states = new Dictionary<FalloutFormKey, int> { [A(0x91)] = 3, [A(0x92)] = 3 };
            var scripts = Scripts(records, world, quests, effects, states);
            var menu = Menu(records, world, new(records, quests, world, reference => states[reference]), scripts, new());
            Reject(() => menu.Select(4, menu.Generation));
            Require(effects.Count == 0 && menu.Error?.Contains(A(0x90).ToString(), StringComparison.Ordinal) == true &&
                menu.LastReceipt is { State: FalloutTerminalSelectionState.Failed },
                $"An adjacent terminal entry lent its compiled DoorB reference to a result: effects={effects.Count}; error={menu.Error}.");
        }
        using (var world = World(records))
        {
            var quests = new FalloutQuestState(records); var effects = new List<FalloutReferenceScriptEffect>();
            var states = new Dictionary<FalloutFormKey, int> { [A(0x91)] = 3, [A(0x92)] = 3 };
            var scripts = Scripts(records, world, quests, effects, states);
            var menu = Menu(records, world, new(records, quests, world, reference => states[reference]), scripts, new());
            Reject(() => menu.Select(6, menu.Generation));
            Require(quests.Stage(A(0x60)) == 11 && quests.Variable(A(0x60), 1) == 1 && effects.Count == 2 &&
                menu.LastReceipt is { State: FalloutTerminalSelectionState.Failed },
                "A nested quest-result failure lost earlier terminal effects or its own consumed prefix.");
            Reject(() => menu.Select(6, menu.Generation)); Require(quests.Variable(A(0x60), 1) == 1, "Nested source effects replayed.");
        }
    }

    private static void VerifyPresentationFailure(FalloutPluginStack records)
    {
        using var world = World(records);
        var quests = new FalloutQuestState(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        var states = new Dictionary<FalloutFormKey, int> { [A(0x91)] = 3, [A(0x92)] = 3 };
        var scripts = Scripts(records, world, quests, effects, states);
        var menu = Menu(records, world, new(records, quests, world, reference => states[reference]), scripts, new());
        menu.ReportPresentationFailure(new InvalidDataException("source tile failure"));
        menu.ReportPresentationFailure(new InvalidOperationException("later failure"));
        Reject(() => menu.Select(0, menu.Generation));
        menu.Close();
        Reject(menu.RequireSaveable);
        Require(menu.Error == "source tile failure" && menu.LastReceipt is null && effects.Count == 0,
            "A presentation failure was lost on close, replaced, or allowed an unreviewable source result.");
    }

    private static void VerifyAdmissionAndConditions(FalloutPluginStack records)
    {
        using var world = World(records); var quests = new FalloutQuestState(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        var observed = new List<FalloutFormKey>();
        int State(FalloutFormKey reference) { observed.Add(reference); return reference == A(0x91) ? 3 : 1; }
        var conditions = new FalloutTerminalConditions(records, quests, world, State);
        var scripts = Scripts(records, world, quests, effects, new() { [A(0x91)] = 3, [A(0x92)] = 3 });
        var menu = Menu(records, world, conditions, scripts, new());
        Require(observed.Contains(A(0x91)) && !observed.Contains(A(0x14)) &&
            menu.VisibleEntries.Single(value => value.Entry.Index == 2) is { Selectable: false, Error: not null },
            "Terminal predicates assumed the player or silently hid an unsupported owner.");
        Reject(() => menu.Select(2, menu.Generation)); Require(menu.LastReceipt is null, "Blocked predicate applied a source result.");
        Require(menu.VisibleEntries.Single(value => value.Entry.Index == 7) is { Selectable: false, Error: not null },
            "Unknown AddNote/result ordering was advertised as owned execution.");
        Reject(() => menu.Select(7, menu.Generation));
        Require(quests.Variable(A(0x60), 1) == 0 && menu.LastReceipt is null,
            "Blocked mixed note/result entry consumed an invented prefix.");
        var term = FalloutTerminal.Read(records, A(0x20));
        var root = new FalloutCondition(term.Record, 0, 1, 157, 0, 0, 0, 0);
        Require(conditions.Evaluate(A(0x90), root) == 1 && observed[^1] == A(0x90), "Default terminal subject was rebound.");
        Reject(() => conditions.Evaluate(A(0x90), root with { RunOn = 1 }));
        Reject(() => conditions.Evaluate(A(0x90), root with { RunOn = 2, Reference = 0 }));
        Reject(() => conditions.Evaluate(A(0x90), root with { RunOn = 2, Reference = 0x01000001 }));
        world.LockReference(A(0x90), 255);
        Reject(() => menu.Select(1, menu.Generation));
        var password = new FalloutPlayerInventory();
        Require(!world.UnlockWithKey(A(0x90), password), "Terminal unlocked without its typed PNAM note.");
        password.Add(records, B(0x30), 1, 1, true);
        Require(world.UnlockWithKey(A(0x90), password), "Terminal password did not use existing shared access.");
        world.UnloadCell(A(0x80)); Reject(menu.Refresh); Require(effects.Count == 0, "Unloaded terminal executed a result.");
    }

    private static FalloutReferenceWorld World(FalloutPluginStack records)
    { var world = new FalloutReferenceWorld(records); world.LoadCell(FalloutCellSceneReader.Read(records, A(0x80))); return world; }

    private static FalloutReferenceScripts Scripts(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutQuestState quests, List<FalloutReferenceScriptEffect> effects, Dictionary<FalloutFormKey, int> states)
    {
        FalloutQuestStages? stages = null;
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            effects.Add(effect);
            if (effect.Kind == FalloutReferenceEffectKind.DoorOpenState) states[effect.Target!.Value] = effect.Enable ? 1 : 3;
            else if (effect.Kind == FalloutReferenceEffectKind.SetStage) stages!.Enter(effect.Target!.Value, effect.Stage);
            else throw new NotSupportedException("Synthetic terminal host received an unrelated effect.");
        }, GetOpenState: reference => states[reference]));
        stages = new(records, quests, scripts.StageSteps, _ => throw new NotSupportedException("Synthetic stage predicate is unbound."));
        return scripts;
    }

    private static FalloutTerminalMenu Menu(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutTerminalConditions conditions, FalloutReferenceScripts scripts, FalloutPlayerInventory inventory) =>
        new(records, A(0x90), reference =>
        {
            if (!world.IsResident(reference) || !world.CanActivate(reference) || world.GetLocked(reference) != 0)
                throw new InvalidOperationException("Synthetic terminal is not resident, available and unlocked.");
        }, conditions.Evaluate, selection =>
        {
            selection.Entry.RequireSelectionEffects();
            if (selection.Entry.Note is { } note) _ = FalloutNote.Read(records, note).RequireText();
            scripts.ExecuteTerminalResult(selection);
            if (selection.Entry.AddNote && selection.Entry.Note is { } added && inventory.Item(added) is null)
                inventory.Add(records, added, 1, 1, true);
        });

    private static void VerifyMasterContext(string directory)
    {
        var path = Path.Combine(directory, "TerminalContext.esp");
        var body = Terminal(0x02000020, Entry("Context", "", 0,
            Program("SetStage SyntheticQuest 10", 0x01000060)));
        File.WriteAllBytes(path, Join(Header("TerminalNotes.esm", "TerminalBase.esm"), body));
        string firstHash; byte[] firstBytes;
        using (var first = FalloutPluginStack.Load(directory, ["TerminalBase.esm", "TerminalNotes.esm", "TerminalContext.esp"]))
        {
            var terminal = FalloutTerminal.Read(first, new("TerminalContext.esp", 0x20));
            firstHash = terminal.SourceHash; firstBytes = terminal.Record.ReadData().ToArray();
            Require(terminal.Entries[0].Program.References.Single().Form == A(0x60), "Initial declaring context was not mapped.");
        }
        File.WriteAllBytes(path, Join(Header("TerminalBase.esm", "TerminalNotes.esm"), body));
        using var swapped = FalloutPluginStack.Load(directory, ["TerminalBase.esm", "TerminalNotes.esm", "TerminalContext.esp"]);
        var changed = FalloutTerminal.Read(swapped, new("TerminalContext.esp", 0x20));
        Require(firstBytes.AsSpan().SequenceEqual(changed.Record.ReadData()) && firstHash != changed.SourceHash &&
            changed.Entries[0].Program.References.Single().Form == B(0x60),
            "Identical TERM bytes concealed a different declaring master/reference context.");
    }

    private static void VerifyOutputGuard(string directory)
    {
        var existing = Path.Combine(directory, "existing.json"); File.WriteAllText(existing, "synthetic-marker");
        Reject(() => OwnedTerminalProbe.OutputPath(existing, directory, directory, []));
        Reject(() => OwnedTerminalProbe.OutputPath(Path.Combine(directory, "new.json"), directory, directory, []));
        var sibling = Path.Combine(Path.GetDirectoryName(directory)!, "opennv-terminal-result-" + Guid.NewGuid().ToString("N") + ".json");
        Require(OwnedTerminalProbe.OutputPath(sibling, directory, directory, []) == Path.GetFullPath(sibling),
            "A fresh result outside selected source roots was refused.");
        Reject(() => OwnedTerminalProbe.OutputPath(Path.ChangeExtension(sibling, ".esm"), directory, directory, []));
        Require(File.ReadAllText(existing) == "synthetic-marker" && !File.Exists(sibling),
            "Output validation overwrote a selected input or wrote an unrequested result.");
    }

    private static byte[] Base() => Join(Header(),
        Record("DOOR", 1, Field("EDID", Text("DoorBase"))), Record("MISC", 2, Field("EDID", Text("WrongLinkType"))),
        Record("NPC_", 0x10), Terminal(0x20),
        Record("SCPT", 0x40, [.. Local(1, "sample"), Field("SCTX", Text("short sample\nbegin GameMode\nend"))]),
        Record("QUST", 0x60, [Field("EDID", Text("SyntheticQuest")), Field("DATA", [0, 0]), Field("SCRI", UInt(0x40)),
            Field("INDX", BitConverter.GetBytes((short)10)), Field("QSDT", [0]),
            Field("INDX", BitConverter.GetBytes((short)11)), Field("QSDT", [0]),
            .. Program("set SyntheticQuest.sample to SyntheticQuest.sample + 1\nUnsupportedNestedResult", 0x60)]),
        Record("CELL", 0x80, Field("DATA", [1])),
        Group(0x80, Reference("ACHR", 0x14, 0x10, "PlayerRef"),
            Record("REFR", 0x90, Field("EDID", Text("TerminalRef")), Field("NAME", UInt(0x20)),
                Field("DATA", new byte[24]), Field("XLKR", UInt(0x91))),
            Reference("REFR", 0x93, 0x20, "TerminalWithoutLink"),
            Reference("REFR", 0x91, 1, "DoorA"), Reference("REFR", 0x92, 1, "DoorB")),
        Terminal(0x70, [Field("ITXT", Text("bad")), Field("RNAM", Text("")), Field("ANAM", [0, 0]), .. Program("")]),
        Terminal(0x71, Entry("bad", "", 8, Program(""))),
        Terminal(0x72, Entry("bad", "", 0, [Field("INAM", [1, 2, 3]), .. Program("")])),
        Terminal(0x73, Entry("bad", "", 0, [Field("INAM", UInt(1)), .. Program("")])),
        Terminal(0x74, Entry("bad", "", 0, [Field("SCHR", ScriptHeader(0, 2, 0)), Field("SCDA", [1])])),
        Terminal(0x75, Entry("bad", "", 0, [.. Program(""), Field("SCTX", Text("")), Field("SCTX", Text(""))])),
        Terminal(0x76, Entry("bad", "", 0, [Field("SCHR", ScriptHeader(1, 0, 0)), Field("SCRO", [1, 2, 3])])),
        Terminal(0x77, Entry("bad", "", 0, [Field("SCHR", ScriptHeader(1, 0, 0)), Field("SCRV", UInt(1))])),
        Terminal(0x78, [Field("ITXT", Text("bad")), Field("ANAM", [0]), .. Program("")]),
        Terminal(0x79, Entry("bad", "", 0, [Field("TNAM", UInt(2)), .. Program("")])),
        Terminal(0x7a, Entry("compiled", "", 0, [Field("SCHR", ScriptHeader(0, 1, 0)), Field("SCDA", [1])])),
        Terminal(0x7b, Entry("locals", "", 0, [Field("SCHR", ScriptHeader(1, 1, 1)), Field("SCDA", [1]),
            Field("SCTX", Text("float transient\nset transient to 1")), .. Local(1, "transient", 0), Field("SCRV", UInt(1))])));

    private static byte[] Notes() => Join(Header("TerminalBase.esm"),
        Record("NOTE", 0x01000030, Field("EDID", Text("SyntheticReport")), Field("FULL", Text("Synthetic report")),
            Field("DATA", [1]), Field("TNAM", Text("A first-party synthetic report."))),
        Terminal(0x01000031, Entry("Read report", "", 1, [Field("INAM", UInt(0x01000030)), .. Program("")])),
        Record("NOTE", 0x01000032, Field("EDID", Text("SyntheticImage")), Field("DATA", [2]), Field("XNAM", Text("synthetic.dds"))),
        Record("SOUN", 0x01000050), Record("DIAL", 0x01000051),
        Record("NOTE", 0x01000033, Field("EDID", Text("SyntheticSound")), Field("DATA", [0]), Field("SNAM", UInt(0x01000050))),
        Record("NOTE", 0x01000034, Field("EDID", Text("SyntheticVoice")), Field("DATA", [3]),
            Field("TNAM", UInt(0x01000051)), Field("SNAM", UInt(0x10))),
        Record("NOTE", 0x01000035, Field("EDID", Text("UnknownNote")), Field("DATA", [4])),
        Record("NOTE", 0x01000036, Field("EDID", Text("WrongVoiceType")), Field("DATA", [3]),
            Field("TNAM", UInt(0x01000050)), Field("SNAM", UInt(0x10))),
        Record("QUST", 0x01000060, Field("EDID", Text("OtherQuest")), Field("DATA", [0, 0])));

    private static byte[] Patch() => Join(Header("TerminalNotes.esm", "TerminalBase.esm"),
        Record("TERM", 0x01000020, [Field("EDID", Text("SyntheticTerminal")), Field("FULL", Text("Synthetic terminal")),
            Field("DESC", Text("Synthetic welcome.")), Field("PNAM", UInt(0x30)), Field("DNAM", [0, 2, 5, 0]),
            .. Entry("Open gate", "Gate command accepted.", 2,
                Program("DoorA.SetOpenState 1\nDoorB.SetOpenState 1\nSetStage SyntheticQuest 10", 0x01000091, 0x01000092, 0x01000060),
                Condition(58, 0x80, 10, 0x01000060), Condition(157, 0, 3, runOn: 2, reference: 0x01000091)),
            .. Entry("Reports", "", 0, [Field("TNAM", UInt(0x31)), .. Program("")]),
            .. Entry("Unknown predicate", "", 0, Program(""), Condition(999, 0, 1)),
            .. Entry("Fail result", "", 0, Program("set SyntheticQuest.sample to SyntheticQuest.sample + 1\n" +
                "DoorA.Lock 100\nUnsupportedTerminalSuffix\nset SyntheticQuest.sample to 99", 0x01000060, 0x01000091)),
            .. Entry("Scoped result", "", 0, Program("DoorB.SetOpenState 0", 0x01000091)),
            .. Entry("Repeat result", "", 0, Program("set SyntheticQuest.sample to SyntheticQuest.sample + 1", 0x01000060)),
            .. Entry("Nested result", "", 0, Program("DoorA.SetOpenState 1\nSetStage SyntheticQuest 11\n" +
                "set SyntheticQuest.sample to 99", 0x01000091, 0x01000060)),
            .. Entry("Mixed result", "", 1, [Field("INAM", UInt(0x30)),
                .. Program("set SyntheticQuest.sample to SyntheticQuest.sample + 1", 0x01000060)])]));

    private static byte[] Terminal(uint id, params byte[][] entries) => Record("TERM", id,
        [Field("EDID", Text("Terminal" + id)), Field("DESC", Text("")), Field("DNAM", [0, 2, 0, 0]), .. entries]);
    private static byte[][] Entry(string text, string result, byte flags, byte[][] program, params byte[][] conditions) =>
        [Field("ITXT", Text(text)), Field("RNAM", result.Length == 0 ? new byte[4] : Text(result)),
            Field("ANAM", [flags]), .. program, .. conditions];
    private static byte[][] Program(string source, params uint[] references) =>
        [Field("SCHR", ScriptHeader((uint)references.Length, 0, 0)),
            .. (source.Length == 0 ? Array.Empty<byte[]>() : new[] { Field("SCTX", Text(source)) }),
            .. references.Select(reference => Field("SCRO", UInt(reference)))];
    private static byte[] ScriptHeader(uint references, uint size, uint locals)
    { var data = new byte[20]; Put(data, 4, references); Put(data, 8, size); Put(data, 12, locals); data[18] = 1; return data; }
    private static byte[][] Local(uint index, string name, byte type = 1)
    { var data = new byte[24]; Put(data, 0, index); data[16] = type; return [Field("SLSD", data), Field("SCVR", Text(name))]; }
    private static byte[] Condition(ushort function, byte flags, float comparison, uint argument = 0, uint runOn = 0, uint reference = 0)
    {
        var data = new byte[28]; data[0] = flags; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), comparison);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), function); Put(data, 12, argument); Put(data, 20, runOn); Put(data, 24, reference);
        return Field("CTDA", data);
    }
    private static byte[] Reference(string type, uint id, uint basis, string name) => Record(type, id,
        Field("EDID", Text(name)), Field("NAME", UInt(basis)), Field("DATA", new byte[24]));
    private static byte[] Header(params string[] masters) => Record("TES4", 0,
        [Field("HEDR", new byte[12]), .. masters.SelectMany(master => new[] { Field("MAST", Text(master)), Field("DATA", new byte[8]) })]);
    private static byte[] Group(uint cell, params byte[][] records)
    {
        var data = Join(records); var group = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        Put(group, 4, (uint)group.Length); Put(group, 8, cell); Put(group, 12, 6); data.CopyTo(group, 24); return group;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        Put(bytes, 4, (uint)data.Length); Put(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] UInt(uint value) => BitConverter.GetBytes(value);
    private static void Put(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static FalloutFormKey A(uint id) => new("TerminalBase.esm", id);
    private static FalloutFormKey B(uint id) => new("TerminalNotes.esm", id);
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException) { return; }
        throw new InvalidOperationException("Invalid or unowned terminal behavior was accepted.");
    }
}
