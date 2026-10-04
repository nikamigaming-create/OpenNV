using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ScriptRecoveryContracts
{
    internal static void Verify(FalloutPluginStack records)
    {
        ScriptInspectionContracts.Run();
        static FalloutFormKey Key(uint id) => new("Base.esm", id);
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); }
        FalloutScriptFunction? Function(string name) => name.ToLowerInvariant() switch
        {
            "getcurrenttime" or "getdisabled" => new([], _ => throw new InvalidOperationException("Recovery invoked a read.")) { ReadOnly = true },
            "getrandompercent" or "getgameloaded" => new([], _ => throw new InvalidOperationException("Recovery consumed state.")),
            _ => null,
        };
        bool Safe(string source, string operand = "GetCurrentTime") =>
            FalloutGameModeProgram.Read("begin GameMode\n" + source + "\nend").CanRetryMissingRead(operand, Function);
        Require(Safe("set count to GetCurrentTime\nset timer to timer + 1"), "Initial failed read stayed latched.");
        Require(Safe("if GetDisabled == 0\nset count to GetCurrentTime\nendif"), "Pure condition blocked safe read recovery.");
        Require(Safe("set count to GetRandomPercent\nset timer to GetRandomPercent", "GetRandomPercent"), "Initial random read cannot recover.");
        Require(!Safe("set timer to timer + 1\nset count to GetCurrentTime"), "Recovery could repeat a prior assignment.");
        Require(!Safe("AddItem Reward 1\nset count to GetCurrentTime"), "Recovery could repeat a reward.");
        Require(!Safe("if GetGameLoaded\nset count to GetCurrentTime\nendif"), "Recovery could consume a lifecycle edge.");
        Require(!Safe("if GetRandomPercent < 50\nset count to GetCurrentTime\nendif"), "Recovery could reroll a prior query.");
        Require(!Safe("if count == 0\nset count to GetCurrentTime\nendif\nAddItem Reward 1\nset timer to GetCurrentTime"),
            "A later occurrence hid an already executed effect.");
        Require(!Safe("set count to Unsupported", "Unsupported"), "An absent function was declared recoverable.");
        bool SafeCommand(string source) => FalloutGameModeProgram.Read("begin OnLoad\n" + source + "\nend", "OnLoad")
            .CanRetryMissingCommand("PlayGroup", Function);
        Require(SafeCommand("if count == 0\nPlayGroup Forward 1\nelseif count == 1\nPlayGroup Backward 1\nendif"),
            "Alternative first-effect animation guards cannot recover.");
        Require(!SafeCommand("AddItem Reward 1\nPlayGroup Forward 1"), "Animation recovery could repeat a reward.");
        Require(!SafeCommand("if GetRandomPercent < 50\nPlayGroup Forward 1\nendif"), "Animation recovery could reroll a guard.");
        Require(!SafeCommand("if GetGameLoaded\nPlayGroup Forward 1\nendif"), "Animation recovery could consume a lifecycle edge.");
        Require(!SafeCommand("set count to 1\nPlayGroup Forward 1"), "Animation recovery could repeat an assignment.");
        Require(!SafeCommand("PlayGroup Forward GetGameLoaded"), "Command recovery could consume its argument again.");
        bool SafeDeath(string source) => FalloutGameModeProgram.Read("begin OnLoad\n" + source + "\nend", "OnLoad")
            .CanRetryMissingCommand("Kill", Function, 0);
        Require(SafeDeath("Kill\nset count to 1"), "Initial missing Kill cannot recover.");
        Require(!SafeDeath("set count to 1\nKill") && !SafeDeath("if GetRandomPercent < 50\nKill\nendif") &&
            !SafeDeath("Kill player"), "Death recovery admitted a prior mutation, consumptive guard or changed arguments.");
        Require(!FalloutGameModeProgram.Read("begin OnLoad\nKill GetGameLoaded\nend", "OnLoad")
            .CanRetryMissingCommand("Kill", Function, 1), "Death recovery could consume a lifecycle argument twice.");
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, Key(0x803));
        world.LoadCell(cell);
        var globals = new OpenNV.Runtime.Gameplay.State.FalloutGlobalState(
            [new(Key(0x38), "GameHour", (byte)'s', 6.75f, "synthetic")]);
        const string clockError = "GameMode: Script operand GetCurrentTime has no variable owner.";
        var snapshots = world.Capture().Select(state => state with
        {
            Variables = new Dictionary<uint, double> { [1] = 17, [2] = 41 },
            ScriptError = state.Reference == Key(0x922) ? "OnLoad: Script operand GetRandomPercent has no variable owner." : clockError
        }).ToArray();
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!);
        cold.LoadCell(cell);
        cold.ScriptValues.Restore(new(0, [], 123));
        var scripts = new FalloutReferenceScripts(records, cold, new(records), new((_, _) => false,
            _ => throw new InvalidDataException("Recovery invented a host effect."), Globals: globals));
        Require(scripts.Dispatch(Key(0x920), "OnLoad").Error == clockError, "An unrelated event cleared the failed block.");
        var result = scripts.Dispatch(Key(0x920), "GameMode");
        Require(result is { Error: null, Blocks: 1, RecoveredError: clockError } && cold.Get(Key(0x920)).Read(1) == 6.75 &&
            cold.Get(Key(0x920)).Read(2) == 42, "Cold recovery lost locals, repeated work or skipped the repaired expression.");
        Require(scripts.Dispatch(Key(0x921), "GameMode").Error == clockError && cold.Get(Key(0x921)).Read(2) == 41,
            "Legacy failure with an executed prefix was replayed.");
        var expectedRandom = new FalloutSoundRandomState(123);
        Require(scripts.Dispatch(Key(0x922), "OnLoad") is { Error: null, Blocks: 1, RecoveredError: not null } &&
            cold.Get(Key(0x922)).Read(1) == expectedRandom.NextBounded(100) && cold.Get(Key(0x922)).Read(2) == 42,
            "Recovery consumed randomness or skipped the random source instruction.");
        var savedValues = JsonSerializer.Deserialize<FalloutScriptValueStoreSnapshot>(JsonSerializer.Serialize(cold.ScriptValues.Capture()))!;
        var next = new FalloutScriptValueStore(); next.Restore(savedValues);
        for (var index = 0; index < 1000; ++index)
        {
            var value = cold.ScriptValues.RandomPercent();
            Require(value < 100 && value == next.RandomPercent(), "Script randomness escaped 0..99 or changed on cold restore.");
        }
        next.Restore(new(0, []));
        Require(next.Capture().RandomState is null, "Legacy value state inherited a previous session's random stream.");
        Console.WriteLine("OPENNV_SCRIPT_RECOVERY_PASS preservedLocals=true noRepeatedEffects=true noQueryInvocation=true randomRange=true coldRandom=true");
    }
}
