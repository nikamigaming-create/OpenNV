using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static partial class CampaignGoalContracts
{
    internal static void Run()
    {
        WithPlugins(Layers(), records =>
        {
            var hashes = records.EffectiveRecords().ToDictionary(record => record.FormKey, Hash);
            var goals = FalloutQuestTargets.Read(records, Key(0x400));
            Require(goals.Count == 3 && goals.Select(goal => goal.Ordinal).SequenceEqual(new[] { 0, 1, 2 }) &&
                goals.Select(goal => goal.Objective).SequenceEqual(new uint[] { 17, 17, 3 }) &&
                goals.All(goal => goal.Quest == Key(0x400)) &&
                goals.Select(goal => goal.Reference).SequenceEqual(new[]
                {
                    Key(0x210), Key(0x210), new FalloutFormKey(OverridePlugin, 0x600)
                }), "Winning targets lost source order, objective scope, duplicates or master/self identity.");
            Require(goals[0].Flags == 1 && goals[1].Flags == 0 && goals[0].Conditions.Count == 2 &&
                goals[1].Conditions.Count == 1 && goals[2].Conditions.Count == 0 &&
                goals.SelectMany(goal => goal.Conditions).All(condition =>
                    condition.Owner.Plugin.Name == OverridePlugin && condition.Owner.FormKey == Key(0x400)) &&
                goals[0].Conditions[0].FormArgument1 == new FalloutFormKey(OtherPlugin, 0x900) &&
                goals[0].Conditions[0].RunOn == 0 && goals[0].Conditions[1].RunOn == 2 &&
                goals[1].Conditions[0].Owner.Plugin.AdjustFormId(goals[1].Conditions[0].Reference) ==
                    new FalloutFormKey(OtherPlugin, 0x900),
                "Target CTDA admission swallowed quest/stage/neighbor conditions or guessed their subjects.");
            var previousObserver = FalloutPluginRecord.ReadObserver;
            var reads = 0;
            try
            {
                FalloutPluginRecord.ReadObserver = (_, _) => reads++;
                var graph = new FalloutCampaignPortalGraph(records);
                var constructionReads = reads;
                Require(constructionReads > 0, "Portal construction did not bind source XTEL.");
                RequireRoute(graph.Find(Key(0x300), Key(0x302), _ => true), 0x210, 0x221);
                RequireRoute(graph.Find(Key(0x300), Key(0x302), reference => reference != Key(0x210)), 0x211, 0x241);
                RequireRoute(graph.Find(Key(0x300), Key(0x302), reference => reference != Key(0x220)), 0x211, 0x241);
                RequireRoute(graph.Find(Key(0x300), Key(0x302), _ => true), 0x210, 0x221);
                Require(graph.Find(Key(0x300), Key(0x300), _ => throw new InvalidOperationException()).Count == 0,
                    "Same-cell routing invented a door or queried eligibility.");
                var reverse = Expect<NotSupportedException>(() => graph.Find(Key(0x302), Key(0x300), _ => true));
                Require(reverse.Message.Contains(Key(0x302).ToString(), StringComparison.Ordinal) &&
                    reverse.Message.Contains(Key(0x300).ToString(), StringComparison.Ordinal),
                    "A one-way route failure lost its source endpoints.");
                var blocked = Expect<NotSupportedException>(() => graph.Find(Key(0x300), Key(0x302), _ => false));
                Require(blocked.Message.Contains(OverridePlugin, StringComparison.Ordinal) &&
                    blocked.Message.Contains(Key(0x210).ToString(), StringComparison.Ordinal),
                    "Rejected eligibility lost the winning source door.");
                Expect<NotSupportedException>(() => graph.Find(Key(0x300), Key(0x304), _ => true));
                Expect<InvalidDataException>(() => graph.Find(Key(0x100), Key(0x302), _ => true));
                var unavailable = Expect<NotSupportedException>(() => graph.Find(Key(0x300), Key(0x302),
                    _ => throw new NotSupportedException("lock owner unavailable")));
                Require(unavailable.InnerException is NotSupportedException &&
                    unavailable.Message.Contains(Key(0x210).ToString(), StringComparison.Ordinal),
                    "An unsupported eligibility query was treated as an unlocked/enabled door.");
                Require(reads == constructionReads, "Repeated route queries reread plugins instead of cached links.");
            }
            finally { FalloutPluginRecord.ReadObserver = previousObserver; }
            Require(records.EffectiveRecords().All(record => hashes[record.FormKey] == Hash(record)),
                "Source-goal or portal reads changed plugin data.");
        });
        QuestRejections();
        PortalRejections();
        PortalIssueScope();
        Console.WriteLine("OPENNV_CAMPAIGN_GOALS_PASS winningMasters=true sourceOrdinals=true targetConditions=true " +
            "directedPortals=true sourceWinners=true changingEligibility=true cachedReads=true malformedVisible=true " +
            "scopedIssues=true sourceUnchanged=true");
    }

    private static void QuestRejections()
    {
        byte[][] Objective(params byte[][] extra) =>
            [Field("QOBJ", BitConverter.GetBytes(17u)), Field("NNAM", Text("Description")), .. extra];
        void Invalid<T>(params byte[][] fields) where T : Exception =>
            WithPlugins([(BasePlugin, Join(Header(), World(), Quest(fields)))],
                records => Expect<T>(() => FalloutQuestTargets.Read(records, Key(0x400))));

        Invalid<InvalidDataException>(Field("QOBJ", new byte[2]));
        Invalid<InvalidDataException>(Field("QOBJ", BitConverter.GetBytes(17u)));
        Invalid<InvalidDataException>(Field("NNAM", Text("Orphan")));
        Invalid<InvalidDataException>(Field("QSTA", Target(0x210)));
        Invalid<InvalidDataException>(Objective(Field("QOBJ", BitConverter.GetBytes(17u)),
            Field("NNAM", Text("Duplicate"))));
        Invalid<InvalidDataException>(Objective(Field("NNAM", Text("Duplicate"))));
        Invalid<InvalidDataException>(Field("QOBJ", BitConverter.GetBytes(17u)), Field("NNAM", [65]));
        Invalid<InvalidDataException>(Objective(Field("CTDA", Condition(20, 524))));
        Invalid<InvalidDataException>(Objective(Field("QSTA", new byte[16])));
        Invalid<InvalidDataException>(Objective(Field("QSTA", Target(0))));
        Invalid<InvalidDataException>(Objective(Field("QSTA", Target(0x777))));
        Invalid<InvalidDataException>(Objective(Field("QSTA", Target(0x100))));
        Invalid<NotSupportedException>(Objective(Field("QSTA", Target(0x210, 2))));
        Invalid<InvalidDataException>(Objective(Field("QSTA", Target(0x210)), Field("CTDA", new byte[32])));
        Invalid<InvalidDataException>(Objective(Field("QSTA", Target(0x210)),
            Field("CNAM", Text("Wrong scope")), Field("CTDA", Condition(20, 524))));
        Invalid<InvalidDataException>(Objective(Field("QSTA", Target(0x210)), Field("INDX", new byte[2]),
            Field("QSTA", Target(0x211))));
        var nonFinite = Condition(28, 524);
        BinaryPrimitives.WriteSingleLittleEndian(nonFinite.AsSpan(4), float.NaN);
        Invalid<InvalidDataException>(Objective(Field("QSTA", Target(0x210)), Field("CTDA", nonFinite)));
        Invalid<InvalidDataException>(Objective(Field("QSTA", Target(0x210)),
            Field("CTDA", Condition(20, 524, flags: 0xc0))));
        WithPlugins(Layers(Quest(Objective(Field("QSTA", Target(0x212))))),
            records => Expect<InvalidDataException>(() => FalloutQuestTargets.Read(records, Key(0x400))));
        WithPlugins([(BasePlugin, Join(Header(), World(), Quest(Objective(
            Field("QSTA", Target(0x210)), Field("CTDA", Condition(28, 999, runOn: 3, flags: 2))))))], records =>
        {
            var condition = FalloutQuestTargets.Read(records, Key(0x400))[0].Conditions[0];
            Require(condition.Function == 999 && condition.RunOn == 3 && condition.Flags == 2,
                "Read-only target admission evaluated or rewrote an unowned condition.");
            Expect<InvalidDataException>(() => FalloutQuestTargets.Read(records, Key(0x100)));
        });
    }

    private static void PortalRejections()
    {
        void Invalid<T>(byte[] source, byte[]? destination = null, byte[]? cells = null) where T : Exception =>
            WithPlugins([(BasePlugin, PortalFixture(source, destination, cells))],
                records =>
                {
                    var graph = new FalloutCampaignPortalGraph(records);
                    Require(graph.Issues.Count == 1 && graph.Issues[0].Reference == Key(0x210),
                        "Malformed/unsupported source edge was erased from the named Issues lane.");
                    Expect<T>(() => graph.Find(Key(0x300), Key(0x301), _ => true));
                });

        var valid = Teleport(0x220);
        Invalid<InvalidDataException>(Reference(0x210, 0x100, extra: Field("XTEL", new byte[28])));
        Invalid<InvalidDataException>(Reference(0x210, 0x100, extra: Field("XTEL", Teleport(0))));
        Invalid<InvalidDataException>(Reference(0x210, 0x100, teleport: 0x777));
        Invalid<InvalidDataException>(Reference(0x210, 0x100, teleport: 0x220, extra: Field("XTEL", valid)));
        Invalid<NotSupportedException>(Reference(0x210, 0x100, extra: Field("XTEL", Teleport(0x220, 1))));
        var nonFinite = Teleport(0x220);
        BinaryPrimitives.WriteSingleLittleEndian(nonFinite.AsSpan(16), float.PositiveInfinity);
        Invalid<InvalidDataException>(Reference(0x210, 0x100, extra: Field("XTEL", nonFinite)));
        Invalid<InvalidDataException>(Reference(0x210, 0x101, teleport: 0x220));
        Invalid<InvalidDataException>(Reference(0x210, 0x100, teleport: 0x220), Reference(0x220, 0x101));
        Invalid<InvalidDataException>(Reference(0x210, 0x100, teleport: 0x220), Record("ACHR", 0x220));
        Invalid<InvalidDataException>(Reference(0x210, 0x100, teleport: 0x220), Record("REFR", 0x220, 0x20));
        Invalid<InvalidDataException>(Record("REFR", 0x210, 0, Field("NAME", new byte[2]), Field("XTEL", valid)));
        Invalid<InvalidDataException>(Reference(0x210, 0x100, teleport: 0x220,
            extra: Field("NAME", BitConverter.GetBytes(0x100u))));
        Invalid<InvalidDataException>(Reference(0x210, 0x100, teleport: 0x220), cells: []);
        WithPlugins([(BasePlugin, Join(Header(), Record("DOOR", 0x100), Cell(0x300), Cell(0x301),
            Reference(0x210, 0x100, teleport: 0x220), Group(0x301, Reference(0x220, 0x100))))],
            records =>
            {
                var graph = new FalloutCampaignPortalGraph(records);
                Require(graph.Issues.Count == 1 && graph.Issues[0].Reference == Key(0x210) &&
                    graph.Issues[0].Cell is null,
                    "Unplaced malformed source portal lost its unscoped divergence.");
                Expect<NotSupportedException>(() => graph.Find(Key(0x300), Key(0x301), _ => true));
            });
        WithPlugins([(BasePlugin, PortalFixture(Reference(0x210, 0x100, teleport: 0x220)))], records =>
        {
            var decoded = FalloutCellSceneReader.ReadTeleport(records.GetEffective(Key(0x210)))!;
            var scene = FalloutCellSceneReader.Read(records, Key(0x300));
            var sceneLink = scene.References.Single().Teleport!;
            Require(decoded.Door == sceneLink.Door && decoded.Flags == sceneLink.Flags &&
                decoded.Position.SequenceEqual(sceneLink.Position) && decoded.RotationRadians.SequenceEqual(sceneLink.RotationRadians),
                "Campaign portals and native cell assembly disagree on XTEL decoding.");
            RequireRoute(new FalloutCampaignPortalGraph(records).Find(Key(0x300), Key(0x301), _ => true), 0x210);
        });
    }

    private static void PortalIssueScope()
    {
        const string detached = "Detached.esp";
        FalloutFormKey DetachedKey(uint id) => new(detached, id);
        var disconnected = Join(Header(BasePlugin),
            Cell(0x0100_0400), Group(0x0100_0400, Reference(0x0100_0410, 0x100,
                extra: Field("XTEL", Teleport(0x0100_0420, 1)))),
            Cell(0x0100_0401), Group(0x0100_0401, Reference(0x0100_0420, 0x100)),
            Cell(0x0100_0402), Group(0x0100_0402, Reference(0x0100_0430, 0x100,
                extra: Field("XTEL", new byte[28]))));
        WithPlugins([(BasePlugin, PortalFixture(Reference(0x210, 0x100, teleport: 0x220))),
            (detached, disconnected)], records =>
        {
            var previousObserver = FalloutPluginRecord.ReadObserver;
            var reads = 0;
            try
            {
                FalloutPluginRecord.ReadObserver = (_, _) => reads++;
                var graph = new FalloutCampaignPortalGraph(records);
                var indexedReads = reads;
                Require(graph.Issues.Count == 2 && graph.Issues[0].Reference == DetachedKey(0x410) &&
                    graph.Issues[0].Cell == DetachedKey(0x400) &&
                    graph.Issues[0].Destination == DetachedKey(0x420) &&
                    graph.Issues[0].Winner == detached &&
                    graph.Issues[0].ErrorType == nameof(NotSupportedException) &&
                    graph.Issues[0].Error.Contains("0x00000001", StringComparison.Ordinal) &&
                    graph.Issues[1].Reference == DetachedKey(0x430) &&
                    graph.Issues[1].ErrorType == nameof(InvalidDataException),
                    "Disconnected unsupported/malformed portals lost winner, master remap or error identity.");
                bool SelectedEdge(FalloutFormKey reference) => reference == Key(0x210) || reference == Key(0x220);
                RequireRoute(graph.Find(Key(0x300), Key(0x301), SelectedEdge), 0x210);
                RequireRoute(graph.Find(Key(0x300), Key(0x301), SelectedEdge), 0x210);
                Expect<NotSupportedException>(() => graph.Find(DetachedKey(0x400), DetachedKey(0x401), _ => true));
                Expect<InvalidDataException>(() => graph.Find(DetachedKey(0x402), Key(0x301), _ => true));
                Require(graph.Issues.Count == 2 && reads == indexedReads,
                    "Scoped queries cleared global divergence or rescanned source data.");
            }
            finally { FalloutPluginRecord.ReadObserver = previousObserver; }
        });
        WithPlugins([(BasePlugin, Join(Header(), Record("DOOR", 0x100),
            Cell(0x300), Group(0x300, Reference(0x210, 0x100, teleport: 0x220)),
            Cell(0x301), Group(0x301, Reference(0x220, 0x100),
                Reference(0x221, 0x100, extra: Field("XTEL", Teleport(0x230, 1)))),
            Cell(0x302), Group(0x302, Reference(0x230, 0x100))))], records =>
        {
            var graph = new FalloutCampaignPortalGraph(records);
            RequireRoute(graph.Find(Key(0x300), Key(0x301), _ => true), 0x210);
            var error = Expect<NotSupportedException>(() => graph.Find(Key(0x300), Key(0x302), _ => true));
            Require(error.InnerException is NotSupportedException &&
                error.Message.Contains(Key(0x221).ToString(), StringComparison.Ordinal) &&
                graph.Issues.Count == 1,
                "A reachable unowned edge was traversed, skipped as a known absent edge or lost its source fault.");
            Expect<NotSupportedException>(() => graph.Find(Key(0x301), Key(0x302), _ => false));
        });
        WithPlugins([(BasePlugin, PortalFixture(Join(Reference(0x210, 0x100, teleport: 0x220),
            Reference(0x211, 0x100, extra: Field("XTEL", Teleport(0x220, 1))))))], records =>
        {
            var graph = new FalloutCampaignPortalGraph(records);
            Expect<NotSupportedException>(() => graph.Find(Key(0x300), Key(0x301), _ => true));
        });
    }

    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
    private static void RequireRoute(IReadOnlyList<FalloutFormKey> route, params uint[] doors) =>
        Require(route.SequenceEqual(doors.Select(Key)), "Source door route lost directed winner/order or current eligibility.");

    private static T Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected source admission failure {typeof(T).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
