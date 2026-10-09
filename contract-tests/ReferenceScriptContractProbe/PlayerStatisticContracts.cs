using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class PlayerStatisticContracts
{
    private const string EventSource = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string DirectSource = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    internal static void Run()
    {
        foreach (var challenge in new[] { false, true })
        {
            SignedState(challenge); ActualSuffixOrder(challenge); ColdComplete(challenge);
            FaultPrefix(challenge, "probe"); FaultPrefix(challenge, "refresh"); MissingSuffix(challenge);
            if (challenge) FaultPrefix(true, "challenge");
        }
        CompiledArguments(); SourceDrift(); TransportCatalogue(false); TransportCatalogue(true);
        Console.WriteLine("OPENNV_PLAYER_STATISTICS_PASS authored=true signedInt32=true zeroNoOp=true " +
            "sourceEnumCatalogue=true compiledType41=true eventThenProbeThenRefresh=true " +
            "ioCommittedPrefix=true currentColdNoReplay=true unequalCatalogueTransport=true " +
            "sourceDriftRefused=true owned=unexecuted nativeStatsMenu=unexecuted challengeEvent=unexecuted");
    }
    internal static void RunOwnedCatalogue(string executable)
    {
        using var input = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        var before = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        var catalogue = FalloutExecutableStringTable.ReadMiscellaneousStatistics(executable, before);
        input.Position = 0;
        var after = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        Require(before == after && catalogue.Names.Count > 0 && catalogue.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalogue.Names.Count,
            "Owned statistic catalogue lost source identity/extent or changed its original input.");
        // Names/default text and original bytes stay private. This receipt only
        // admits byte transport; it executes no event, menu or player effect.
        Console.WriteLine("OPENNV_OWNED_STATISTIC_CATALOGUE_PASS source=" + before + " count=" + catalogue.Names.Count +
            " menu=" + catalogue.StatsMenuId + " originalUnchanged=true runtime=unexecuted");
    }
    private sealed class Fixture
    {
        internal readonly List<string> Order = [];
        internal readonly FalloutPlayerStatistics Owner;
        internal bool Present, MissingProbe;
        internal string? ThrowAt;
        internal int Events, Probes, Refreshes;
        internal Fixture(bool challenge, FalloutPlayerStatisticsSnapshot? restore = null)
        {
            Owner = new(Source(challenge), new((mutation, kind) =>
            {
                Events++; Order.Add("event"); Require(Owner!.Read(mutation.Index) == mutation.After, "Event overtook signed value commit.");
                Require(Owner.SaveBlocker is not null, "Entered event allowed an incomplete capture.");
                Reject(() => Owner.Capture()); _ = Owner.State;
                if (ThrowAt == "challenge") throw new IOException("Authored event failed after its actual entered call.");
                return new(mutation, kind, new('a', 64), Events);
            }, menu =>
            {
                Probes++; Order.Add("probe");
                if (MissingProbe) throw new NotSupportedException("Authored actual native-menu probe is absent.");
                if (ThrowAt == "probe") throw new IOException("Authored probe failed after the real counter/event prefix.");
                return new(menu, Present, new('b', 64), Present ? 765UL : 0, Present ? new('c', 64) : null);
            }, (mutation, menu) =>
            {
                Refreshes++; Order.Add("refresh");
                Require(Owner!.Read(mutation.Index) == mutation.After, "Native refresh saw an uncommitted counter.");
                if (ThrowAt == "refresh") throw new IOException("Authored actual refresh failed after entry.");
                return new(mutation, menu, Refreshes);
            }), restore);
        }
    }
    private static FalloutMiscellaneousStatisticSource Source(bool challenge)
    {
        var count = challenge ? 24 : 5;
        var rows = Enumerable.Range(0, count).Select(index => new FalloutMiscellaneousStatisticRow((ushort)index,
            "Authored Counter " + index, FalloutMiscellaneousStatisticSource.SettingName("sAuthoredCounter%02d", index),
            "Authored presentation " + index, 0)).ToArray();
        var consumers = FalloutMiscellaneousStatisticSource.Consumers(challenge ? EventSource : DirectSource);
        return new(challenge ? EventSource : DirectSource, new('1', 64),
            FalloutMiscellaneousStatisticSource.CurrentContractSha256, 1003,
            consumers.ChallengeEvent, consumers.SleepStartIndex, rows);
    }
    private static void SignedState(bool challenge)
    {
        var fixture = new Fixture(challenge); var owner = fixture.Owner;
        owner.Mod(ushort.MaxValue, 0, "authored-zero");
        Require(owner.Operations == 0 && fixture.Order.Count == 0 && owner.Capture().Values.All(value => value == 0),
            "Zero delta touched index/value/event/UI.");
        owner.Mod(1, int.MaxValue, "authored-first"); owner.Mod(1, 1, "authored-overflow");
        Require(owner.Read(1) == int.MinValue, "Statistic add changed unchecked signed Int32 storage.");
        owner.Mod(1, -1, "authored-negative");
        Require(owner.Read(1) == int.MaxValue, "Statistic negative add changed its signed wraparound.");
        var before = JsonSerializer.Serialize(owner.Capture()); Reject(() => owner.Mod(ushort.MaxValue, 1, "authored-invalid"));
        Require(JsonSerializer.Serialize(owner.Capture()) == before, "Invalid enum created a mutation prefix.");
    }
    private static void ActualSuffixOrder(bool challenge)
    {
        var fixture = new Fixture(challenge) { Present = true };
        fixture.Owner.Mod(2, -17, "authored-existing-native-menu");
        Require(fixture.Order.SequenceEqual(challenge ? new[] { "event", "probe", "refresh" } : new[] { "probe", "refresh" }),
            "Statistic source event/menu order changed or invented an event.");
        Require(fixture.Owner.Capture().LastOperation is { Prefix: FalloutStatisticPrefix.Complete,
            Refresh.CompletedCallOrdinal: 1, Menu.Present: true }, "Existing-menu suffix has no actual completed receipt.");
        var absent = new Fixture(challenge); absent.Owner.Mod(0, 4, "authored-actual-absent-menu");
        Require(absent.Refreshes == 0 && absent.Owner.Capture().LastOperation is { Menu.Present: false, Refresh: null },
            "Actual absent menu received a fabricated refresh.");
    }
    private static void ColdComplete(bool challenge)
    {
        var warm = new Fixture(challenge) { Present = true }; warm.Owner.Mod(3, -9, "authored-cold");
        var saved = Copy(warm.Owner.Capture()); var cold = new Fixture(challenge, saved);
        Require(cold.Order.Count == 0 && JsonSerializer.Serialize(cold.Owner.Capture()) == JsonSerializer.Serialize(saved),
            "Cold complete statistics replayed event/UI or changed the committed source/value.");
        cold.Owner.Mod(3, 8, "authored-next-real-call");
        Require(cold.Owner.Read(3) == -1 && cold.Owner.Operations == 2, "Cold statistics refused a genuinely new operation.");
    }
    private static void FaultPrefix(bool challenge, string step)
    {
        var warm = new Fixture(challenge) { Present = true, ThrowAt = step };
        Reject(() => warm.Owner.Mod(2, 7, "authored-fault"));
        var state = warm.Owner.Capture();
        var expected = step switch { "challenge" => FalloutStatisticPrefix.ChallengeEntered,
            "probe" => FalloutStatisticPrefix.MenuProbeEntered, _ => FalloutStatisticPrefix.RefreshEntered };
        Require(state.Values[2] == 7 && state.Operations == 1 && state.LastOperation is { FailureType: "System.IO.IOException" } &&
            state.LastOperation.Prefix == expected, "Ordinary callback fault lost the real value/attempted suffix.");
        var calls = warm.Order.Count; Reject(() => warm.Owner.Mod(2, 7, "authored-refused-retry"));
        Require(warm.Order.Count == calls && warm.Owner.Read(2) == 7, "Warm failure retried its counter/event/native prefix.");
        var cold = new Fixture(challenge, Copy(state)); Reject(() => cold.Owner.Mod(2, 7, "authored-cold-refused-retry"));
        Require(cold.Order.Count == 0 && JsonSerializer.Serialize(cold.Owner.Capture()) == JsonSerializer.Serialize(state),
            "Cold failure replayed or changed its attempted operation.");
        Reject(() => (state with { LastOperation = state.LastOperation! with { Prefix = FalloutStatisticPrefix.Complete } }).Validate());
        Reject(() => (state with { Values = state.Values.Select((value, index) => index == 2 ? value + 1 : value).ToArray() }).Validate());
    }
    private static void MissingSuffix(bool challenge)
    {
        var source = Source(challenge);
        var missing = new FalloutPlayerStatistics(source, new(null, null, null));
        Reject(() => missing.Mod(0, 1, "authored-missing-producer"));
        Require(missing.Capture() is { Operations: 1, LastOperation.Error: not null } && missing.Read(0) == 1,
            "Missing event/menu producer discarded the genuine value commit or reported success.");
        var direct = new Fixture(challenge) { MissingProbe = true };
        Reject(() => direct.Owner.Mod(1, 3, "authored-unowned-menu-presence"));
        Require(direct.Owner.Capture().LastOperation is { Prefix: FalloutStatisticPrefix.MenuProbeEntered, Menu: null },
            "Unknown native menu presence became genuine absence.");
    }
    private static void CompiledArguments()
    {
        var context = new FalloutCompiledOperandContext(_ => throw new InvalidDataException("Unexpected reference."),
            _ => throw new InvalidDataException("Unexpected local."), _ => throw new InvalidDataException("Unexpected global."),
            _ => throw new InvalidDataException("Unexpected query."), new());
        byte[] bytes = [2, 0, 1, 0, (byte)'n', 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5), -8);
        var decoded = FalloutCompiledOperands.Command(0x1137, null, bytes, context);
        Require(decoded.Arguments[0].SourceParameterType == 41 && decoded.Arguments[0].Value.Number == 1 &&
            decoded.Arguments[1].Value.Number == -8 && FalloutCompiledParameterNames.Read(41, 1) is null &&
            FalloutCompiledSemanticOwners.IsEffect("ModPCMiscStat") && FalloutCompiledSemanticOwners.IsQuery("GetPCMiscStat"),
            "Compiled source UInt16 statistic argument did not reach its canonical owner.");
        var fixture = new Fixture(false);
        Require(FalloutPlayerStatisticCommands.Apply("modpcmiscstat", ["1", "-8"], fixture.Owner) && fixture.Owner.Read(1) == -8,
            "Actual decoded canonical statistic command lost its real effect.");
        var function = FalloutPlayerStatisticCommands.Function("getpcmiscstat", fixture.Owner)!;
        Require(function.ReadOnly && function.InvokeValue([new(FalloutScriptValue.String("authored counter 1"))]).Number == -8 &&
            function.InvokeValue([new(1d)]).Number == -8, "Query inferred localized text or lost the signed counter.");
        var zero = FalloutPlayerStatisticCommands.Function("modpcmiscstat", fixture.Owner)!;
        _ = zero.InvokeValue([new(FalloutScriptValue.String("unbound original name")), new(0d)]);
        Require(fixture.Owner.Operations == 1, "Zero command parsed/indexed a statistic or grew its mutation history.");
        Reject(() => FalloutCompiledParameterNames.Read(41, .5)); Reject(() => FalloutCompiledParameterNames.Read(41, 65536));
        Reject(() => FalloutPlayerStatisticCommands.Index(fixture.Owner, FalloutScriptValue.Form(1)));
        Reject(() => FalloutPlayerStatisticCommands.Index(fixture.Owner, FalloutScriptValue.String("Authored presentation 1")));
        Reject(() => FalloutCompiledOperands.Command(0x1137, null, bytes.Concat(new byte[] { 0 }).ToArray(), context));
    }
    private static void SourceDrift()
    {
        var fixture = new Fixture(false); fixture.Owner.Mod(0, 2, "authored-source"); var state = fixture.Owner.Capture();
        var reordered = state.Source with { Rows = state.Source.Rows.Select((row, index) => index == 0 ? row with { ScriptName = "other original name" } : row).ToArray() };
        Reject(() => new FalloutPlayerStatistics(reordered, new(null, null, null), Copy(state)));
        Reject(() => new FalloutPlayerStatistics(state.Source with { RuntimeSha256 = new('2', 64) }, new(null, null, null), Copy(state)));
        Reject(() => (state.Source with { Rows = state.Source.Rows.Select(row => row with { Index = 0 }).ToArray() }).Validate());
        var last = state.LastOperation!;
        Reject(() => (state with { LastOperation = last with { Menu = last.Menu! with { Present = true } } }).Validate());
        Reject(() => FalloutMiscellaneousStatisticSource.SettingName("sAuthored%3d", 1));
    }
    private static FalloutPlayerStatisticsSnapshot Copy(FalloutPlayerStatisticsSnapshot state) =>
        JsonSerializer.Deserialize<FalloutPlayerStatisticsSnapshot>(JsonSerializer.Serialize(state))!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or IOException)
        { return; }
        throw new InvalidDataException("Expected statistic source/prefix refusal was accepted.");
    }
}
