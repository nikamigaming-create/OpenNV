using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class FurnitureContracts
{
    private static void SandboxEmptyRescanContracts()
    {
        uint ticks = 100;
        var clock = new FalloutSandboxProcessClock(() => ticks);
        clock.Observe(false);
        var preferences = new FalloutSandboxPreferences();
        var empty = FalloutSandboxActivityProgress.Empty(12, preferences); empty.Validate();
        Require(empty.Activity == FalloutSandboxActivityProgress.NoActivity && empty.Duration == 0 && empty.Target is null &&
            empty.Preferences.SequenceEqual(preferences.Capture()) && FalloutSandboxProcedure.Read(1, empty.Activity) == 21 &&
            FalloutSandboxProcedure.Read(3, empty.Activity) == -1,
            "Empty Sandbox invented an activity, duration/preference draw or public procedure.");
        var owner = new FalloutSandboxRescanOwner(new(new("Fixture.esm", 1), new string('a', 64), 0, []), clock);
        var draws = 0;
        owner.Refresh([], 2, 4, () => { draws++; return 2500; });
        Require(draws == 1 && owner.Capture().RemainingMilliseconds == 2500 && owner.Candidates.Count == 0,
            "Sandbox rescan range is not upper-exclusive milliseconds with one draw.");
        ticks += 500; clock.Observe(false);
        Require(!owner.Due && owner.Capture().RemainingMilliseconds == 2000, "Empty retry rescanned before its retained deadline.");
        var stable = JsonSerializer.Serialize(owner.StableState);
        clock.Observe(true); ticks += 50000; clock.Observe(true); clock.Observe(false);
        Require(owner.Capture().RemainingMilliseconds == 2000 && JsonSerializer.Serialize(owner.StableState) == stable,
            "Paused process time advanced a rescan or rewrote its selection state.");
        var saved = JsonSerializer.Deserialize<FalloutSandboxRescanSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        var coldClock = new FalloutSandboxProcessClock(() => ticks); coldClock.Observe(false);
        var cold = new FalloutSandboxRescanOwner(saved, coldClock);
        Require(cold.Capture().RemainingMilliseconds == 2000 && draws == 1, "Cold rescan redrew its retained deadline.");
        ticks += 2000; clock.Observe(false); coldClock.Observe(false);
        Require(owner.Due && cold.Due, "Warm/cold rescan missed exact deadline equality.");
        owner.Refresh([], 3, 3, () => { draws++; return 1; });
        Require(draws == 1 && owner.Capture().RemainingMilliseconds == 3000, "Equal rescan bounds consumed a random draw.");
        var candidates = new[] { new FalloutSandboxCandidateSource(FalloutSandboxActivity.Eat, null, null, true) };
        owner.Refresh(candidates, 1, 1, () => 0); candidates[0] = new(FalloutSandboxActivity.Wander, null, null, false);
        var snapshot = owner.Capture(); snapshot.Candidates[0] = candidates[0];
        Require(owner.Candidates.Single().Activity == FalloutSandboxActivity.Eat,
            "Cached Sandbox candidates aliased caller or snapshot storage.");
    }
}
