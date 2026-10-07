using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class GameTimeCommandContracts
{
    internal static void Run()
    {
        ReferenceCommands();
        DateArguments();
        FailedCommands();
        QuestCommands();
        FunctionCommands();
        Console.WriteLine("OPENNV_GAME_TIME_COMMANDS_PASS reference=true questSharedAndFallback=true function=true " +
            "optionalSignedDate=true float32=true lazy=true pendingClockCold=true prefixAndFaultRetained=true " +
            "hardcoreNeedsUnownedRefused=true noGameplayOrDllClaim=true");
    }

    private static (FalloutGlobalState Globals, FalloutGameTime Clock) Clock(FalloutPluginStack records)
    {
        var forms = FalloutGameTimeBindings.Read(records);
        var keys = new[] { forms.Year, forms.Month, forms.Day, forms.Hour, forms.DaysPassed, forms.TimeScale };
        float[] values = [2281, 9, 13, 12, 888.25f, 30];
        var globals = new FalloutGlobalState(keys.Select((key, index) =>
            new FalloutGlobal(key, $"SyntheticTime{index}", (byte)'f', values[index], $"synthetic-global-{index}")));
        var clock = new FalloutGameTime(globals, forms,
            new([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], "synthetic-calendar"));
        clock.InitializeNewGame();
        return (globals, clock);
    }

    private static FalloutReferenceScripts Executor(ScriptSaveFixture fixture, FalloutGlobalState globals,
        FalloutGameTime? clock, Func<bool>? hardcore) =>
        new(fixture.Records, fixture.World, new(fixture.Records),
            new((_, _) => throw new InvalidDataException("Time fixture queried furniture."),
                _ => throw new InvalidDataException("Time fixture emitted a presentation effect."),
                Globals: globals, IsHardcore: hardcore, GameTime: clock));

    private static void ReferenceCommands()
    {
        using var fixture = new ScriptSaveFixture(
            "set saved to GetGameDaysPassed 2281 10 13\nSetGameHour 8\nset suffix to GetCurrentTime");
        var (globals, clock) = Clock(fixture.Records);
        var session = new FalloutScriptSession();
        var executor = Executor(fixture, globals, clock, () => session.Hardcore);
        Require(executor.Activate(fixture.Caller, fixture.Player).Error is null &&
            fixture.Value(1) == .5 && fixture.Value(2) == 32 && clock.Hour == 32,
            "Reference time commands did not read their clock or retain an unnormalized wrapped hour.");
        var savedGlobals = JsonSerializer.Deserialize<FalloutGlobalStateSnapshot>(JsonSerializer.Serialize(globals.Capture()))!;
        var savedClock = JsonSerializer.Deserialize<FalloutGameTimeSnapshot>(JsonSerializer.Serialize(clock.Capture()))!;
        var (coldGlobals, cold) = Clock(fixture.Records); coldGlobals.Restore(savedGlobals); cold.Restore(savedClock);
        for (var step = 0; step < 31; ++step) { clock.AdvanceSimulation(1f / 60); cold.AdvanceSimulation(1f / 60); }
        Require(globals.Capture().Values.SequenceEqual(coldGlobals.Capture().Values) && clock.Capture() == cold.Capture() &&
            globals.Get(FalloutGameTimeBindings.Read(fixture.Records).Day) == 14,
            "Pending source hour lost its exact cold simulation continuation.");
        executor.ExecuteProgram(fixture.Records.GetEffective(fixture.Quest), fixture.Script,
            FalloutGameModeProgram.Read("begin GameMode\nSetGameHour 0.1\nend"), 0);
        Require(BitConverter.SingleToInt32Bits(clock.Hour) == BitConverter.SingleToInt32Bits(24 + .1f),
            "Source Float32 extraction was replaced with double-precision hour arithmetic.");
    }

    private static void DateArguments()
    {
        foreach (var query in new[]
        {
            "GetGameDaysPassed", "GetGameDaysPassed 2281", "GetGameDaysPassed 2281 10",
            "GetGameDaysPassed 2281 10 13", "GetGameDaysPassed 2281.9 10.9 13.9"
        })
        {
            using var fixture = new ScriptSaveFixture("set saved to " + query);
            var (globals, clock) = Clock(fixture.Records);
            var executor = Executor(fixture, globals, clock, null);
            Require(executor.Activate(fixture.Caller, fixture.Player).Error is null && fixture.Value(1) == .5,
                "An optional date/default/truncated integer required a Hardcore consumer or used another epoch.");
        }
        using var lazy = new ScriptSaveFixture(
            "if 0\nSetGameHour -1\nset suffix to GetGameDaysPassed 2281 0 13\nendif\nset saved to 7");
        var (lazyGlobals, _) = Clock(lazy.Records);
        var before = lazyGlobals.Capture();
        Require(Executor(lazy, lazyGlobals, null, null).Activate(lazy.Caller, lazy.Player).Error is null &&
            lazy.Value(1) == 7 && lazy.Value(2) == 0 && before.Values.SequenceEqual(lazyGlobals.Capture().Values),
            "Inactive time operations bound missing owners, evaluated invalid operands or wrote state.");
    }

    private static void FailedCommands()
    {
        foreach (var command in new[]
        {
            "SetGameHour", "SetGameHour 1 2", "SetGameHour -1", "SetGameHour \"wrong\"",
            "set suffix to GetGameDaysPassed 2281 0 13", "set suffix to GetGameDaysPassed 2281 13 13",
            "set suffix to GetGameDaysPassed 2281 10 13 1", "set suffix to GetGameDaysPassed 1e20",
            "Player.SetGameHour 8"
        })
            Refused(command, clockPresent: true, new FalloutScriptSession(), queryPresent: true, expected: null);
        Refused("SetGameHour 8", clockPresent: false, new FalloutScriptSession(), queryPresent: true, expected: "clock owner");
        Refused("set suffix to GetGameDaysPassed", clockPresent: false, new FalloutScriptSession(),
            queryPresent: false, expected: "clock owner");
        Refused("SetGameHour 8", clockPresent: true, new FalloutScriptSession(), queryPresent: false, expected: "Hardcore query");
        Refused("SetGameHour 8", clockPresent: true, new FalloutScriptSession { Hardcore = true },
            queryPresent: true, expected: "Hardcore-needs update owner");
    }

    private static void Refused(string command, bool clockPresent, FalloutScriptSession session, bool queryPresent, string? expected)
    {
        using var fixture = new ScriptSaveFixture("set saved to saved + 1\n" + command + "\nset suffix to 99");
        var (globals, clock) = Clock(fixture.Records);
        var before = globals.Capture(); var beforeClock = clock.Capture();
        var executor = Executor(fixture, globals, clockPresent ? clock : null, queryPresent ? () => session.Hardcore : null);
        var result = executor.Activate(fixture.Caller, fixture.Player);
        Require(result.Error is not null && fixture.Value(1) == 1 && fixture.Value(2) == 0 &&
            before.Values.SequenceEqual(globals.Capture().Values) && beforeClock == clock.Capture(),
            "Invalid/unowned time operation consumed its suffix or changed the clock.");
        if (expected is not null) Require(result.Error!.Contains(expected, StringComparison.Ordinal),
            "Unowned time operation lost its exact stopping owner.");
        Require(executor.Activate(fixture.Caller, fixture.Player).Error == result.Error && fixture.Value(1) == 1,
            "A failed time operation replayed its already consumed prefix.");
    }

    private static void QuestCommands()
    {
        foreach (var shared in new[] { false, true })
        {
            using var fixture = new ScriptSaveFixture("",
                "set saved to GetGameDaysPassed 2281 10 13\nSetGameHour 104\nGetGameDaysPassed\nset suffix to GetCurrentTime");
            var (globals, clock) = Clock(fixture.Records);
            var quests = new FalloutQuestState(fixture.Records);
            var scripts = new FalloutQuestScripts(fixture.Records, quests, new HashSet<FalloutFormKey>(), new(),
                globals, defaultProcessingDelay: 0, references: fixture.World);
            var executor = new FalloutReferenceScripts(fixture.Records, fixture.World, quests,
                new((_, _) => throw new InvalidDataException("Time quest queried furniture."),
                    _ => throw new InvalidDataException("Time quest emitted a presentation effect."),
                    Globals: globals, IsHardcore: () => scripts.Session.Hardcore, GameTime: clock));
            scripts.Host = new((_, _) => throw new InvalidDataException("Time quest entered a stage."),
                _ => throw new InvalidDataException("Time quest queried an actor value."),
                shared ? executor.ExecuteProgram : null, shared ? executor.InvokeFunction : null, GameTime: clock);
            scripts.Advance(0);
            Require(quests.Variable(fixture.Quest, 1) == .5 && quests.Variable(fixture.Quest, 2) == 104 && clock.Hour == 104,
                "Shared/fallback quest used another calendar or failed its actual hour write.");
            clock.AdvanceSimulation(0);
            Require(clock.Hour == 8 && globals.Get(FalloutGameTimeBindings.Read(fixture.Records).Day) == 17,
                "Source absolute hour acquired an extra current-hour increment.");
        }
    }

    private static void FunctionCommands()
    {
        using var fixture = new ScriptSaveFixture("", functionBody:
            "SetGameHour 104\nSetFunctionValue GetGameDaysPassed 2281 10 13");
        var (globals, clock) = Clock(fixture.Records);
        var session = new FalloutScriptSession();
        var value = Executor(fixture, globals, clock, () => session.Hardcore)
            .InvokeFunction(new("Saves.esm", 0x52), fixture.Caller, [], 0);
        Require(clock.Hour == 104 && value == clock.GetDaysPassed(),
            "Source user function lost its caller's shared pending-hour/calendar owner.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
