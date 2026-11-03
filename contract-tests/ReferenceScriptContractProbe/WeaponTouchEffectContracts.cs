using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class WeaponTouchEffectContracts
{
    // Reuse the ordinary actor-damage source fixture, not a second combat world.
    internal static void Run(FalloutPluginStack records, FalloutCellScene cell)
    {
        FalloutFormKey Key(uint id) => new("Test.esm", id);
        var effect = new FalloutIngestibleEffect(0, Key(0xa0), new string('a', 64),
            0, 16, 0x75, 10, 0, 0, 1, false, []);
        var source = new FalloutIngestible(Key(0xa1), new string('b', 64), 0, null, null, 0, null, [effect]);
        var condition = new FalloutCondition(records.GetEffective(Key(1)), 0, 1, ushort.MaxValue, 0, 0, 0, 0);
        var script = effect with { Index = 1, Archetype = 1, Duration = 8, Script = Key(0xa2), Conditions = [condition] };
        var marker = effect with { Index = 2, ActorValue = -1, Flags = 0x2030, Magnitude = 0 };
        Reject(() => FalloutInstantTouchEffects.RequireSourceOwner(source));
        FalloutInstantTouchEffects.RequireSourceOwner(source, healthDelivered: true);
        Reject(() => FalloutInstantTouchEffects.RequireSourceOwner(source with { Effects = [effect, script] }, healthDelivered: true));
        var contacts = new[] { new FalloutTouchEffectContact(Key(0x91), 0, 0, 0) };
        var blast = FalloutTouchEffectContacts.Select(records, source with { Effects = [effect, marker with { Area = 999 }] },
            contacts, new HashSet<FalloutFormKey> { Key(0x91) }, (_, _) => true);
        Require(blast.Count == 1 && blast[Key(0x91)].SequenceEqual(new[] { effect }) &&
            FalloutTouchEffectContacts.MaximumAreaRadius(records, source with { Effects = [marker with { Area = 999 }] }) == 0,
            "Explosion health delivery lost its actual target or expanded a display-only marker area.");
        var selected = FalloutWeaponTouchEffects.Select(source with { Effects = [effect, script, marker] }, _ => 0);
        Require(selected.Effects.Count == 1 && FalloutWeaponTouchEffects.HealthDamage(selected) == 10,
            "Inactive script or tooltip marker refused or changed an actual instant contact.");
        Reject(() => FalloutWeaponTouchEffects.Select(source with { Effects = [effect, script, marker] }, _ => 1));
        Reject(() => FalloutWeaponTouchEffects.Select(source with { Effects = [effect with { AdditionalPresentation = true }] }, _ => 0));
        Reject(() => FalloutWeaponTouchEffects.Select(source with { Effects = [effect with { Duration = 1 }] }, _ => 0));
        Reject(() => FalloutWeaponTouchEffects.Select(source with { Effects = [effect with { Resistance = 52 }] }, _ => 0));
        Reject(() => FalloutWeaponTouchEffects.Select(source with { Effects = [effect with { Flags = 0x71 }] }, _ => 0));
        Reject(() => FalloutWeaponTouchEffects.Select(source with { Effects = [effect with { Flags = 0x80000075 }] }, _ => 0));
        Require(FalloutWeaponTouchEffects.Select(source with { Effects = [script] }, _ => 0).Effects.Count == 0,
            "A false source condition became an active effect.");
        Require(FalloutWeaponTouchEffects.HealthDamage(FalloutWeaponTouchEffects.Select(source with
        { Effects = [effect with { Flags = 0x175 }] }, _ => 0)) == 0, "NoMagnitude was ignored.");
        Reject(() => FalloutWeaponTouchEffects.Read(records, Key(1)));

        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(cell);
        var victim = Key(0x91); var attacker = Key(0x92);
        var hit = world.DamageActor(victim, attacker, 1, FalloutWeaponTouchEffects.HealthDamage(selected),
            99, 1, healthOnly: true);
        Require(hit is { HealthBefore: 50, HealthAfter: 40, HealthDamage: 10, LimbDamage: 0, Dead: false } &&
            world.Get(victim).Injury!.LimbDamage.Count == 0,
            "Health magic borrowed the head multiplier or damaged anatomy.");
        var lethal = world.DamageActor(victim, attacker, 1, 40, 99, 1, healthOnly: true);
        Require(lethal is { HealthAfter: 0, HealthDamage: 40, LimbDamage: 0, Died: true, Dead: true } &&
            world.Get(victim).Injury is { Killer: var killer, DeathEventPending: true } && killer == attacker &&
            world.Inventory(victim, 1).Contents.Item(Key(3))!.Count == 2,
            "Magic health death lost killer, source loot or the death event.");
        var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(snapshots);
        var corpse = cold.DamageActor(victim, attacker, 1, 40, 99, 1, healthOnly: true);
        Require(corpse is { HealthAfter: 0, HealthDamage: 0, LimbDamage: 0, Died: false, Dead: true } &&
            cold.Get(victim).Injury!.LimbDamage.Count == 0 && cold.Inventory(victim, 1).Contents.Item(Key(3))!.Count == 2,
            "Cold magic contact repeated death loot, damage or limb injury.");
        Console.WriteLine("OPENNV_WEAPON_TOUCH_EFFECT_CONTRACT_PASS condition=actual marker=display-only health=unscaled death=owned cold=once unknown=visible");
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidDataException(message); }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unknown weapon effect was admitted.");
    }
}
