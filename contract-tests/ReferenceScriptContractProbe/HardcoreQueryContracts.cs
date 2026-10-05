using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class HardcoreQueryContracts
{
    internal static void Run()
    {
        var session = new FalloutScriptSession();
        using (var fixture = new ScriptSaveFixture("set saved to saved + 1\nset suffix to IsHardcore", hardcore: () => session.Hardcore))
        {
            foreach (var enabled in new[] { false, true })
            {
                session.Hardcore = enabled;
                ScriptManualSaveContracts.Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null &&
                    fixture.Value(2) == (enabled ? 1 : 0), "IsHardcore did not read the actual player session.");
            }
            var snapshot = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!;
            var coldSession = new FalloutScriptSession(); coldSession.Restore(snapshot);
            using var cold = new ScriptSaveFixture("set suffix to IsHardcore", hardcore: () => coldSession.Hardcore);
            ScriptManualSaveContracts.Require(cold.Scripts.Activate(cold.Caller, cold.Player).Error is null && cold.Value(2) == 1,
                "Cold player session lost its Hardcore query.");
        }
        foreach (var bad in new[] { "player.IsHardcore", "IsHardcore 1" })
        {
            using var fixture = new ScriptSaveFixture($"set saved to 1\nset suffix to {bad}", hardcore: () => true);
            ScriptManualSaveContracts.Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is not null &&
                fixture.Value(1) == 1 && fixture.Value(2) == 0, "Invalid IsHardcore context/arity consumed its failed write.");
        }
        using (var missing = new ScriptSaveFixture("set saved to 1\nset suffix to IsHardcore"))
        {
            var result = missing.Scripts.Activate(missing.Caller, missing.Player);
            ScriptManualSaveContracts.Require(result.Error is not null && missing.Value(1) == 1 && missing.Value(2) == 0 &&
                missing.Scripts.Dispatch(missing.Caller, "GameMode").Error == result.Error,
                "Missing player session invented a value or replayed its source prefix.");
        }
        foreach (var shared in new[] { false, true })
        {
            using var fixture = new ScriptSaveFixture("", "set suffix to IsHardcore");
            var quests = new FalloutQuestState(fixture.Records);
            var scripts = new FalloutQuestScripts(fixture.Records, quests, new HashSet<FalloutFormKey>(), new(),
                references: fixture.World, defaultProcessingDelay: 0);
            var executor = new FalloutReferenceScripts(fixture.Records, fixture.World, quests,
                new((_, _) => false, _ => { }, IsHardcore: () => scripts.Session.Hardcore));
            scripts.Host = new((_, _) => throw new InvalidDataException("Unexpected stage"), _ => 0,
                shared ? executor.ExecuteProgram : null);
            scripts.Session.Hardcore = true;
            scripts.Advance(0);
            ScriptManualSaveContracts.Require(quests.Variable(fixture.Quest, 2) == 1,
                "Shared/fallback quest IsHardcore read a different session.");
            scripts.Session.Hardcore = false;
            scripts.Advance(.1);
            ScriptManualSaveContracts.Require(quests.Variable(fixture.Quest, 2) == 0,
                "Shared/fallback quest IsHardcore cached its old session.");
        }
        Console.WriteLine("OPENNV_HARDCORE_QUERY_PASS playerSession=true typedZeroArguments=true explicitReceiverRefused=true " +
            "readOnly=true cold=true missingOwnerRefused=true prefixRetained=true sharedAndFallback=true");
    }
}
