using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class CompiledScriptContracts
{
    internal static void OpeningCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-opening-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var diagnostic in new byte[][] { [], Text("EnablePlayerControls\nSetStage AuthoredOpening 99"),
                [65, 0, 66, 0], [0x80, 0] })
            {
                var selected = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(selected);
                var input = Path.Combine(selected, "Bytecode.esm");
                var code = Instruction(0x1061);
                File.WriteAllBytes(input, Join(Tes4(), Record("QUST", 0x20, Field("EDID", Text("AuthoredOpening")),
                    Field("DATA", new byte[8]), Field("INDX", U16(10)), Field("QSDT", [0]),
                    Field("SCHR", Header(code, 0)), Field("SCDA", code),
                    diagnostic.Length == 0 ? [] : Field("SCTX", diagnostic))));
                var hash = SHA256.HashData(File.ReadAllBytes(input));
                using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
                var catalog = FalloutOpeningPlayerControlResolver.ResolveForExecution(records, ["AuthoredOpening"]);
                Require(catalog.ResultDriven && catalog.Stage("AuthoredOpening", 10) is { Source.Length: 0, Commands.Count: 0 },
                    "Original opening catalog parsed or predicted diagnostic controls.");
                Require(FalloutOpeningStageTransitionResolver.Resolve(records, catalog, executeGameMode: true).Transitions.Count == 0,
                    "Actual scheduling invented a transition, wait or attached-script SCTX requirement.");
                Reject(() => FalloutOpeningStageTransitionResolver.Resolve(records, catalog));
                Reject(() => new FalloutOpeningStageMachine(new([]), catalog, "AuthoredOpening", 10));
                Reject(() => FalloutOpeningStageTransitionResolver.AddDialogueWaits(catalog, new([])));
                Reject(() => FalloutOpeningStageTransitionResolver.AddDialogueResults(records, catalog, new([]), "AuthoredOpening", [10]));
                using var world = new OpenNV.Runtime.World.Cells.FalloutReferenceWorld(records);
                var quests = new FalloutQuestState(records); var controls = FalloutPlayerControlState.AllEnabled; var calls = 0;
                var executor = new OpenNV.Runtime.World.Cells.FalloutReferenceScripts(records, world, quests,
                    new((_, _) => false, effect =>
                    {
                        Require(effect.Kind == OpenNV.Runtime.World.Cells.FalloutReferenceEffectKind.PlayerControls,
                            "Opening control contract dispatched an unowned effect.");
                        controls = new FalloutPlayerControlCommand(effect.Enable, effect.Controls!).Apply(controls); ++calls;
                    }));
                var stages = new FalloutQuestStages(records, quests, executor.StageSteps,
                    _ => throw new InvalidDataException("Unconditioned opening evaluated a predicate."));
                stages.Enter(Key(0x20), 10);
                Require(calls == 1 && controls == new FalloutPlayerControlCommand(false, []).Apply(FalloutPlayerControlState.AllEnabled) &&
                    quests.Stage(Key(0x20)) == 10 && stages.CaptureResults().Single() is { Completed: true, Steps: 1 },
                    "Original compiled stage did not produce its one real control effect and closed result.");
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "Opening catalog changed original bytes.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("OPENNV_COMPILED_OPENING_CATALOG_CONTRACT_PASS noSctxPrediction=true originalEffect=true " +
            "noInventedTimer=true noAttachedSctxRequirement=true sourceUnchanged=true ordinaryCampaign=false");
    }
}
