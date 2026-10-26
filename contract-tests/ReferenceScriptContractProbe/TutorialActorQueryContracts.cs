using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using System.Buffers.Binary;

internal static class TutorialActorQueryContracts
{
    internal static void Run()
    {
        StatisticNames();
        using var fixture = new ScriptSaveFixture("");
        var quests = new FalloutQuestState(fixture.Records);
        var dead = false;
        var health = .375f;
        var targets = new List<FalloutFormKey>();
        var executor = new FalloutReferenceScripts(fixture.Records, fixture.World, quests,
            new((_, _) => false, _ => throw new InvalidDataException("Actor query emitted an effect."),
                IsDead: actor => { targets.Add(actor); return dead; },
                HealthPercentage: actor => { targets.Add(actor); return health; }));
        void Execute(string body) => executor.ExecuteProgram(fixture.Records.GetEffective(fixture.Quest),
            fixture.Records.GetEffective(new("Saves.esm", 0x51)), FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
        Execute("if player.GetDead == 1\nreturn\nendif\nset saved to saved + 1\nset suffix to player.GetHealthPercentage");
        Require(quests.Variable(fixture.Quest, 1) == 1 && quests.Variable(fixture.Quest, 2) == health &&
            targets.SequenceEqual(new[] { fixture.Player, fixture.Player }),
            "Living player queries did not reach the engine-created player's authoritative owners.");
        dead = true; targets.Clear();
        Execute("if player.GetDead == 1\nreturn\nendif\nset saved to saved + 1\nset suffix to player.GetHealthPercentage");
        Require(quests.Variable(fixture.Quest, 1) == 1 && targets.SequenceEqual(new[] { fixture.Player }),
            "Dead player query did not stop the authored tutorial prefix.");
        targets.Clear(); health = .125f;
        Execute("set saved to Other.GetDead\nset suffix to Other.GetHealthPercentage");
        Require(quests.Variable(fixture.Quest, 1) == 1 && quests.Variable(fixture.Quest, 2) == health &&
            targets.SequenceEqual(new[] { new FalloutFormKey("Saves.esm", 0x91), new FalloutFormKey("Saves.esm", 0x91) }),
            "Actor queries replaced the actual NPC receiver with player state.");
        foreach (var query in new[] { "player.GetDead", "player.GetHealthPercentage" })
        {
            var rejected = false;
            try
            {
                fixture.Scripts.ExecuteProgram(fixture.Records.GetEffective(fixture.Quest),
                    fixture.Records.GetEffective(new("Saves.esm", 0x51)),
                    FalloutGameModeProgram.Read("begin GameMode\nset suffix to " + query + "\nend"), 0);
            }
            catch (NotSupportedException error) when (error.Message.Contains("authoritative vitals owner", StringComparison.Ordinal))
            { rejected = true; }
            Require(rejected, "Missing player vitals fabricated an actor value or tried to load a placed player record.");
        }
        foreach (var prefix in new[] { "", "set saved to saved + 1\n" })
        {
            using var recovery = new ScriptSaveFixture("", prefix +
                "if player.GetDead == 1\nreturn\nendif\nset saved to saved + 1\nset suffix to player.GetHealthPercentage");
            var recoveredQuests = new FalloutQuestState(recovery.Records);
            var original = new FalloutQuestScripts(recovery.Records, recoveredQuests, new HashSet<FalloutFormKey>(), new(),
                references: recovery.World, defaultProcessingDelay: 0);
            var snapshot = original.Capture();
            var legacy = $"No record exists for {recovery.Player}.";
            snapshot = snapshot with { Instances = snapshot.Instances.Select(instance => instance with { Error = legacy }).ToArray() };
            var restored = new FalloutQuestScripts(recovery.Records, recoveredQuests, new HashSet<FalloutFormKey>(), new(),
                references: recovery.World, defaultProcessingDelay: 0);
            restored.Restore(snapshot);
            var readOwner = new FalloutReferenceScripts(recovery.Records, recovery.World, recoveredQuests,
                new((_, _) => false, _ => throw new InvalidDataException("Query recovery emitted an effect."),
                    IsDead: _ => false, HealthPercentage: _ => .375f));
            restored.Host = new((_, _) => throw new InvalidDataException("Query recovery changed a stage."), _ => 0,
                readOwner.ExecuteProgram, CanRetryPlayerDeathRead: readOwner.CanRetryPlayerDeathRead);
            restored.Advance(0);
            var state = restored.Capture().Instances.Single();
            Require(prefix.Length == 0 ? state.Error is null && state.Continuations?.Count == 1 &&
                recoveredQuests.Variable(recovery.Quest, 1) == 1 && recoveredQuests.Variable(recovery.Quest, 2) == .375f :
                state.Error == legacy && recoveredQuests.Variable(recovery.Quest, 1) == 0,
                "Cold initial-read recovery repeated an effect prefix or left a safe first read stopped.");
        }
        Console.WriteLine("OPENNV_TUTORIAL_ACTOR_QUERY_PASS enginePlayer=true deathGuard=true liveHealth=true actualNpcReceiver=true missingOwnerRefused=true");
    }

    private static void StatisticNames()
    {
        var data = new byte[43 * sizeof(uint)];
        var names = new Dictionary<uint, string>();
        for (var index = 0; index < 43; index++)
        {
            var address = (uint)(100 + index * 4);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(index * 4), address);
            names[address] = index == 0 ? "Quests Completed" : "Synthetic " + (char)('A' + index / 26) + (char)('A' + index % 26);
        }
        string? Literal(uint address) => names.GetValueOrDefault(address);
        Require(FalloutExecutableStringTable.ReadMiscStatisticNames(data, Literal)!.SequenceEqual(names.Values),
            "Synthetic statistic name table lost slot order or extent.");
        Require(FalloutExecutableStringTable.ReadMiscStatisticNames(data.AsSpan(0, data.Length - 1), Literal) is null,
            "Truncated statistic table was accepted.");
        names.Remove(104);
        Require(FalloutExecutableStringTable.ReadMiscStatisticNames(data, Literal) is null, "Missing statistic literal invented a name.");
        names[104] = "Quests Completed";
        var rejected = false;
        try { _ = FalloutExecutableStringTable.ReadMiscStatisticNames(data, Literal); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "Duplicate statistic names were accepted.");
        names[104] = "Unsupported9";
        Require(FalloutExecutableStringTable.ReadMiscStatisticNames(data, Literal) is null, "Invalid statistic table name was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
