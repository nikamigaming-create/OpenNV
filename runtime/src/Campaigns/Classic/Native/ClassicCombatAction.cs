using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicCombatContracts(ClassicSkillContract Skill,
    ClassicCriticalSelectionContract Critical, ClassicRetailDamageContract Damage)
{
    internal void Validate()
    {
        Skill.Validate(); Critical.Validate(); Damage.Validate();
        if (Skill.ExactBuild != "fallout2-retail-1.02" || Critical.ExactBuild != Skill.ExactBuild || Damage.ExactBuild != Skill.ExactBuild)
            throw new NotSupportedException("Attack execution contracts belong to different builds.");
    }
}

internal sealed record ClassicActionClip(string Path, string Sha256, int Frames, int Fps, int ContactFrame)
{
    internal double ContactSeconds => (double)ContactFrame / Fps;
    internal double Duration => (double)Frames / Fps;
    internal int Frame(double elapsed) => Math.Clamp((int)Math.Floor(elapsed * Fps), 0, Frames - 1);

    internal static ClassicActionClip Read(Func<string, byte[]> read, string path)
    {
        // Split-direction art needs a retained identity for each file, not the
        // hash of just one direction. That case is not silently admitted here.
        if (!path.EndsWith(".frm", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Action requires a complete six-direction FRM identity.");
        var bytes = read(path);
        if (bytes.Length < 62) throw new InvalidDataException("Action FRM header is truncated.");
        var first = Fallout1NativeFrmReader.ReadFirstFrame(bytes);
        var contact = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6));
        var fps = first.StoredFps == 0 ? 10 : first.StoredFps;
        if (first.FramesPerDirection <= 0 || contact >= first.FramesPerDirection)
            throw new InvalidDataException("Action FRM contact frame exceeds its frame sequence.");
        for (var direction = 0; direction < 6; direction++)
            for (var frame = 0; frame < first.FramesPerDirection; frame++)
            {
                var decoded = Fallout1NativeFrmReader.ReadFrame(bytes, direction, frame);
                if (decoded.FramesPerDirection != first.FramesPerDirection || decoded.StoredFps != first.StoredFps)
                    throw new InvalidDataException("Action FRM directions disagree on timing.");
            }
        return new(path, ClassicPremadeReader.Hash(bytes), first.FramesPerDirection, fps, contact);
    }
}

internal sealed record ClassicActionPose(string Path, int Frame, int Rotation);
internal sealed record ClassicCombatAction(long Sequence, string Map, int TargetSerial,
    int PlayerRotation, int TargetRotation, int HitPointsBefore, int HitPointsAfter,
    int ActionPointsBefore, int ActionPointsAfter, int AmmunitionSpent,
    int Chance, int HitRoll, int? DamageRoll, int DamageThreshold, int DamageResistance,
    ClassicActionClip Attack, ClassicActionClip Reaction, double ReactionDelay,
    double Elapsed, bool Committed, string? Boundary, int ActorHandle = int.MaxValue, int MaximumDamage = 0, long TurnNumber = 1)
{
    internal bool Hit => HitRoll <= Chance;
    internal double Duration => Math.Max(Attack.Duration, ReactionDelay + Reaction.Duration);
    internal ClassicActionPose PlayerPose => new(Attack.Path, Attack.Frame(Elapsed), PlayerRotation);
    internal ClassicActionPose? TargetPose => Elapsed < ReactionDelay ? null :
        new(Reaction.Path, Reaction.Frame(Elapsed - ReactionDelay), ActorHandle == int.MaxValue ? TargetRotation : PlayerRotation);

    internal void Validate(ClassicCombatContracts contracts, int maximumAp, int maximumDamage)
    {
        contracts.Validate();
        if (Sequence < 0 || string.IsNullOrWhiteSpace(Map) || TargetSerial < -1 || ActorHandle < 1 || (ActorHandle == int.MaxValue) != (TargetSerial >= 0) ||
            PlayerRotation is < 0 or >= 6 || TargetRotation is < 0 or >= 6 ||
            HitPointsBefore <= 0 || HitPointsAfter < 0 || HitPointsAfter > HitPointsBefore ||
            ActionPointsBefore > maximumAp || ActionPointsAfter < 0 || ActionPointsBefore - ActionPointsAfter != 3 ||
            AmmunitionSpent != 0 || MaximumDamage != maximumDamage || TurnNumber <= 0 || Chance > 95 || HitRoll is < 1 or > 100 ||
            !double.IsFinite(Elapsed) || Elapsed < 0 || Elapsed > Duration ||
            !double.IsFinite(ReactionDelay) || ReactionDelay < 0 ||
            Hit != (DamageRoll is not null) || DamageRoll is { } rolled && (rolled < 1 || rolled > maximumDamage) ||
            Committed && (Elapsed != Duration || Boundary is not null))
            throw new InvalidDataException("Retained basic-punch transaction is inconsistent.");
        foreach (var clip in new[] { Attack, Reaction })
            if (clip.Frames <= 0 || clip.Fps <= 0 || clip.ContactFrame < 0 || clip.ContactFrame >= clip.Frames ||
                string.IsNullOrWhiteSpace(clip.Path) || clip.Sha256.Length != 64)
                throw new InvalidDataException("Retained action clip is invalid.");
        var expected = DamageRoll is { } damage
            ? ClassicRetailDamageOwner.Resolve(contracts.Damage, new(damage, 2, 1, 1, 100,
                DamageThreshold, DamageResistance, HitPointsBefore)).TargetHitPoints : HitPointsBefore;
        if (expected != HitPointsAfter || Boundary is not null)
            throw new InvalidDataException("Retained action damage or unresolved death boundary changed.");
    }

    internal static int PlayerMaximumDamage(ClassicPlayerSession player) =>
        Math.Max(player.Stats.Special[0] - 5, 1) + (player.Choice.Character.Traits.Contains(6) ? 4 : 0) + 2;

    internal static void RequirePlayerTraits(ClassicPlayerSession player)
    {
        if (player.Choice.Character.Traits.Any(trait => trait is 4 or 8 or 9 or 13))
            throw new NotSupportedException("Selected trait requires its finesse, special death, jinxed or time-dependent combat owner.");
    }

    internal static (ClassicCombatAction Action, ClassicRetailRandomLifecycleState Random) PreparePunch(
        ClassicPlayerSession player, ClassicCombatParticipant target, ClassicAttackModeContract mode,
        ClassicCombatContracts contracts, long sequence, int ap, Func<string> playerIdle,
        Func<uint, string> resolveCritter)
    {
        contracts.Validate(); mode.Validate();
        if (mode.Id != "basic-punch" || mode.HitMode != 4 || mode.SkillIndex != 3 || mode.AnimationCode != 16 ||
            mode.DamageType != 0 || mode.ActionPointCost != 3 || mode.AmmunitionPerAttack != 0 ||
            mode.MaximumDamageDerivedStat != "melee-damage")
            throw new NotSupportedException("Native execution requires the qualified basic-punch contract.");
        RequirePlayerTraits(player);
        if (player.Inventory.Held is not null)
            throw new NotSupportedException("Held attacks and trait-adjusted attacks need their source execution owner.");
        var clock = player.Doors.CurrentGameTime ?? throw new NotSupportedException("Attack has no shared campaign clock.");
        if (clock / contracts.Critical.TicksPerDay >= contracts.Critical.CriticalUpgradeEnabledAfterDays)
            throw new NotSupportedException("Critical/status execution is not yet bound; no attack roll consumed.");
        // Full ambient source light bounds local light from below. No renderer
        // light value is substituted for the missing source tile-light owner.
        if (player.Initialization?.Map != player.MapPath || player.Initialization.Light != 100)
            throw new NotSupportedException("Source attack light requires full admitted ambient light or a tile-light owner.");
        var random = player.CampaignRandom ?? throw new NotSupportedException("Attack has no shared campaign RNG.");
        var randomContract = player.RandomContract ?? throw new NotSupportedException("Campaign RNG contract is absent.");
        var source = player.Level.Objects.TopLevelObjects.Single(row => row.Serial == target.Serial);
        if (source.Inventory.Count != 0) throw new NotSupportedException("Target inventory requires equipped-item combat execution.");
        if (target.Results != 0) throw new NotSupportedException($"Target status 0x{target.Results:x} requires its source status owner.");
        if (((source.Fid >> 16) & 0xff) != 0) throw new NotSupportedException("Target source pose is not standing.");
        if (ClassicHexGrid.Distance(player.Tile, target.Tile) != 1) throw new InvalidOperationException("Basic punch target is not adjacent.");
        if (ClassicHexGrid.Neighbor(target.Tile, target.Rotation) != player.Tile)
            throw new NotSupportedException($"Target facing {target.Rotation} requires its non-frontal reaction sequence (actor {target.Tile}, player {player.Tile}).");
        _ = new ClassicCombatant(player, target);
        var bytes = player.Catalog.Read(source.Prototype.LogicalPath!, out _);
        if (ClassicPremadeReader.Hash(bytes) != target.PrototypeSha256 || (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(44)) & ~0x60) != 0)
            throw new NotSupportedException("Target prototype changed or has unowned combat flags.");
        int Stat(int index) => checked(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(48 + index * 4)) +
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(188 + index * 4)));
        var profile = player.Choice.Character;
        var skill = ClassicSkillOwner.Resolve(contracts.Skill, "Unarmed", new(player.Stats.Special,
            profile.SkillBonuses[3], profile.TaggedSkills.Contains(3), (profile.Traits.Contains(15) ? -10 : 0) + (profile.Traits.Contains(10) ? -10 : 0), 0, ClassicSkillDifficulty.Normal));
        if (contracts.Skill.Skills.Single(row => row.Id == "Unarmed").DifficultyAdjusted)
            throw new NotSupportedException("Difficulty-adjusted unarmed needs a supplied difficulty owner.");
        var attackSource = ClassicAttackModeOwner.PrepareSource(mode, PlayerMaximumDamage(player) - 2);
        var intent = ClassicAttackOwner.Prepare("player", target.Map + ":" + target.Serial, 1, ap, attackSource);
        if (intent.Boundary != ClassicAttackBoundary.HitRollRequired)
            throw new InvalidOperationException("Attack refused: " + intent.Boundary);
        byte[] Read(string path) => player.Catalog.Read(path, out _);
        var idle = playerIdle();
        if (!idle.EndsWith("aa.frm", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Player source body has no unarmed animation family.");
        var attack = ClassicActionClip.Read(Read, idle[..^6] + "aq.frm");
        string ReactionPath(int animation) => resolveCritter((source.Fid & ~0x00ff0000u) | ((uint)animation << 16));
        var hit = ClassicActionClip.Read(Read, ReactionPath(14));
        var dodge = ClassicActionClip.Read(Read, ReactionPath(13));
        if (dodge.ContactSeconds > attack.ContactSeconds)
            throw new NotSupportedException("Dodge timing requires an attacker-delay sequence not yet connected.");
        var threshold = Stat(17); var resistance = Stat(24);
        if (threshold < 0 || resistance is < 0 or > 100 || Stat(9) < 0)
            throw new InvalidDataException("Target source armor values are invalid.");
        var chance = Math.Min(95, checked(skill - Stat(9) - target.ActionPoints));
        var roll = ClassicRetailAttackRollOwner.Roll(random.RandomState, randomContract, contracts.Critical, chance, player.Stats.Special[6], clock);
        if (roll.UpgradeRoll is not null) throw new InvalidOperationException("Unexpected critical RNG consumption.");
        var hitRoll = chance - roll.Margin;
        random = random with
        {
            RandomState = roll.RandomState,
            Events = [.. random.Events,
            new("attack-hit", "native-action:" + sequence, 1, 100, hitRoll)]
        };
        int? damageRoll = null;
        var hp = target.HitPoints;
        if (roll.Outcome == ClassicRetailAttackRollOutcome.Success)
        {
            var damage = ClassicRetailAttackRollOwner.RollDamage(random.RandomState, randomContract,
                attackSource.MinimumDamage, attackSource.MaximumDamage, 0);
            damageRoll = damage.Damage;
            random = random with
            {
                RandomState = damage.RandomState,
                Events = [.. random.Events,
                new("attack-damage", "native-action:" + sequence, attackSource.MinimumDamage, attackSource.MaximumDamage, damageRoll)]
            };
            hp = ClassicRetailDamageOwner.Resolve(contracts.Damage,
                new(damageRoll.Value, 2, 1, 1, 100, threshold, resistance, hp)).TargetHitPoints;
        }
        var reaction = damageRoll is null ? dodge : hp == 0
            ? ClassicActionClip.Read(Read, ReactionPath(20)) : hit;
        var delay = attack.ContactSeconds - (damageRoll is null ? dodge.ContactSeconds : 0);
        var action = new ClassicCombatAction(sequence, player.MapPath, target.Serial,
            ClassicHexGrid.Rotation(player.Tile, target.Tile), target.Rotation, target.HitPoints, hp,
            ap, ap - mode.ActionPointCost, mode.AmmunitionPerAttack, chance, hitRoll, damageRoll, threshold, resistance,
            attack, reaction, delay, 0, false, null, MaximumDamage: attackSource.MaximumDamage);
        action.Validate(contracts, player.Stats.ActionPoints, attackSource.MaximumDamage); player.ValidateRandomCommit(random);
        return (action, random);
    }
}
