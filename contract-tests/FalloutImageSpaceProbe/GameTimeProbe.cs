using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class GameTimeProbe
{
    internal static void Run()
    {
        var days = new ushort[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        var bytes = days.SelectMany(BitConverter.GetBytes).ToArray();
        var decoded = FalloutCalendar.Decode(bytes) ?? throw new Exception("Calendar declaration was lost.");
        Require(decoded.SequenceEqual(days), "Owned calendar values were replaced.");
        Reject<InvalidDataException>(() => FalloutCalendar.Decode(bytes.Concat(bytes).ToArray()));
        bytes[4] = 1;
        Require(FalloutCalendar.Decode(bytes) is null, "Malformed month declaration was accepted.");
        var calendar = new FalloutCalendar(days, "synthetic-calendar");
        var forms = Enumerable.Range(1, 7).Select(index => new FalloutFormKey("Test.esm", (uint)(index + 0x900))).ToArray();
        var bindings = new FalloutGameTimeBindings(forms[0], forms[1], forms[2], forms[3], forms[4], forms[5]);
        float[] values = [2280, 1, 28, 23.5f, 3, 60, 0.125f];
        var sources = forms.Select((form, index) => new FalloutGlobal(form, $"Arbitrary{index}", (byte)'s', values[index], $"source-{index}")).ToArray();
        var globals = new FalloutGlobalState(sources);
        var clock = new FalloutGameTime(globals, bindings, calendar);
        clock.InitializeNewGame();
        Require(clock.DaysPassed == (float)(3 + 23.5 / 24), "New-game day counter did not include source time of day.");
        clock.AdvanceSimulation(30);
        Require(clock.Hour == 24 && globals.Get(forms[2]) == 28, "Exact-hour boundary changed the engine clock's comparison.");
        clock.AdvanceSimulation(30);
        Require(clock.Hour == 0.5f && globals.Get(forms[1]) == 2 && globals.Get(forms[2]) == 1,
            "Source calendar rollover acquired a leap day or wrong month base.");
        globals.Set(forms[5], 30);
        var savedGlobals = globals.Capture(); var savedClock = clock.Capture();
        var restoredGlobals = new FalloutGlobalState(sources); restoredGlobals.Restore(savedGlobals);
        var restoredClock = new FalloutGameTime(restoredGlobals, bindings, calendar); restoredClock.Restore(savedClock);
        for (var step = 0; step < 17; step++) { clock.AdvanceSimulation(1f / 60); restoredClock.AdvanceSimulation(1f / 60); }
        Require(globals.Capture().Values.SequenceEqual(restoredGlobals.Capture().Values) && clock.Capture() == restoredClock.Capture(),
            "Cold restoration drifted from the same shared simulation ticks.");
        Require(globals.Get(forms[6]) == 0.125f, "Integer-declared global lost its original Float32 bits.");
        var unchanged = restoredGlobals.Capture();
        Reject<InvalidDataException>(() => restoredGlobals.Restore(new(savedGlobals.Values.Select((value, index) =>
            index == 0 ? value with { SourceSha256 = "another-source" } : value).ToArray())));
        Require(unchanged.Values.SequenceEqual(restoredGlobals.Capture().Values), "Rejected global restore partially mutated gameplay state.");
        Reject<InvalidDataException>(() => globals.Set(forms[0], float.NaN));
        Reject<ArgumentOutOfRangeException>(() => clock.AdvanceSimulation(-1));
        Console.WriteLine("OPENNV_GAME_TIME_CONTRACT_PASS sourceGlobals=true float32=true sourceCalendar=true coldRestore=true simulationClockOnly=true");
    }

    internal static void Owned(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var globals = FalloutGlobalState.Read(records);
        var calendar = FalloutCalendar.Read(Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe"));
        var bindings = FalloutGameTimeBindings.Read(records);
        var clock = new FalloutGameTime(globals, bindings, calendar);
        foreach (var form in new[] { bindings.Year, bindings.Month, bindings.Day, bindings.Hour, bindings.DaysPassed, bindings.TimeScale })
        {
            var declaration = globals.Sources.Single(value => value.Form == form);
            Console.WriteLine($"OPENNV_OWNED_CLOCK_GLOBAL source={form} editor={declaration.EditorId} type={(char)declaration.Type} value={declaration.InitialValue:R} sha256={declaration.SourceSha256}");
        }
        clock.InitializeNewGame(); clock.AdvanceSimulation(1f / 60);
        var saved = globals.Capture(); var savedTime = clock.Capture();
        var restoredGlobals = FalloutGlobalState.Read(records); restoredGlobals.Restore(saved);
        var restoredClock = new FalloutGameTime(restoredGlobals, bindings, calendar); restoredClock.Restore(savedTime);
        clock.AdvanceSimulation(1f / 60); restoredClock.AdvanceSimulation(1f / 60);
        Require(globals.Capture().Values.SequenceEqual(restoredGlobals.Capture().Values), "Owned cold global restoration changed the next simulation tick.");
        Console.WriteLine($"OPENNV_OWNED_GAME_TIME_PASS globals={globals.Sources.Count} calendarSha256={calendar.SourceSha256} initialHour={saved.Values.Single(value => value.Form == bindings.Hour).Value:R} matchedClock=unverified");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException($"Expected {typeof(T).Name}.");
    }
}
