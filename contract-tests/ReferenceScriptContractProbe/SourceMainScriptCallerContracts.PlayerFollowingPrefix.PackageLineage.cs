using System.Text.Json;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerPackageLineageContracts()
    {
        using var warm = new FollowingPrefixFixture();
        var executable = warm.Source.Controls.Main.EngineSha256;
        var current = warm.Common.ReadSourcePackageSlot(warm.Player, 1, FalloutProcessPackageSlot.Current);
        var primary = warm.Common.ReadSourcePackageSlot(warm.Player, 1, FalloutProcessPackageSlot.Primary);
        var constructor = warm.Common.Capture();
        FalloutActorProcessCommonState.Validate(constructor);
        FalloutProcessPackageState.ValidateRead(current, executable, warm.Stack, constructor);
        FalloutProcessPackageState.ValidateRead(primary, executable, warm.Stack, constructor);
        Reject(() => FalloutProcessPackageState.ValidateRead(current with
        { Process = current.Process with { GameplayOwnership = Guid.NewGuid() } }, executable, warm.Stack, constructor));
        Reject(() => FalloutProcessPackageState.ValidateRead(current with
        { Process = current.Process with { Changed = 0 } }, executable, warm.Stack, constructor));
        Reject(() => FalloutProcessPackageState.ValidateRead(current with
        { Process = current.Process with { Changed = constructor.Sequence + 1 } }, executable, warm.Stack, constructor));
        foreach (var words in new[]
        {
            current.Fields.Fields with { Procedure = Guid.NewGuid() }, current.Fields.Fields with { Extra = 1 },
            current.Fields.Fields with { Index = 0 }, current.Fields.Fields with { TimeBits = 0 },
            current.Fields.Fields with { Flags = 1 }
        })
            Reject(() => FalloutProcessPackageState.ValidateRead(current with
            { Fields = current.Fields with { Fields = words } }, executable, warm.Stack));
        var move = current.Fields.InitialMove ?? throw new InvalidDataException("Original Player getter lost its source move receipt.");
        Reject(() => FalloutProcessPackageState.ValidateRead(current with
        { Fields = current.Fields with { InitialMove = move with { DestinationStores = 3 } } }, executable, warm.Stack));

        var observation = Guid.NewGuid();
        warm.Common.ObserveSourcePackageAssignment(warm.Player, 1, observation,
            "authored-actual-package-lineage-writer", null, null, true, FalloutProcessPackageWriterTarget.Both);
        var retained = warm.Common.Capture();
        var original = Slots(retained);
        var originalJson = JsonSerializer.Serialize(retained);
        FalloutProcessPackageState.ValidateRead(current, executable, warm.Stack, retained);
        Reject(() => FalloutProcessPackageState.ValidateRead(current with
        { Process = current.Process with { Changed = retained.Current.Single(entry => entry.Source.EnginePlayer).Changed } },
            executable, warm.Stack, retained));
        Reject(() => FalloutProcessPackageState.ValidateRead(current with
        { Fields = current.Fields with { Changed = 1 } }, executable, warm.Stack));
        var saved = warm.Snapshot();
        FalloutProcessCommonSnapshot final = retained;
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            using var cold = new FollowingPrefixFixture(saved);
            var captured = cold.Common.Capture();
            FalloutActorProcessCommonState.Validate(captured);
            var slots = Slots(captured);
            var publications = captured.Publications ?? throw new InvalidDataException("Cold Common omitted its independently published process lineage.");
            Require(publications.Count == cycle + 2 && publications[0].Process == retained.CapturedProcess &&
                publications[^1].Process == captured.CapturedProcess &&
                slots.Current.Constructor == original.Current.Constructor && slots.Primary?.Constructor == original.Primary?.Constructor &&
                JsonSerializer.Serialize(slots.RetiredInitialLow) == JsonSerializer.Serialize(original.RetiredInitialLow),
                "Cold package fields replayed construction or republished their distinct retired initial Low factory.");
            foreach (var fields in new[] { slots.Current, slots.Primary ?? throw new InvalidDataException("Cold primary package owner was omitted.") })
            {
                var lineage = fields.Publications ?? throw new InvalidDataException("Cold package field omitted a historical factory.");
                Require(lineage.Count == cycle + 2 && lineage.Select(item => item.Process).SequenceEqual(publications.Select(item => item.Process)) &&
                    lineage[0].Factory == fields.Constructor.Owner && lineage[^1] == fields.Publication &&
                    lineage[^1].PreviousProcess == lineage[^2].Process && lineage[^1].PreviousFactory == lineage[^2].Factory &&
                    lineage.Select(item => item.Factory).Distinct().Count() == lineage.Count &&
                    fields is { Available: false, Observations.Count: 1 } && fields.Observations[0].Observation == observation &&
                    fields.Observations[0].Process == retained.CapturedProcess,
                    "Cold package state lost complete actual factory/writer history or admitted a stale constructor getter.");
            }
            FalloutProcessPackageState.ValidateRead(current, executable, warm.Stack, captured);
            FalloutProcessPackageState.ValidateRead(primary, executable, warm.Stack, captured);
            Reject(() => cold.Common.ReadSourcePackageSlot(cold.Player, 1, FalloutProcessPackageSlot.Current));
            Require(JsonSerializer.Serialize(retained) == originalJson, "Cold publication mutated an older captured package lineage.");
            saved = cold.Snapshot(); final = captured;
        }
        var finalSlots = Slots(final);
        RejectCommon(ChangeCurrent(final, fields => fields with { Constructor = fields.Constructor with { Process = Guid.NewGuid() } }));
        RejectCommon(ChangeCurrent(final, fields => fields with
        { Observations = fields.Observations.Select(item => item with { Process = Guid.NewGuid() }).ToArray() }));
        RejectCommon(ChangeCurrent(final, fields => ChangeLatest(fields, publication => publication with { PreviousProcess = Guid.NewGuid() })));
        RejectCommon(ChangeCurrent(final, fields => ChangeLatest(fields, publication => publication with { PreviousFactory = Guid.NewGuid() })));
        RejectCommon(ChangeCurrent(final, fields => ChangeLatest(fields, publication => publication with { Factory = fields.Constructor.Owner })));
        RejectCommon(ChangeCurrent(final, fields => fields with { Publications = fields.Publications!.Skip(1).ToArray() }));
        var foreign = Guid.NewGuid();
        RejectCommon(ChangeCurrent(final, fields =>
        {
            var lineage = fields.Publications ?? throw new InvalidDataException("Missing authored current package lineage.");
            var authored = lineage.Select((item, index) => index == 0 ? item with { Process = foreign } :
                index == 1 ? item with { PreviousProcess = foreign } : item).ToArray();
            return fields with
            {
                Constructor = fields.Constructor with { Process = foreign }, Publications = authored, Publication = authored[^1],
                Observations = fields.Observations.Select(item => item with { Process = foreign }).ToArray()
            };
        }));
        var knownProcess = final.Publications ?? throw new InvalidDataException("Final Common process lineage was omitted.");
        RejectCommon(ChangeCurrent(final, fields => fields with
        { Observations = fields.Observations.Select(item => item with { Process = knownProcess[^1].Process }).ToArray() }));
        var foreignFactory = Guid.NewGuid();
        Reject(() => FalloutProcessPackageState.ValidateRead(current with
        {
            Fields = current.Fields with
            {
                Constructor = current.Fields.Constructor with { Owner = foreignFactory },
                Publication = new(current.Process.Process, foreignFactory),
                Publications = [new(current.Process.Process, foreignFactory)],
                InitialMove = move with { DestinationFactory = foreignFactory }
            }
        }, executable, warm.Stack, final));
        Require(finalSlots.Current.Publication.Factory != finalSlots.Primary?.Publication.Factory &&
            finalSlots.Current.Constructor.Owner != finalSlots.RetiredInitialLow?.Constructor.Owner,
            "Current/primary/retired Low package factories were aliased.");
        Console.WriteLine("OPENNV_PLAYER_PACKAGE_LINEAGE_PASS coldCycles=3 actualCommonProcessChain=true completeFactoryChain=true " +
            "constructorAndWriterHistory=true foreignProcessFactoryLeaseRefused=true immutableCaptures=true noReplay=true");

        static FalloutProcessPackageSlots Slots(FalloutProcessCommonSnapshot state) => state.Current.Single(entry => entry.Source.EnginePlayer).Packages ??
            throw new InvalidDataException("Authored package snapshot omitted its real Player slot owners.");
        static FalloutProcessCommonSnapshot ChangeCurrent(FalloutProcessCommonSnapshot state,
            Func<FalloutProcessPackageFields, FalloutProcessPackageFields> change) => state with
        {
            Current = state.Current.Select(entry => entry.Source.EnginePlayer && entry.Packages is { } slots
                ? entry with { Packages = slots with { Current = change(slots.Current) } } : entry).ToArray()
        };
        static FalloutProcessPackageFields ChangeLatest(FalloutProcessPackageFields fields,
            Func<FalloutProcessPackagePublication, FalloutProcessPackagePublication> change)
        {
            var lineage = fields.Publications ?? throw new InvalidDataException("Missing exact cold package publications.");
            var latest = change(fields.Publication);
            return fields with { Publication = latest, Publications = lineage.Take(lineage.Count - 1).Append(latest).ToArray() };
        }
        static void RejectCommon(FalloutProcessCommonSnapshot state) => Reject(() => FalloutActorProcessCommonState.Validate(state));
    }
}
