using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

internal static class AttackVariantContracts
{
    internal static void Run()
    {
        var draws = 0;
        uint Draw(uint value) { draws++; return value; }
        foreach (var count in new[] { 2, 3, 5 })
            for (uint value = 0; value < count * 3; value++)
                Require(FalloutAttackAnimationSelection.Index(count, () => Draw(value)) == value % count,
                    "Attack selector lost the complete modulo domain or forced alternation.");
        Require(FalloutAttackAnimationSelection.Index(3, () => uint.MaxValue) == uint.MaxValue % 3,
            "Attack selection treated the unsigned draw as signed.");
        var before = draws;
        Require(FalloutAttackAnimationSelection.Index(1, () => Draw(0)) == 0 && draws == before,
            "Single-sequence preparation consumed a variant draw.");
        string[] paths = ["meshes/synthetic/1hmattackleft_a.kf", "meshes/synthetic/1hmattackleft_b.kf"];
        var selected = FalloutAttackAnimationSelection.Select(paths, () => Draw(1));
        before = draws;
        Require(FalloutAttackAnimationSelection.Select(paths, () => Draw(0), selected) == selected && draws == before,
            "Retained attack selection redrew the active KF.");
        Reject(() => FalloutAttackAnimationSelection.Select(paths, () => Draw(0), "foreign.kf"));
        Reject(() => FalloutAttackAnimationSelection.Select([paths[0], paths[0]], () => Draw(0)));
        Require(draws == before, "Malformed or changed source catalog consumed random state.");
        var start = new FalloutActorEngagement(new("Synthetic.esm", 1), "attack",
            Position: [1, 2, 3], Rotation: [0, 0, 0, 1],
            WeaponHandling: new(true, [new(new("Synthetic.esm", 2), new("Synthetic.esm", 3), 4)], 19, 23),
            AttackRandomState: 17);
        string Hash(string path) => new(path == paths[0] ? 'A' : 'B', 64);
        var active = FalloutAttackAnimationSelection.Bind(start, paths, Hash);
        Require(active.AttackRandomState != start.AttackRandomState && active.Seconds == 0 && active.StartPending,
            "Attack start failed to retain its exact draw state and unconsumed clock.");
        var mid = active with { Seconds = .3, StartPending = false };
        var cold = JsonSerializer.Deserialize<FalloutActorEngagement>(JsonSerializer.Serialize(mid))!;
        Require(Same(FalloutAttackAnimationSelection.Bind(cold, paths, Hash), mid) &&
            cold.Position!.SequenceEqual(mid.Position!) && cold.Rotation!.SequenceEqual(mid.Rotation!) &&
            cold.WeaponHandling!.Magazines.SequenceEqual(mid.WeaponHandling!.Magazines),
            "Cold attack redrew, changed its source KF/hash or replayed its clock prefix.");
        Require(cold.Transition("pursue").AttackRandomState == mid.AttackRandomState &&
            cold.Transition("pursue").Animation is null && cold.Transition("pursue").Seconds == 0,
            "Action transition discarded future random state or retained a stale KF.");
        var next = FalloutAttackAnimationSelection.Bind(cold.Transition("attack"), paths, Hash);
        var replay = FalloutAttackAnimationSelection.Bind(mid.Transition("attack"), paths, Hash);
        Require(Same(next, replay) && next.AttackRandomState != mid.AttackRandomState,
            "Warm/cold next attack did not reproduce selection from persistent RNG.");
        Require(!Same(cold, mid with { Seconds = .4 }) && !Same(cold, mid with { AttackRandomState = 29 }) &&
            !Same(cold, mid with { AnimationHash = new('C', 64) }) &&
            !Same(cold, mid with { Position = [1, 2, 4] }) &&
            !Same(cold, mid with { WeaponHandling = mid.WeaponHandling! with
                { Magazines = [mid.WeaponHandling!.Magazines[0] with { Loaded = 5 }] } }),
            "Cold attack comparison ignored a changed clock, RNG, source hash, placement or magazine.");
        Reject(() => FalloutAttackAnimationSelection.Bind(cold, paths, _ => new('C', 64)));
        Reject(() => FalloutAttackAnimationSelection.Bind(start with { AttackRandomState = null }, paths, Hash));
        Reject(() => FalloutAttackAnimationSelection.Bind(start with { Seconds = .3, StartPending = false }, paths, Hash));
        var timeline = new FalloutWeaponAnimationTimeline([new(0, "start"), new(.2f, "Hit"), new(1, "end")], 0, 1, 1);
        Require(timeline.Crossed(0, .3, true).Count(key => FalloutWeaponAnimationTimeline.DischargesWeapon(key.Text)) == 1 &&
            timeline.Crossed(cold.Seconds, .4, cold.StartPending).All(key => !FalloutWeaponAnimationTimeline.DischargesWeapon(key.Text)),
            "Cold attack replayed a consumed source discharge.");
        var inventory = new FalloutPlayerInventory();
        var handling = new FalloutWeaponHandling(inventory);
        var snapshot = handling.Capture();
        var restored = new FalloutWeaponHandling(inventory);
        restored.Restore(JsonSerializer.Deserialize<FalloutWeaponHandlingSnapshot>(JsonSerializer.Serialize(snapshot))!,
            _ => throw new InvalidDataException("Empty handling proof requested an unrelated weapon."));
        Require(handling.NextAttackRandomUInt32() == restored.NextAttackRandomUInt32() &&
            handling.Capture().AttackRandomState == restored.Capture().AttackRandomState,
            "Player shared attack randomness changed across cold handling restoration.");
        Console.WriteLine("OPENNV_ATTACK_VARIANT_CONTRACT_PASS completeModuloDomain=true replacement=true singleNoDraw=true retainedNoDraw=true sourceHash=true clockPrefix=true futureColdSelection=true playerRandomCold=true ambiguityOwnerRequired=true");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static bool Same(FalloutActorEngagement first, FalloutActorEngagement second) =>
        JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidDataException("Unowned attack continuation was admitted.");
    }
}
