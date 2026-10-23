using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ReferenceAccessContracts
{
    internal static void LockpickContracts()
    {
        var draws = new List<uint>(); var rewards = 0; var failures = 0;
        uint Draw(uint bound) { draws.Add(bound); return bound == uint.MaxValue ? 123u : 49u; }
        var session = new FalloutLockpickSession(50, Draw, () => ++rewards, () => ++failures);
        Require(session.Force() && rewards == 0 && failures == 0 && session.Turning,
            "A successful Force draw unlocked before the source cylinder turn.");
        session.Advance(100); session.Advance(600);
        Require(rewards == 0 && session.CylinderAngle == 45, "Turning used the wrong retained millisecond interval.");
        session.RebaseClock(); session.Advance(5000);
        Require(rewards == 0 && session.CylinderAngle == 45, "Clock rebase accrued inactive time.");
        session.Advance(5500); session.Advance(6500);
        Require(rewards == 1 && session.Finished && !session.Turning && failures == 0 &&
            draws.SequenceEqual(new uint[] { uint.MaxValue, 100 }), "Layout/force RNG order or terminal effect cardinality changed.");
        MustReject(() => session.Force());
        var equality = new FalloutLockpickSession(49, Draw, () => ++rewards, () => ++failures);
        Require(!equality.Force() && failures == 1 && rewards == 1, "Equality was admitted as force success.");
        var cancelled = new FalloutLockpickSession(100, Draw, () => ++rewards, () => ++failures);
        cancelled.Force(); cancelled.Advance(1); cancelled.Cancel(); cancelled.Advance(5000);
        Require(rewards == 1 && failures == 1, "Cancel published a pending success or failure.");
        var throwing = new FalloutLockpickSession(100, Draw,
            () => throw new InvalidOperationException("reward-prefix"), () => ++failures);
        throwing.Force(); throwing.Advance(1); MustReject(() => throwing.Advance(1001));
        throwing.Advance(2001);
        Require(throwing.Finished && failures == 1, "A throwing successful effect became replayable or a false failed force.");
        Require(FalloutLockpickSession.Skill(24.99f) == 24 && FalloutLockpickSession.Skill(25) == 25 &&
            FalloutLockpickSession.Skill(-1) == 0 && FalloutLockpickSession.Skill(100.5f) == 100,
            "Current finite skill conversion lost clamp/floor.");
        MustReject(() => FalloutLockpickSession.Skill(float.NaN));
        var liveChance = 0;
        var live = new FalloutLockpickSession(() => liveChance, Draw, () => ++rewards, () => ++failures);
        liveChance = 100;
        Require(live.Force(), "Force used constructor-time skill rather than its current owner.");
        live.Cancel();

        var directory = Path.Combine(Path.GetTempPath(), "opennv-force-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var tail = Lock(25, 20, 0, 20); UInt(tail, 12, 7); UInt(tail, 16, 9);
            var questPin = Item("MISC", 0xc1, "QuestMarkedPin"); UInt(questPin, 8, 0x400);
            File.WriteAllBytes(Path.Combine(directory, "Access.esm"), Join(Fixture(),
                questPin,
                Group(0x80, Reference("REFR", 0xc0, 1, "HistoryDoor", Field("XLOC", tail)))));
            using var records = OpenNV.Runtime.Content.FalloutPluginStack.Load(directory, ["Access.esm"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(OpenNV.Runtime.Content.FalloutCellSceneReader.Read(records, Key(0x80)));
            var inventory = new FalloutPlayerInventory(); inventory.Add(records, Key(3), 2, 1, true);
            inventory.Add(records, Key(0xc1), 1, 1, true);
            Require(FalloutLockpickSession.RequirePlainPins(records, inventory, Key(3)) == 2,
                "Ordinary plain pins were refused.");
            MustReject(() => FalloutLockpickSession.RequirePlainPins(records, inventory, Key(0xc1)));
            records.QuestObjects.Set(Key(0xc1), false);
            Require(FalloutLockpickSession.RequirePlainPins(records, inventory, Key(0xc1)) == 1,
                "Pin admission used an immutable source quest flag instead of its live owner.");
            records.QuestObjects.Set(Key(3), true);
            MustReject(() => FalloutLockpickSession.RequirePlainPins(records, inventory, Key(3)));
            records.QuestObjects.Set(Key(3), false);
            var guardedSounds = 0;
            var guarded = new FalloutLockpickSession(100, bound => bound == uint.MaxValue ? 0x80000000u : 1,
                () => ++rewards, () => ++failures);
            guarded.ConfigureManual(1000, 800, 100, 100, 1, 1,
                () =>
                {
                    _ = FalloutLockpickSession.RequirePlainPins(records, inventory, Key(3));
                    ++guardedSounds;
                    inventory.Remove(Key(3), 1, true);
                    return inventory.Item(Key(3))?.Count ?? 0;
                }, () => { }, new());
            guarded.AdvanceManual(1, 0, true); guarded.AdvanceManual(1001, 0, true);
            records.QuestObjects.Set(Key(3), true);
            MustReject(() => guarded.AdvanceManual(2001, 0, true));
            guarded.AdvanceManual(3001, 0, true);
            Require(guarded.Finished && guardedSounds == 0 && inventory.Item(Key(3))!.Count == 2,
                "Live quest-marked pin refusal lost inventory, requested break audio or replayed its prefix.");
            records.QuestObjects.Set(Key(3), false);
            var sound = 0;
            var manual = new FalloutLockpickSession(100, bound => bound == uint.MaxValue ? 0x80000000u : 1,
                () => ++rewards, () => ++failures);
            manual.ConfigureManual(1000, 500, 100, 100, 1, 1,
                () => { inventory.Remove(Key(3), 1, true); return inventory.Item(Key(3))?.Count ?? 0; },
                () => ++sound, new());
            Require(manual.Target == 500 && manual.PermittedAngle() == 90, "Fresh source layout or generated child id17 changed.");
            manual.AdvanceManual(1, 300, false);
            Require(manual.PermittedAngle() < 90 && sound == 1, "Wrong position became a full turn or movement start lost sound.");
            manual.AdvanceManual(2, 0, true); manual.AdvanceManual(1002, 0, true);
            Require(manual.ManualPhase == 4 && inventory.Item(Key(3))!.Count == 2,
                "Entering obstruction damaged/consumed the pin in the same update.");
            manual.AdvanceManual(1502, 0, false);
            Require(inventory.Item(Key(3))!.Count == 2 && manual.PinHealth == 1, "Releasing obstruction applied damage.");
            manual.AdvanceManual(1503, 0, true); manual.AdvanceManual(2003, 0, true); manual.AdvanceManual(3003, 0, true);
            Require(inventory.Item(Key(3))!.Count == 1 && manual.Target == 500 && manual.ManualPhase == 2 &&
                inventory.Notifications.Capture().LastOrdinal == 0, "Pin loss duplicated, rerolled or emitted ItemRemoved HUD.");
            manual.AdvanceManual(3004, -300, true); manual.AdvanceManual(4004, 0, true);
            Require(manual.Finished && manual.Won && inventory.Item(Key(3))!.Count == 1 && rewards == 2,
                "Correct manual turn did not succeed exactly once without pin loss.");
            manual.AdvanceManual(5004, 0, true);
            var history = world.RequirePickLock(Key(0xc0));
            Require(history.FailedPickAttempts == 7 && history.PickRewardCount == 9, "Full XLOC histories were treated as padding.");
            world.UnlockReference(Key(0xc0)); world.LockReference(Key(0xc0));
            history = world.RequirePickLock(Key(0xc0));
            Require(history.FailedPickAttempts == 0 && history.PickRewardCount == 9, "Unlock/relock reset the wrong history.");
            var active = world.RequirePickLock(Key(0x91));
            world.FailForceLock(Key(0x91), active);
            Require(world.RequirePickLock(Key(0x91)).FailedPickAttempts == 1 && world.GetLocked(Key(0x91)) == 1,
                "Failed Force unlocked or lost its retained counter.");
            world.UnlockReference(Key(0x91)); world.LockReference(Key(0x91));
            active = world.RequirePickLock(Key(0x91));
            world.CompletePickLock(Key(0x91), active, () => ++rewards);
            Require(world.GetLocked(Key(0x91)) == 0 && world.Get(Key(0x91)).LockState!.PickRewardCount == 1,
                "Successful Force did not join authoritative access/reward history.");
            world.LockReference(Key(0x91)); active = world.RequirePickLock(Key(0x91));
            world.CompletePickLock(Key(0x91), active, () => ++rewards);
            Require(rewards == 3, "A second menu/relock duplicated XP.");
            var snapshots = System.Text.Json.JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(
                System.Text.Json.JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots);
            Require(cold.Get(Key(0x91)).LockState == world.Get(Key(0x91)).LockState,
                "Canonical cold continuation lost observed lock history.");
            var legacy = snapshots.Single(value => value.Reference == Key(0x91));
            using var unknown = new FalloutReferenceWorld(records);
            unknown.Restore([legacy with { LockState = legacy.LockState! with
                { Locked = true, FailedPickAttempts = null, PickRewardCount = null }, Unlocked = false }]);
            MustReject(() => unknown.RequirePickLock(Key(0x91)));
            MustReject(() => world.RequirePickLock(Key(0xb0)));
            MustReject(() => world.RequirePickLock(Key(0x97)));
            MustReject(() => world.RequirePickLock(Key(0xa1)));
            var uncertain = world.RequirePickLock(Key(0x90));
            MustReject(() => world.CompletePickLock(Key(0x90), uncertain,
                () => throw new InvalidOperationException("partially-published-XP")));
            MustReject(() => world.RequirePickLock(Key(0x90)));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_LOCKPICK_CONTRACT_PASS layoutThenForce=true strictChance=true sourceClock=true cancel=true once=true " +
            "typedNoCrime=true sourceTails=true coldCanonical=true legacyUnknown=true liveQuestPinRefused=true manualAndOrdinaryInput=unverified");
    }

    private static void MustReject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Expected lockpick refusal.");
    }
}
