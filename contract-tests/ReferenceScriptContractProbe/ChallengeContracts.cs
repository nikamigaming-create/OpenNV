using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ChallengeContracts
{
    private const string Plugin = "Challenge.esm";
    private const string Engine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    internal static void Run()
    {
        SourceMainScriptCallerContracts.Run();
        var directory = Directory.CreateTempSubdirectory("opennv-challenge-");
        try
        {
            EventFilters(directory.FullName); NestedCompletion(directory.FullName);
            RecurringAndSigned(directory.FullName); NativeSuffixFailure(directory.FullName);
            NoticePrefix(directory.FullName); OriginalRegistry(directory.FullName);
            CompiledRewards(directory.FullName); UnknownRewardFamilies(directory.FullName);
            var bytes = new byte[64]; bytes[0] = 0x68; U32(1).CopyTo(bytes, 1); bytes[5] = 0x68;
            U32(512).CopyTo(bytes, 6); bytes[24] = 0xd9; bytes[25] = 5; U32(2).CopyTo(bytes, 26);
            Require(FalloutExecutableStringTable.ReadChallengeHudDeclaration(bytes, id => id == 1 ? "%s %d/%d %s" : null,
                id => id == 2 ? 4.5f : throw new InvalidDataException()) == new FalloutChallengeHudDeclaration("%s %d/%d %s", 4.5f),
                "Challenge HUD declaration lost owned literals or timing.");
            Reject(() => FalloutExecutableStringTable.ReadChallengeHudDeclaration(bytes, _ => "%s %f", _ => 4.5f));
            Console.WriteLine("OPENNV_CHALLENGE_CONTRACT_PASS authored=true sourceEvent11=true nullFormWordFilters=true " +
                "nestedActualCounters=true signedProgress=true rawFlags=true onceOnlyCompiledRewards=true " +
                "genuineSharedRetirement=true unknownInstructionPrefix=true currentColdNoReplay=true " +
                "winningRegistration=true noticesBeforeMissingCue=true nativeStatsAndCue=UNOWNED ownedGameplay=UNEXECUTED parity=UNVERIFIED");
        }
        finally { directory.Delete(true); }
    }

    // These native-menu observations and the independent GameMode producer are
    // authored actors in this fixture, never proof of an original singleton.
    private sealed class Fixture : IDisposable
    {
        internal readonly FalloutHudNotifications Queue = new();
        internal readonly FalloutReferenceWorld World;
        internal readonly FalloutPlayerStatistics Statistics;
        internal FalloutChallenges Challenges => World.Challenges;
        internal readonly FalloutReferenceScripts Scripts;
        internal int MenuCalls;
        internal Action<ushort>? DuringMenu;
        internal Fixture(FalloutPluginStack records, bool notices = false,
            FalloutPlayerStatisticsSnapshot? statistics = null, FalloutChallengesSnapshot? challenges = null,
            FalloutHudNotificationsSnapshot? hud = null, bool bindGameMode = true, bool allows = true)
        {
            World = new(records);
            if (hud is not null) Queue.Restore(hud);
            var rows = Enumerable.Range(0, 43).Select(index => new FalloutMiscellaneousStatisticRow((ushort)index,
                "Authored statistic " + index, "sAuthoredCounter" + index, "Authored row " + index, 0)).ToArray();
            var source = new FalloutMiscellaneousStatisticSource(Engine, new('1', 64),
                FalloutMiscellaneousStatisticSource.CurrentContractSha256, 1003, 11, 23, rows);
            var ini = new FalloutNumericIniSettings([new("bShowChallengeUpdates:GamePlay", FalloutIniCollection.Main,
                notices ? 1u : 0u)], [], "authored-original-INI-declaration");
            World.ConfigureCampaignChallenges(Queue, FalloutChallengeEventSource.Read(source, ini));
            World.ConfigureCampaignScriptEngineContexts(FalloutImmediateScriptSource.Read(Challenges.Source!));
            Statistics = new(source, new(null, menu =>
            {
                ++MenuCalls; DuringMenu?.Invoke(checked((ushort)menu));
                return new(menu, false, new('a', 64), 0, null);
            }, null), statistics);
            Challenges.BindStatistics(Statistics);
            if (challenges is not null) Challenges.Restore(challenges);
            if (bindGameMode) World.BindChallengeGameModeSource(new AuthoredGameMode(FalloutImmediateScriptSource.Read(Challenges.Source!), allows));
            Scripts = new(records, World, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Authored challenge reached an unrelated host effect."),
                Challenges: Challenges, Statistics: Statistics));
            Scripts.BindCampaignChallengeRewards();
        }
        public void Dispose() { Challenges.Retire(); Statistics.Retire(); World.Dispose(); }
    }
    private sealed class AuthoredGameMode(FalloutImmediateScriptSource source, bool allows) : IFalloutChallengeGameModeSource
    {
        private long _ordinal;
        private FalloutChallengeGameModeObservation? _last;
        public FalloutChallengeGameModeObservation Observe()
        {
            source.Validate();
            return _last = new(source.EngineSha256, source.Identity, ++_ordinal, allows);
        }
        public void RequireCurrent(FalloutChallengeGameModeObservation observation)
        {
            if (!ReferenceEquals(observation, _last)) throw new InvalidDataException("Authored predicate lost its actual current observation.");
        }
    }

    private static void EventFilters(string directory)
    {
        Write(directory, Challenge(0x100, 11, 2, 0, 0), Challenge(0x101, 11, 2, 0, 1),
            Challenge(0x102, 11, 2, 0, 0, primary: 0x900), Challenge(0x103, 11, 2, 0, 0, secondary: 0x900),
            Challenge(0x104, 11, 2, 0, 0, value2: 1), Challenge(0x105, 11, 2, 0, 0, value3: 1),
            Challenge(0x106, 11, 2, 1, 0), Challenge(0x107, 13, 5, 0), Record("MISC", 0x900));
        using var records = Load(directory); using var fixture = new Fixture(records);
        fixture.Statistics.Mod(0, 1, "authored-index-zero");
        Require(fixture.Challenges.State(Key(0x100)).Progress == 1 &&
            new uint[] { 0x101, 0x102, 0x103, 0x104, 0x105, 0x106, 0x107 }.All(id => fixture.Challenges.State(Key(id)).Progress == 0),
            "CHAL11 treated zero as a wildcard, accepted null form arguments, skipped source words, or incremented type13.");
        fixture.Challenges.Unlock(Key(0x106)); fixture.Statistics.Mod(0, -1, "authored-negative-delta");
        Require(fixture.Challenges.State(Key(0x100)).Progress == 0 && fixture.Challenges.State(Key(0x106)) is
            { Progress: -1, RuntimeFlags: 1 }, "Unlock or signed source delta lost its genuine state.");
        fixture.Challenges.IncrementScripted(Key(0x107));
        Require(fixture.Challenges.State(Key(0x107)).Progress == 1 && fixture.Statistics.Read(0) == 0,
            "A real scripted13 request substituted for statistic11 or invented a counter.");
        var saved = Copy(fixture.Challenges.Capture());
        Require(saved.Entries.Count == 8 && saved.Buckets[11].Members.Count == 7 &&
            saved.LastDispatch is { Event: 13, Complete: true }, "Registry denominator lost inactive source members.");
        using var missing = new FalloutReferenceWorld(records);
        missing.ConfigureCampaignChallenges(new(), null); Reject(() => missing.Challenges.IncrementScripted(Key(0x107)));
        Require(missing.Challenges.State(Key(0x107)).Progress == 0, "Unadmitted source selected a synthetic dispatcher.");
    }

    private static void NestedCompletion(string directory)
    {
        Write(directory, Challenge(0x100, 11, 1, 0, 1), Challenge(0x101, 11, 2, 0, 27));
        using var records = Load(directory); using var warm = new Fixture(records);
        warm.Statistics.Mod(1, 1, "authored-completion");
        var counters = Copy(warm.Statistics.Capture()); var saved = Copy(warm.Challenges.Capture());
        var dispatch = saved.LastDispatch!;
        Require(counters.Operations == 2 && counters.LastOperation is
            { Mutation.Ordinal: 1, ThroughOrdinal: 2, Children: [{ Mutation.Index: 27, Mutation.Ordinal: 2, Prefix: FalloutStatisticPrefix.Complete }] } &&
            saved.LastDispatch is { Ordinal: 1, Complete: true, Children: [{ Ordinal: 2, Event: 11, Complete: true }] } &&
            dispatch.Attempts.Single().CompletionStatisticOrdinal == 2 && saved.ChallengesCompleted == 1 &&
            warm.Challenges.State(Key(0x100)) is { Completed: true, EverCompleted: true, Progress: 1 } &&
            warm.Challenges.State(Key(0x101)).Progress == 1, "Completion lost its actual nested counter/event or manually incremented another challenge.");
        using var cold = new Fixture(records, statistics: counters, challenges: saved);
        Require(cold.MenuCalls == 0 && JsonSerializer.Serialize(cold.Challenges.Capture()) == JsonSerializer.Serialize(saved) &&
            JsonSerializer.Serialize(cold.Statistics.Capture()) == JsonSerializer.Serialize(counters), "Cold completion replayed its counter/script/menu suffix.");
        cold.Statistics.Mod(27, 1, "authored-next-counter-call");
        Require(cold.Statistics.Operations == 3 && cold.Statistics.Read(27) == 2 && cold.Challenges.State(Key(0x101)).Completed,
            "Challenge27 recursively counted its own completion or failed a new actual counter operation.");
        var drift = saved with { Entries = saved.Entries.Select(row => row.Form == Key(0x100) ? row with { SourceSha256 = new('0', 64) } : row).ToArray() };
        using var refused = new Fixture(records, statistics: counters);
        var before = JsonSerializer.Serialize(refused.Challenges.Capture()); Reject(() => refused.Challenges.Restore(drift));
        Require(JsonSerializer.Serialize(refused.Challenges.Capture()) == before, "Source drift partially applied cold registry state.");
        var changed = saved with { LastDispatch = dispatch with { Statistic = dispatch.Statistic! with { Origin = "forged counter origin" } } };
        Reject(() => refused.Challenges.Restore(changed));
    }

    private static void RecurringAndSigned(string directory)
    {
        Write(directory, Challenge(0x100, 11, 1, 2, 2), Challenge(0x101, 11, int.MaxValue, 0, 3),
            Challenge(0x102, 11, -2, 0, 0, flagsTail: 0x80000000));
        using var records = Load(directory); using var fixture = new Fixture(records);
        fixture.Statistics.Mod(2, 1, "authored-recurring-first"); fixture.Statistics.Mod(2, 1, "authored-recurring-second");
        Require(fixture.Challenges.State(Key(0x100)) is { Progress: 0, RuntimeFlags: 4, Completed: false, EverCompleted: true } &&
            fixture.Statistics.Read(27) == 2, "Recurring reset or raw completed-before flag lost its source counter calls.");
        fixture.Statistics.Mod(3, int.MinValue, "authored-signed-progress"); fixture.Statistics.Mod(3, -1, "authored-progress-wrap");
        Require(fixture.Challenges.State(Key(0x101)) is { Progress: int.MaxValue, Completed: true } &&
            fixture.Statistics.Read(3) == int.MaxValue, "Progress changed the original unchecked signed Int32 consumer.");
        fixture.Statistics.Mod(0, -1, "authored-negative-threshold");
        Require(fixture.Challenges.State(Key(0x102)).Completed && fixture.Challenges.State(Key(0x102)).Progress == -1,
            "An unproven positive-threshold/known-flags reader guard rejected original signed fields.");
    }

    private static void NativeSuffixFailure(string directory)
    {
        Write(directory, Challenge(0x100, 11, 100, 0, 1));
        using var records = Load(directory); using var warm = new Fixture(records);
        warm.DuringMenu = _ => throw new IOException("Authored actual menu producer failed after CHAL11 returned.");
        Reject(() => warm.Statistics.Mod(1, 4, "authored-native-probe-failure"));
        var counters = Copy(warm.Statistics.Capture()); var saved = Copy(warm.Challenges.Capture());
        Require(counters.LastOperation is { Prefix: FalloutStatisticPrefix.MenuProbeEntered, FailureType: "System.IO.IOException", Challenge: not null } &&
            warm.Challenges.State(Key(0x100)).Progress == 4 && saved.LastDispatch!.Complete,
            "Native failure cleared the committed counter/CHAL consumer or invented absent-menu success.");
        using var cold = new Fixture(records, statistics: counters, challenges: saved);
        Reject(() => cold.Statistics.Mod(1, 4, "authored-cold-retry"));
        Require(cold.MenuCalls == 0 && cold.Statistics.Read(1) == 4, "Cold failed callback replayed its actual committed prefix.");
        using var arbitrary = new Fixture(records);
        arbitrary.DuringMenu = _ => arbitrary.Statistics.Mod(2, 1, "unowned-menu-reentry");
        Reject(() => arbitrary.Statistics.Mod(1, 1, "authored-forbidden-reentry"));
        Require(arbitrary.Statistics.Operations == 1 && arbitrary.Statistics.Read(2) == 0,
            "An arbitrary callback borrowed the challenge's synchronous reentry authority.");
        // The nested completed counter is already changed when its native
        // callback fails. Its ordinal remains owned even though Mod threw.
        WriteCompletionCounterFailure();
        void WriteCompletionCounterFailure()
        {
            var other = Path.Combine(directory, "completion-counter-fault"); Directory.CreateDirectory(other);
            Write(other, Challenge(0x100, 11, 1, 0, 1));
            using var counterRecords = Load(other); using var counter = new Fixture(counterRecords);
            counter.DuringMenu = _ => throw new IOException("Authored nested completed-counter probe fault.");
            Reject(() => counter.Statistics.Mod(1, 1, "authored-completed-counter-callback-fault"));
            var refusedCounters = Copy(counter.Statistics.Capture()); var refusedChallenges = Copy(counter.Challenges.Capture());
            Require(counter.Statistics.Read(27) == 1 && refusedCounters.Operations == 2 &&
                refusedChallenges.LastDispatch!.Attempts.Single() is { Prefix: FalloutChallengePrefix.CompletionStatisticEntered,
                    CompletionStatisticOrdinal: 2, Error: not null },
                "Throwing completed-counter callback lost its actual entered/committed operation identity.");
            using var counterCold = new Fixture(counterRecords, statistics: refusedCounters, challenges: refusedChallenges);
            Reject(() => counterCold.Statistics.Mod(1, 1, "authored-counter-fault-no-replay"));
            Require(counterCold.MenuCalls == 0 && counterCold.Statistics.Read(27) == 1,
                "Cold nested completed-counter fault repeated its already committed award.");
        }
    }

    private static void NoticePrefix(string directory)
    {
        Write(directory, Challenge(0x100, 11, 100, 0, 4, interval: 1, text: true));
        using (var records = Load(directory))
        using (var fixture = new Fixture(records, notices: true))
        {
            fixture.Statistics.Mod(4, 1, "authored-periodic-notice"); fixture.Statistics.Mod(4, -1, "authored-zero-periodic-notice");
            Require(fixture.Queue.Capture().Pending.Select(row => row.Event.Count).SequenceEqual(new[] { 1, 0 }),
                "Original signed/zero periodic count was rejected by a positive-item-count HUD rule.");
        }
        Write(directory, Challenge(0x100, 11, 1, 0, 1, text: true));
        using var completionRecords = Load(directory); using var warm = new Fixture(completionRecords, notices: true);
        Reject(() => warm.Statistics.Mod(1, 1, "authored-missing-native-cue"));
        var counters = Copy(warm.Statistics.Capture()); var challenges = Copy(warm.Challenges.Capture()); var hud = Copy(warm.Queue.Capture());
        Require(challenges.LastDispatch!.Attempts.Single() is { Prefix: FalloutChallengePrefix.InterfaceCueEntered,
            CompletionStatisticOrdinal: 2, NoticeOrdinal: 1, Error: not null } && warm.Statistics.Read(27) == 1 &&
            warm.Challenges.State(Key(0x100)) is { Progress: 1, Completed: false } && hud.Pending.Single().Event.Kind == FalloutHudEventKind.ChallengeCompleted,
            "Cue refusal discarded an actual counter/HUD prefix or fabricated final flags/award completion.");
        using var cold = new Fixture(completionRecords, notices: true, statistics: counters, challenges: challenges, hud: hud);
        Reject(() => cold.Statistics.Mod(1, 1, "authored-no-cue-prefix-replay"));
        Require(cold.MenuCalls == 0 && JsonSerializer.Serialize(cold.Queue.Capture()) == JsonSerializer.Serialize(hud),
            "Cold cue failure replayed its notice, counted a completion twice, or dropped the pending original notice.");
    }

    private static void OriginalRegistry(string directory)
    {
        Write(directory, Challenge(0x100, 11, 100, 0, 1), Challenge(0x101, 11, 100, 0, 1));
        var overridePath = Path.Combine(directory, "Override.esp");
        File.WriteAllBytes(overridePath, Join(Record("TES4", 0, fields: [Field("HEDR", new byte[12]),
            Field("MAST", Text(Plugin)), Field("DATA", new byte[8])]), Challenge(0x100, 11, 200, 0, 1),
            Record("CHAL", 0x101, flags: 0x20), Challenge(0x01000300, 11, 100, 0, 1)));
        try
        {
            using var records = FalloutPluginStack.Load(directory, [Plugin, "Override.esp"]); using var fixture = new Fixture(records);
            fixture.Statistics.Mod(1, 1, "authored-winning-registration");
            var saved = fixture.Challenges.Capture();
            Require(saved.Entries.Select(row => row.Form).SequenceEqual(new[] { Key(0x100), new FalloutFormKey("Override.esp", 0x300) }) &&
                saved.LastDispatch!.Attempts.Select(row => row.Form).SequenceEqual(saved.Entries.Select(row => row.Form).Reverse()) &&
                fixture.Challenges.State(Key(0x100)).SourceSha256 == FalloutChallengeDefinition.Read(records.GetEffective(Key(0x100))).Sha256,
                "Overrides reordered a first registration, resurrected a deleted winner, or selected a loser definition.");
            var hash = SHA256.HashData(File.ReadAllBytes(overridePath));
            using var cold = new Fixture(records, statistics: Copy(fixture.Statistics.Capture()), challenges: Copy(saved));
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(overridePath))), "Current cold registry mutated authored source bytes.");
        }
        finally { File.Delete(overridePath); }
        Write(directory, Record("CHAL", 0x100, fields: [Field("DATA", new byte[23])]));
        using var malformed = Load(directory); Reject(() => new FalloutChallenges(malformed, new()));
    }

    private static void Write(string directory, params byte[][] records)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        File.WriteAllBytes(Path.Combine(directory, Plugin), Join(Record("TES4", 0, fields: [Field("HEDR", header)]), Join(records)));
    }
    private static FalloutPluginStack Load(string directory) => FalloutPluginStack.Load(directory, [Plugin]);
    private static FalloutFormKey Key(uint id) => new(Plugin, id);
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException) { return; }
        throw new InvalidDataException("Invalid challenge source, continuation or consumer was accepted.");
    }
    private static byte[] Challenge(uint id, uint type, int threshold, uint flags, ushort value1 = 0, uint? script = null,
        uint? primary = null, uint? secondary = null, ushort value2 = 0, ushort value3 = 0,
        uint interval = 100, bool text = false, uint flagsTail = 0)
    {
        var data = new byte[24]; U32(type).CopyTo(data, 0); BitConverter.GetBytes(threshold).CopyTo(data, 4);
        U32(flags | flagsTail).CopyTo(data, 8); U32(interval).CopyTo(data, 12);
        U16(value1).CopyTo(data, 16); U16(value2).CopyTo(data, 18); U16(value3).CopyTo(data, 20);
        return Record("CHAL", id, fields: [Field("EDID", Text("Authored" + id.ToString("x"))), Field("DATA", data),
            text ? Field("FULL", Text("Authored challenge")) : [], text ? Field("DESC", Text("Authored progress")) : [],
            script is { } code ? Field("SCRI", U32(code)) : [], primary is { } one ? Field("SNAM", U32(one)) : [],
            secondary is { } two ? Field("XNAM", U32(two)) : []]);
    }
    private static byte[] Record(string name, uint id, uint flags = 0, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        U32((uint)data.Length).CopyTo(bytes, 4); U32(flags).CopyTo(bytes, 8); U32(id).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        U16(checked((ushort)data.Length)).CopyTo(bytes, 4); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + "\0");
    private static byte[] U16(ushort value) => BitConverter.GetBytes(value);
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
}
