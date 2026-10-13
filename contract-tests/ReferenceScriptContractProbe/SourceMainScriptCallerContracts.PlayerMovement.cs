using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerMovementFlags()
    {
        var stack = new string('2', 64);
        var player = new FalloutFormKey("MovementFixture.esm", 0x14);
        var npc = new FalloutFormKey("MovementFixture.esm", 0x101);
        FalloutCombatActorIdentity Identity(FalloutFormKey actor) => new(actor, new("MovementFixture.esm", 7),
            actor == player ? "ENGINE_PLAYER" : "ACHR", 0, new('3', 64), "NPC_", 0, new('4', 64), actor == player);
        FalloutProcessGameplayState Gameplay(FalloutFormKey _) => throw new InvalidOperationException("A flag getter copied gameplay.");
        using var common = new FalloutActorProcessCommonState(
            FalloutActorProcessRuntimeDeclaration.ForExecutable(FalloutSourceMainFamily.Fallout3), stack, Identity, Gameplay,
            _ => throw new InvalidOperationException("A flag getter allocated a physical body."));
        common.Construct(player, 1, FalloutDetectionProcessLevel.High);
        common.Construct(npc, 1, FalloutDetectionProcessLevel.Low);
        var initial = common.Capture();
        var first = common.ReadAndClearPlayerMovementFlags(player, 1);
        Require(first is { Flags: 0, AfterFlags: 0, Stores: 1, Returned: true } &&
            first.Changed == initial.Sequence + 1,
            "Actual High movement flag getter omitted its independent clear of even a zero word.");
        var serialized = JsonSerializer.Deserialize<FalloutProcessCommonSnapshot>(JsonSerializer.Serialize(common.Capture()))!;
        using var cold = new FalloutActorProcessCommonState(CommonSource(), stack, Identity, Gameplay, _ => null, serialized);
        var before = cold.Capture();
        Require(before.Current.Single(entry => entry.Source.EnginePlayer).SceneFields!.MovementFlags is { Flags: 0, Stores: 1 },
            "Cold process construction replayed the source movement clear.");
        var second = cold.ReadAndClearPlayerMovementFlags(player, 1);
        Require(second.Before.Process != first.Before.Process && second.Before.GameplayOwnership == first.Before.GameplayOwnership &&
            cold.Capture().Current.Single(entry => entry.Source.EnginePlayer).SceneFields!.MovementFlags is { Flags: 0, Stores: 2 },
            "Repeated movement flag read reused an old process or reset the retained clear history.");
        Reject(() => cold.ReadAndClearPlayerMovementFlags(player, 2));
        Reject(() => cold.ReadAndClearPlayerMovementFlags(npc, 1));
        Reject(() => FalloutActorProcessCommonState.Validate(serialized with
        {
            Current = serialized.Current.Select(entry => entry.Source.EnginePlayer ? entry with
            {
                SceneFields = entry.SceneFields! with { MovementFlags = new(4, 1) }
            } : entry).ToArray()
        }));
        Reject(() => FalloutActorProcessCommonState.Validate(serialized with { Schema = "opennv-actor-process-common/v7" }));
        using var newVegas = new FalloutActorProcessCommonState(
            FalloutActorProcessRuntimeDeclaration.ForExecutable(FalloutSourceMainFamily.NewVegas), stack, Identity, Gameplay, _ => null);
        newVegas.Construct(player, 1, FalloutDetectionProcessLevel.High);
        var legacy = newVegas.Capture() with { Schema = "opennv-actor-process-common/v7" };
        using var legacyCold = new FalloutActorProcessCommonState(
            FalloutActorProcessRuntimeDeclaration.ForExecutable(FalloutSourceMainFamily.NewVegas), stack, Identity, Gameplay, _ => null, legacy);
        Require(legacyCold.Capture().Current.Single().SceneFields is null &&
            legacyCold.Capture().Current.Single().Gameplay == legacy.Current.Single().Gameplay,
            "FO3 movement fields invalidated an unchanged New Vegas v7 cold owner.");
        Console.WriteLine("OPENNV_PLAYER_MOVEMENT_FLAGS_PASS separateHighWord=true zeroClearStore=true repeated=true coldNoReplay=true missingProducerRefused=true newVegasV7Preserved=true gameplay=UNEXECUTED");
        static FalloutActorProcessRuntimeDeclaration CommonSource() =>
            FalloutActorProcessRuntimeDeclaration.ForExecutable(FalloutSourceMainFamily.Fallout3);
    }
}
