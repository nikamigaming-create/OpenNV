using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class TerminalContracts
{
    private static void VerifyClosedStageResult(FalloutPluginStack records)
    {
        using var world = World(records);
        var quests = new FalloutQuestState(records);
        var calls = 0;
        FalloutQuestStages? stages = null;
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            ++calls;
            if (effect.Kind == FalloutReferenceEffectKind.SetStage) stages!.Enter(effect.Target!.Value, effect.Stage);
            else if (effect.Kind != FalloutReferenceEffectKind.DoorOpenState) throw new InvalidDataException("Unexpected terminal fixture effect.");
        }));
        stages = new(records, quests, scripts.StageSteps, _ => throw new InvalidOperationException("Unexpected fixture predicate."));
        FalloutTerminalMenu? menu = null;
        menu = new(records, A(0x90), _ => { }, (_, _) => 0, selection =>
        {
            try { scripts.ExecuteTerminalResult(selection); }
            catch (Exception error)
            {
                var source = scripts.TerminalFailureFor(selection, error) ?? throw new InvalidOperationException("Missing exact source failure.");
                var cause = stages.ClosedFailureFor(error) ?? throw new InvalidOperationException("Missing exact closed stage cause.");
                Require(scripts.TerminalFailureFor(selection, new NotSupportedException(error.Message)) is null,
                    "Equal exception text substituted for a source invocation.");
                menu!.BindClosedStageFailure(selection, error, source.Statement, source.StatementCount, cause);
                menu.ReportPresentationFailure(error);
                throw;
            }
        });
        try { menu.Select(6, menu.Generation); throw new InvalidDataException("Nested source failure was absent."); }
        catch (NotSupportedException) { }
        Require(menu.Error is not null && menu.BlockingError is null && quests.Variable(A(0x60), 1) == 1 && calls == 2,
            "Closed nested failure lost its prefix or became an unrelated global blocker.");
        Reject(menu.RequireSaveable);
        menu.Close(); menu.RequireSaveable();
        var results = stages.CaptureResults();
        var saved = JsonSerializer.Deserialize<FalloutTerminalClosedSnapshot>(JsonSerializer.Serialize(menu.CaptureClosed(results)))!;
        Require(saved.Receipt is { State: FalloutTerminalSelectionState.Failed, ClosedStageFailure.Statement: 1 },
            "Terminal failed instruction or consumed selection receipt was lost.");
        var coldCalls = 0;
        var cold = FalloutTerminalMenu.RestoreClosed(records, saved, results,
            _ => { ++coldCalls; throw new InvalidOperationException("Cold admission executed."); },
            (_, _) => { ++coldCalls; throw new InvalidOperationException("Cold predicate executed."); },
            _ => { ++coldCalls; throw new InvalidOperationException("Cold result executed."); });
        cold.RequireSaveable();
        Require(!cold.Active && cold.Error == menu.Error && cold.BlockingError is null && coldCalls == 0 &&
            JsonSerializer.Serialize(cold.CaptureClosed(results)) == JsonSerializer.Serialize(saved) && calls == 2 &&
            quests.Variable(A(0x60), 1) == 1, "Cold closed terminal replayed source or changed its receipt.");
        Reject(() => cold.Select(6, cold.Generation));
        Require(coldCalls == 0, "Failed owner restarted source execution.");
        // Closing an unrelated menu remains possible; the failed owner's
        // source receipt is neither discarded nor acknowledged by that action.
        var independent = new FalloutTerminalMenu(records, A(0x90), _ => { }, (_, _) => 0, _ => { });
        independent.Close(); independent.RequireSaveable();
        Reject(() => FalloutTerminalMenu.ValidateClosed(records, [saved, saved], results));
        Reject(() => FalloutTerminalMenu.ValidateClosed(records, [saved with { ReferenceHash = new string('0', 64) }], results));
        Reject(() => FalloutTerminalMenu.ValidateClosed(records, [saved with { Page = A(0x7a), PageHash = FalloutTerminal.Read(records, A(0x7a)).SourceHash }], results));
        var receipt = saved.Receipt!;
        Reject(() => FalloutTerminalMenu.ValidateClosed(records, [saved with { Page = B(0x31),
            PageHash = FalloutTerminal.Read(records, B(0x31)).SourceHash }], results));
        var failure = receipt.ClosedStageFailure!;
        foreach (var invalid in new[]
        {
            receipt with { FragmentHash = new string('0', 64) },
            receipt with { Generation = saved.Generation },
            receipt with { State = FalloutTerminalSelectionState.Executing },
            receipt with { ClosedStageFailure = null },
            receipt with { ClosedStageFailure = failure with { Statement = 0 } },
            receipt with { ClosedStageFailure = failure with { StatementCount = failure.StatementCount + 1 } },
            receipt with { ClosedStageFailure = failure with { StageFailure = failure.StageFailure with { Stage = 10 } } },
            receipt with { ClosedStageFailure = failure with { StageFailure = failure.StageFailure with { Quest = B(0x60) } } }
        }) Reject(() => FalloutTerminalMenu.ValidateClosed(records, [saved with { Receipt = invalid }], results));
        Reject(() => FalloutTerminalMenu.ValidateClosed(records, [saved], []));
        cold.ReportPresentationFailure(new NotSupportedException(cold.Error));
        Require(cold.BlockingError is not null, "An unrelated equal-text presentation error was hidden.");
        Reject(cold.RequireSaveable);
        Console.WriteLine("OPENNV_CLOSED_TERMINAL_RESULT_PASS exactCause=true sourceInstruction=true consumedPrefix=true " +
            "closedColdNoExecution=true opaqueFailuresRefused=true sourceDriftRefused=true campaign=unverified");
    }
}
