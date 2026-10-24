namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal static class ClassicEnemyAttack
{
    internal static (ClassicCombatAction Action, ClassicRetailRandomLifecycleState Random) Prepare(
        ClassicPlayerSession player, ClassicCombatParticipant actor, ClassicCombatContracts contracts,
        long sequence, int playerAp, Func<string> playerIdle, Func<uint, string> resolveCritter)
    {
        contracts.Validate();
        var declaration = new ClassicCombatant(player, actor);
        ClassicCombatAction.RequirePlayerTraits(player);
        if (player.Inventory.Held is not null || player.Inventory.Worn is not null)
            throw new NotSupportedException("Enemy attack needs the player's held/armor/trait combat owner.");
        if (player.Initialization?.Map != player.MapPath || player.Initialization.Light != 100)
            throw new NotSupportedException("Enemy attack requires the source tile-light owner outside full ambient light.");
        var clock = player.Doors.CurrentGameTime ?? throw new NotSupportedException("Enemy attack has no campaign clock.");
        if (clock / contracts.Critical.TicksPerDay >= contracts.Critical.CriticalUpgradeEnabledAfterDays)
            throw new NotSupportedException("Enemy critical/status execution is unsupported.");
        var chance = Math.Min(95, declaration.Unarmed(contracts.Skill) - player.ArmorClass - playerAp);
        var calledFrequency = declaration.RequireAdjacentRetaliation(player, actor, chance);
        if (actor.ActionPoints < 3 || declaration.Stat(11) < 0)
            throw new InvalidOperationException("Enemy attack has insufficient source AP or invalid melee damage.");
        var maximumDamage = checked(declaration.Stat(11) + 2);
        var source = declaration.Source;
        byte[] Read(string path) => player.Catalog.Read(path, out _);
        var attack = ClassicActionClip.Read(Read, resolveCritter((source.Fid & ~0x00ff0000u) | (16u << 16)));
        var idle = playerIdle();
        if (!idle.EndsWith("aa.frm", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Player source body has no unarmed reaction family.");
        var hit = ClassicActionClip.Read(Read, idle[..^6] + "ao.frm");
        var dodge = ClassicActionClip.Read(Read, idle[..^6] + "an.frm");
        var death = ClassicActionClip.Read(Read, idle[..^6] + "ra.frm");
        if (dodge.ContactSeconds > attack.ContactSeconds)
            throw new NotSupportedException("Enemy dodge requires a delayed attacker sequence.");
        var random = player.CampaignRandom ?? throw new NotSupportedException("Enemy attack has no campaign RNG.");
        var randomContract = player.RandomContract ?? throw new NotSupportedException("Enemy attack has no RNG contract.");
        // Normal damage punch permits aiming. The AI consumes its selection
        // draw before checking intelligence; admitted intelligence below five
        // then retains the torso rather than inventing a called location.
        if (actor.ActionPoints >= 4)
        {
            var called = ClassicRetailRandom.Next(random.RandomState, 1, calledFrequency, randomContract);
            random = random with
            {
                RandomState = called.State,
                Events = [.. random.Events,
                new("ai-called-shot", "native-action:" + sequence, 1, calledFrequency, called.Value)]
            };
        }
        var roll = ClassicRetailAttackRollOwner.Roll(random.RandomState, randomContract, contracts.Critical,
            chance, declaration.Stat(6), clock);
        var hitRoll = chance - roll.Margin;
        random = random with
        {
            RandomState = roll.RandomState,
            Events = [.. random.Events,
            new("attack-hit", "native-action:" + sequence, 1, 100, hitRoll)]
        };
        int? damageRoll = null;
        var hp = player.HitPoints;
        if (roll.Outcome == ClassicRetailAttackRollOutcome.Success)
        {
            var damage = ClassicRetailAttackRollOwner.RollDamage(random.RandomState, randomContract, 1, maximumDamage, 0);
            damageRoll = damage.Damage;
            random = random with
            {
                RandomState = damage.RandomState,
                Events = [.. random.Events,
                new("attack-damage", "native-action:" + sequence, 1, maximumDamage, damage.Damage)]
            };
            hp = ClassicRetailDamageOwner.Resolve(contracts.Damage, new(damage.Damage, 2, 1, 1, 100, 0, 0, hp)).TargetHitPoints;
        }
        var reaction = damageRoll is null ? dodge : hp == 0 ? death : hit;
        var delay = attack.ContactSeconds - (damageRoll is null ? dodge.ContactSeconds : 0);
        var action = new ClassicCombatAction(sequence, player.MapPath, -1,
            player.Rotation, ClassicHexGrid.Rotation(actor.Tile, player.Tile), player.HitPoints, hp,
            actor.ActionPoints, actor.ActionPoints - 3, 0, chance, hitRoll, damageRoll, 0, 0,
            attack, reaction, delay, 0, false, null, actor.Serial + 1, maximumDamage);
        action.Validate(contracts, actor.MaximumActionPoints, maximumDamage);
        player.ValidateRandomCommit(random);
        return (action, random);
    }
}
