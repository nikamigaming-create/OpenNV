using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class FurnitureContracts
{
    private static byte[] FoodHeader() { var bytes = new byte[20]; bytes[4] = 2; return bytes; }

    private static byte[] EatFixture(uint id, int location = 0, int foodType = 2, uint food = 0, int count = 0)
    {
        var data = new byte[12]; data[4] = 3;
        return Record("PACK", id, Field("EDID", Text("Eat" + id)), Field("PKDT", data),
            Field("PLDT", Join(BitConverter.GetBytes(location), BitConverter.GetBytes(0x901u), BitConverter.GetBytes(0))),
            Field("PTDT", Join(BitConverter.GetBytes(foodType), BitConverter.GetBytes(food), BitConverter.GetBytes(count), new byte[4])));
    }

    private static void EatContracts(FalloutPluginStack records, FalloutCellScene cell)
    {
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        FalloutFormKey Key(uint id) => new("Seats.esm", id);
        var actor = Key(0x900); var chair = Key(0x901);
        var package = records.GetEffective(new("Find.esp", 0x40c));
        var source = FalloutEatPackage.Read(package);
        var progress = new FalloutActorEatPackageProgress(cell.Cell.FormKey);
        source.Validate(records, world, actor, progress);
        Require(source.Location.Radius == 0 && source.ContainsChair(world, actor, chair) &&
            !source.ContainsChair(world, actor, Key(0x904)), "Eat changed its authored zero radius or chair identity.");
        var restricted = FalloutEatPackage.Read(records.GetEffective(new("Find.esp", 0x40d)));
        Require(source.AcceptsFood(records, world, Key(0x715)) && restricted.AcceptsFood(records, world, Key(0x715)) &&
            !restricted.AcceptsFood(records, world, Key(0x716)), "A different food bypassed the authored Eat restriction.");
        foreach (var id in new uint[] { 0x40e, 0x40f, 0x410 })
            Reject(() => FalloutEatPackage.Read(records.GetEffective(new("Find.esp", id))));

        var clock = progress with { RetrySeconds = .01f, NoFoodSeconds = .01f };
        var idleCrossing = clock.AdvanceIdle(.02);
        Require(!idleCrossing.Retry && idleCrossing.Progress.RetrySeconds < 0 && idleCrossing.Progress.AdvanceIdle(0).Retry,
            "Eat idle retry lost its float crossing/next-update edge.");
        Require(progress.AfterIdleAttempt(0).RetrySeconds == 1 && progress.AfterIdleAttempt(1).RetrySeconds == 6,
            "Eat idle-request retry interval changed.");
        Reject(() => progress.AfterIdleAttempt(float.NaN));
        var activeRoute = clock.AdvanceNoFood(.02, false);
        Require(!activeRoute.ReturnToEat && activeRoute.Progress.NoFoodSeconds < 0 &&
            !activeRoute.Progress.AdvanceNoFood(1, false).ReturnToEat &&
            activeRoute.Progress.AdvanceNoFood(0, true) is { ReturnToEat: true, Progress.NoFoodSeconds: 20 },
            "No-food timer blocked normal movement or interrupted an accepted route.");

        var hash = FalloutActorFurnitureContinuation.RecordHash(package);
        var meal = new FalloutActorEatProgress(Key(0x715), FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(0x715))),
            null, Consumed: true, NoIdle: true);
        var motion = new FalloutActorPackageMotion(package.FormKey, hash, "meshes/fixture-eat.kf", new string('A', 64), .75, false,
            [0, 0, 0], [0, 0, 0, 1], Eat: meal, EatPackage: progress);
        motion.Validate();
        var inventoryReads = 0;
        var action = FalloutActorEatPackageProgress.NextAction(package.FormKey, motion, false, () => { inventoryReads++; return false; });
        Require(action == FalloutEatContinuationAction.ResumeMeal && inventoryReads == 0 && ReferenceEquals(motion.Eat, meal) &&
            motion.Seconds == .75 && meal.Consumed, "Consumed meal with zero remaining inventory entered approach/ingestion admission again.");
        Require(FalloutActorEatPackageProgress.NextAction(package.FormKey, null, true, () => false) == FalloutEatContinuationAction.HoldOccupied &&
            FalloutActorEatPackageProgress.NextAction(package.FormKey, null, true, () => true) == FalloutEatContinuationAction.SelectFood,
            "No-food sitting invented a meal, or food failed to leave the fallback immediately.");
        Reject(() => (motion with { Sleep = new(cell.Cell.FormKey, chair, false) }).Validate());
        var cold = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(motion))!;
        cold.Validate();
        Require(cold.Eat == meal && cold.EatPackage == progress && cold.Seconds == motion.Seconds,
            "Cold Eat lost its single consumed receipt, independent clocks or actual animation clock.");
        world.Get(actor).PackageAssignment = new(package.FormKey, hash, false);
        world.Get(actor).PackageMotion = motion;
        using var restored = new FalloutReferenceWorld(records);
        restored.Restore(world.Capture());
        Require(restored.Get(actor).PackageMotion?.Eat?.Consumed == true,
            "World restoration dropped the standalone meal ownership.");
        Console.WriteLine("OPENNV_EAT_CONTRACT_PASS source=true restrictedFood=true consumedResume=true seatedNoFood=true clocks=true cold=true physicalPlayback=separate");
    }
}
