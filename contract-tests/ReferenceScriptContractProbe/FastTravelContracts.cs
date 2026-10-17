using System.Globalization;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class FastTravelContracts
{
    internal static void Run()
    {
        Dispatch();
        Clock();
        var policy = new FalloutFastTravel();
        Check(policy.State == new FalloutFastTravelSnapshot(true, true, false), "Initial travel policy differs.");
        policy.Apply(["0"]);
        Check(policy.State == new FalloutFastTravelSnapshot(false, true, false), "Optional wait/keep defaults differ.");
        policy.CompleteRelocation();
        Check(policy.State.CanTravel, "Temporary travel disable survived a cell transition.");
        policy.Apply(["0", "0", "0"]); policy.CompleteRelocation();
        Check(policy.State.CanTravel && !policy.State.CanWait, "Cell transition changed the separate wait permission.");
        policy.Apply(["0", "0", "1"]); policy.Apply(["0", "1", "0"]); policy.CompleteRelocation();
        Check(policy.State == new FalloutFastTravelSnapshot(false, true, true), "Persistent disable was cleared by a later temporary disable.");
        var cold = new FalloutFastTravel();
        cold.Restore(JsonSerializer.Deserialize<FalloutFastTravelSnapshot>(JsonSerializer.Serialize(policy.State))!);
        cold.CompleteRelocation();
        Check(cold.State == policy.State, "Cold restore lost persistent travel policy.");
        cold.Apply(["1", "0"]);
        Check(cold.State == new FalloutFastTravelSnapshot(true, true, false), "Enabling travel did not enable waiting and clear persistence.");
        cold.Apply(["1", "0", "1"]);
        Check(cold.State == new FalloutFastTravelSnapshot(true, true, true), "Explicit persistence on enable was discarded.");
        foreach (var arguments in new string[][] { [], ["0", "0", "0", "0"], ["missing"], ["0", "1.5"], ["0", "1", "NaN"] })
        {
            var before = cold.State;
            Reject(() => cold.Apply(arguments));
            Check(cold.State == before, "Malformed command partially changed travel policy.");
        }
        Console.WriteLine("OPENNV_FAST_TRAVEL_POLICY_PASS sourceDefaults=true stickyDisable=true waitIndependent=true cold=true invalidAtomic=true");
    }

    private static void Dispatch()
    {
        using var fixture = new ScriptSaveFixture("EnableFastTravel 0 0 1\nset saved to 3",
            "EnableFast 1 0\nset saved to 7");
        var policy = new FalloutFastTravel();
        var quests = new FalloutQuestState(fixture.Records);
        var executor = new FalloutReferenceScripts(fixture.Records, fixture.World, quests,
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected travel effect."),
                Command: (_, _, command, arguments) =>
                {
                    Check(command.ToLowerInvariant() is "enablefasttravel" or "enablefast", "Unexpected travel command.");
                    policy.Apply(arguments);
                }));
        Check(executor.Activate(fixture.Caller, fixture.Player).Error is null && fixture.Value(1) == 3 &&
            policy.State == new FalloutFastTravelSnapshot(false, false, true), "Actual reference dispatch lost the travel flags.");
        var scripts = new FalloutQuestScripts(fixture.Records, quests, new HashSet<FalloutFormKey>(), new(),
            defaultProcessingDelay: 0, references: fixture.World);
        scripts.Host = new((_, _) => throw new InvalidDataException("Unexpected stage."),
            _ => throw new InvalidDataException("Unexpected actor value."), executor.ExecuteProgram);
        scripts.Advance(0);
        Check(quests.Variable(fixture.Quest, 1) == 7 && policy.State == FalloutFastTravelSnapshot.Initial,
            "Actual shared quest dispatch did not update the same travel owner.");
    }

    private static void Clock()
    {
        Check(FalloutFastTravel.ElapsedHours(100 * 3600 * 1.75f, 100, 1) == 1 &&
            FalloutFastTravel.ElapsedHours(100 * 3600 * .25f, 100, 1) == .25f,
            "Travel lost its source whole-hour truncation/sub-hour path.");
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Reject(() => FalloutFastTravel.ElapsedHours(10, invalid, 30));
        Reject(() => FalloutFastTravel.ElapsedHours(10, 100, 0));
        using var fixture = new ScriptSaveFixture("");
        var forms = FalloutGameTimeBindings.Read(fixture.Records);
        var keys = new[] { forms.Year, forms.Month, forms.Day, forms.Hour, forms.DaysPassed, forms.TimeScale };
        float[] values = [2281, 0, 31, 23.5f, 30, 30];
        var globals = new FalloutGlobalState(keys.Select((key, index) => new FalloutGlobal(key, "Time" + index, (byte)'f', values[index], "synthetic")));
        var clock = new FalloutGameTime(globals, forms, new([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], "synthetic"));
        var before = globals.Capture(); var beforeClock = clock.Capture();
        clock.ValidateFastTravel(2);
        Check(before.Values.SequenceEqual(globals.Capture().Values) && beforeClock == clock.Capture(),
            "Travel preflight advanced the live clock before arrival committed.");
        clock.AdvanceFastTravel(2);
        Check(clock.Hour == 1.5f && globals.Get(forms.Month) == 1 && globals.Get(forms.Day) == 1,
            "Successful travel did not advance the authoritative calendar across month rollover.");
        var saved = JsonSerializer.Deserialize<FalloutGameTimeSnapshot>(JsonSerializer.Serialize(clock.Capture()))!;
        var savedGlobals = JsonSerializer.Deserialize<FalloutGlobalStateSnapshot>(JsonSerializer.Serialize(globals.Capture()))!;
        var coldGlobals = new FalloutGlobalState(globals.Sources); coldGlobals.Restore(savedGlobals);
        var cold = new FalloutGameTime(coldGlobals, forms, new([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], "synthetic"));
        cold.Restore(saved); cold.AdvanceFastTravel(.25f); clock.AdvanceFastTravel(.25f);
        Check(clock.Hour == 1.75f && globals.Capture().Values.SequenceEqual(coldGlobals.Capture().Values) && clock.Capture() == cold.Capture(),
            "Travel clock lost exact cold fractional continuation.");
    }

    internal static void RunOwned(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var matched = 0;
        foreach (var script in records.EffectiveRecords("SCPT"))
        {
            var field = script.ReadSubrecords().SingleOrDefault(value => value.Signature == "SCTX");
            if (field.Data.IsEmpty) continue;
            var source = FalloutDialogueTopic.ScriptText(field.Data.Span);
            foreach (var line in FalloutDialogueTopic.CodeLines(source))
            {
                var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0 || !new[] { "EnableFastTravel", "EnableFast" }.Contains(words[0], StringComparer.OrdinalIgnoreCase)) continue;
                var arguments = words[1..];
                if (!arguments.All(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) continue;
                var policy = new FalloutFastTravel(); policy.Apply(arguments);
                var cold = new FalloutFastTravel();
                cold.Restore(JsonSerializer.Deserialize<FalloutFastTravelSnapshot>(JsonSerializer.Serialize(policy.State))!);
                Check(cold.State == policy.State, "Owned command lost its policy on cold restore.");
                ++matched;
            }
        }
        Check(matched > 0, "Owned source declares no literal EnableFastTravel commands.");
        Console.WriteLine($"OPENNV_OWNED_FAST_TRAVEL_POLICY_PASS commands={matched} sourceReadonly=true vanillaSelection=true campaign=unverified parity=unverified");
    }

    private static void Check(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid fast travel command was accepted.");
    }
}
