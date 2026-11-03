using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class PlayerActorValueContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-player-values-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Values.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Values.esm"]);
            Require(FalloutRadiationStage.MenuMaximum(records) == 1001 && FalloutRadiationStage.Read(records)[0].Threshold == 1001 &&
                FalloutRadiationStage.DisplayRads(.1f) == 1 && FalloutRadiationStage.DisplayRads(1.49f) == 1 && FalloutRadiationStage.DisplayRads(1.4999999f) == 1 &&
                FalloutRadiationStage.DisplayRads(1.5f) == 2 && FalloutRadiationStage.DisplayRads(2.5f) == 3 &&
                FalloutRadiationStage.DisplayRads(-1) == 0,
                "RAD display used a fixed scale, lost winning threshold order or used text rounding for displayed rads.");
            var globals = new FalloutGlobalState([new(Key(19), "Switch", (byte)'f', 0, "synthetic")]);
            var inventory = new FalloutPlayerInventory();
            var owner = new FalloutPlayerActorValues(records);
            var skills = Skills(records, owner, inventory, globals);
            var vitals = FalloutPlayerVitals.FromActorValues(records, owner);
            Require(owner.Source.Player == Key(7) && owner.Source.StatsOwner == Key(7) && owner.ReadBase(5) == 5 &&
                owner.ReadPermanent(5) == 7 && owner.ReadCurrent(5) == 7, "Winning player source or unconditional ability permanent pool failed.");
            inventory.Add(records, Key(23), 1, 1, true); inventory.Equip(records, Key(23));
            Require(owner.ReadCurrent(6) == 8 && owner.ReadPermanent(6) == 5 && skills.Value("Perception") == 8,
                "Apparel was included in permanent SPECIAL or omitted from the current skill consumer.");
            Require(skills.Value(13) == 290, "Inactive carry effect altered source capacity.");
            globals.Set(Key(19), 1);
            Require(skills.Value(13) == 340, "Active carry effect did not change source capacity.");
            Require(owner.ReadCurrent(10) == 6 && owner.ReadPermanent(10) == 5 && skills.Value("Guns") == 17 &&
                vitals.State.MaximumActionPoints == 83 && vitals.State.MaximumHitPoints == 300,
                "Conditional ability, live skill or current-Agility AP derivation failed.");
            owner.AddModifier(5, FalloutActorValuePool.Permanent, .5f);
            owner.AddModifier(5, FalloutActorValuePool.Temporary, 2.25f);
            owner.AddModifier(5, FalloutActorValuePool.Damage, -3);
            Require(owner.ReadPermanent(5) == 7.5f && owner.ReadCurrent(5) == 6.75f,
                "Player getters conflated permanent, temporary or damage pools.");
            var before = owner.Capture();
            var book = new FalloutSpecialAllocationSession(40, owner.AllocationBinding);
            Require(book.Change(5, 1) && owner.ReadBase(5) == 8 && owner.ReadPermanent(5) == 10 &&
                owner.Capture().Values[5] == before.Values[5] with { Base = 8 } &&
                owner.Capture().Values.Where(pair => pair.Key != 5).All(pair => pair.Value == before.Values[pair.Key]),
                "Book editing did not floor permanent first, write only BASE or retain modifier pools.");
            // Retiring this session has no rollback or author-script stage effect.
            book = new(40, owner.AllocationBinding);
            Require(book.Values[0] == 10 && owner.ReadBase(5) == 8, "A second book session lost its immediate mutation.");
            vitals.Damage(12.25f);
            vitals.Publish(vitals.State with { ActionPoints = vitals.State.ActionPoints - 7, ExperiencePoints = 15 });
            owner.AddModifier(7, FalloutActorValuePool.Temporary, 4);
            Require(vitals.State.MaximumHitPoints == 300, "Temporary Endurance changed the permanent HP formula.");
            owner.AddModifier(7, FalloutActorValuePool.Permanent, 1);
            owner.WriteBaseInteger(7, 6);
            Require(vitals.State.MaximumHitPoints == 340 && vitals.State.ExactHitPoints == 327.75f && vitals.State.ExperiencePoints == 15,
                "Base/permanent Endurance did not update vitals or healed the retained damage/XP.");
            owner.WriteBaseInteger(10, 6);
            Require(skills.Value("Guns") == 19 && vitals.State.MaximumActionPoints == 86 && vitals.State.ActionPoints == 79,
                "Immediate base edit was not shared by skills and AP or lost the spent-AP deficit.");
            VerifyVitalWrites(records);
            VerifyVatsPublications(records);
            VerifyKarma(records);
            VerifyMovementValues(records);
            VerifyFatigue(records);
            VerifyTouchEnchantments(records);
            VerifyScripts(records);
            VerifyCold(records, owner, inventory, globals, vitals);
            VerifyTemplate(directory);
            Console.WriteLine("OPENNV_PLAYER_ACTOR_VALUES_CONTRACT_PASS enginePlayer=true sourceTemplate=true pools=true sourceConditions=true immediateBase=true scriptAuthority=true derivedConsumers=true cold=true legacy=true atomic=true parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static FalloutPlayerSkills Skills(FalloutPluginStack records, FalloutPlayerActorValues owner,
        FalloutPlayerInventory inventory, FalloutGlobalState globals)
    {
        var skills = new FalloutPlayerSkills(records, () => owner.BaseSpecial, _ => false, () => [], globals, inventory,
            Key(7), () => Key(10), () => false, actorValues: owner);
        owner.BindConstantModifiers(skills.Modifiers);
        return skills;
    }

    private static void VerifyVatsPublications(FalloutPluginStack records)
    {
        var values = new FalloutPlayerActorValues(records); values.BindConstantModifiers((_, _) => []);
        var vitals = FalloutPlayerVitals.FromActorValues(records, values);
        var initial = vitals.State.ExactActionPoints;
        var session = new FalloutVatsSession(vitals);
        var publications = new FalloutVatsPublications();
        var instance = Guid.NewGuid();
        var target = new FalloutVatsSession.Target(Key(0x200), 1, "Actor", "Head", 95, 35, true, Key(28), 25);
        var draws = 0;
        session.Add(Key(0x47), instance, target, () => { draws++; return 0; }, false, FalloutVatsActions.OrdinaryRanged(4));
        session.Add(Key(0x47), instance, target, () => { draws++; return uint.MaxValue; }, false, FalloutVatsActions.OrdinaryRanged(4));
        Require(draws == 2 && session.Queue[0].Success && !session.Queue[1].Success && session.ReservedActionPoints == 70 &&
            vitals.State.ExactActionPoints == initial, "VATS queue spent reserved AP or resampled the chosen success.");
        session.Execute();
        var sightCalls = 0;
        var chosenRoll = session.CurrentAttack!.Roll;
        session.ApplyActorLineOfSight(session.CurrentAttack.Ordinal, () => { sightCalls++; return false; });
        Require(!session.CurrentAttack!.Success && session.CurrentAttack.Roll == chosenRoll && draws == 2 &&
            session.CompletedAttacks == 0 && session.ReservedActionPoints == 70 && vitals.State.ExactActionPoints == initial,
            "Covered VATS actor retired the queued shot, resampled success, or spent AP during sight admission.");
        session.ApplyActorLineOfSight(session.CurrentAttack.Ordinal, () => throw new InvalidDataException("Chosen miss queried actor sight."));
        Require(sightCalls == 1, "Chosen VATS miss was promoted or queried again.");
        var first = session.CurrentAttack!;
        var publication = publications.Begin(session);
        session.CommitShot(first.Ordinal);
        Refuse(() => session.CommitShot(first.Ordinal));
        Refuse(() => session.CompleteAttack(true));
        Require(session.CompletedAttacks == 0 && session.PendingPublications == 1 && vitals.State.ExactActionPoints == initial - 35,
            "VATS advanced an unfinished projectile or duplicated its AP debit.");
        session.RecordImpact(first.Ordinal, target.Reference, 1, 60, 45.5f);
        publications.Finish(publication);
        Refuse(() => publications.Finish(publication));
        session.CompleteAttack(true);
        var second = session.CurrentAttack!;
        var delayed = publications.Begin(session);
        session.CommitShot(second.Ordinal);
        session.Cancel();
        Require(publications.Count == 1 && publications.SaveBlocker == "vats-projectile-continuation",
            "Cancelling VATS admitted capture with an unserialized fired continuation.");
        var replacement = new FalloutVatsSession(vitals);
        replacement.Add(Key(0x47), Guid.NewGuid(), target with { ActionPointCost = 0 }, () => 0, true, FalloutVatsAction.Pistol);
        replacement.Execute();
        var replacementPublication = publications.Begin(replacement);
        replacement.CommitShot(replacement.CurrentAttack!.Ordinal);
        Require(!replacement.CanCompleteAttack(false, 0), "Current VATS action bypassed its own pending projectile.");
        publications.Finish(replacementPublication);
        Require(!replacement.CanCompleteAttack(true, 0) && !replacement.CanCompleteAttack(false, 1),
            "VATS completed before its weapon action or queued discharge finished.");
        Require(replacement.CanCompleteAttack(false, 0) && publications.Count == 1,
            "A retired session's projectile incorrectly blocked replacement action completion.");
        replacement.CompleteAttack(true);
        Require(publications.Count == 1 && publications.SaveBlocker is not null && replacement.CompletedAttacks == 1,
            "Completing replacement VATS forgot the retired session's pending save context.");
        session.RecordImpact(second.Ordinal, target.Reference, 1, 45.5f, 40);
        publications.Finish(delayed);
        Require(session.CurrentPhase == FalloutVatsSession.Phase.Cancelled && session.PendingPublications == 0 &&
            publications.SaveBlocker is null && vitals.State.ExactActionPoints == initial - 70 && session.Impacts.Count == 2 &&
            session.Impacts[0].Ordinal == first.Ordinal && session.Impacts[1].Ordinal == second.Ordinal,
            "Cancellation refunded a fired action or lost/reassigned its later impact.");
        session.Fail(new InvalidDataException("Retained original publication failure."));
        session.Fail(new InvalidDataException("Later error."));
        Require(session.Error == "Retained original publication failure." && session.CurrentPhase == FalloutVatsSession.Phase.Faulted,
            "VATS publication failure was overwritten or retired as success.");
        var next = new FalloutVatsSession(vitals);
        next.Add(Key(0x47), Guid.NewGuid(), target with { ActionPointCost = 0 },
            () => throw new InvalidDataException("Always-hit incorrectly sampled RNG."), true, FalloutVatsActions.OrdinaryRanged(9));
        next.Execute(); next.CommitShot(next.CurrentAttack!.Ordinal); next.CompleteAttack(true);
        Require(next.CurrentPhase == FalloutVatsSession.Phase.Completed && next.Queue.Single().Roll is null &&
            next.Queue.Single().Action == FalloutVatsAction.Launcher && session.Impacts.Count == 2,
            "New VATS action reused the prior session's publication/result or invented an always-hit roll.");
        var sightOnly = new FalloutVatsSession(vitals);
        sightOnly.Add(Key(0x47), Guid.NewGuid(), target with { ActionPointCost = 0 }, () => 0, false, FalloutVatsAction.Rifle);
        sightOnly.Execute(); sightOnly.ApplyActorLineOfSight(sightOnly.CurrentAttack!.Ordinal, () => true);
        Require(sightOnly.CurrentAttack!.Success, "Clear actor sight discarded a prepared partial-visibility success.");
        Refuse(() => sightOnly.ApplyActorLineOfSight(sightOnly.CurrentAttack.Ordinal + 1, () => true));
        sightOnly.CommitShot(sightOnly.CurrentAttack.Ordinal);
        Refuse(() => sightOnly.ApplyActorLineOfSight(sightOnly.CurrentAttack.Ordinal, () => false));
        sightOnly.CompleteAttack(true);
        var root = new System.Numerics.Vector3(2, 3, 4);
        var points = FalloutActorSight.Points(root, 8, new(20, 30, 40), new(50, 60, 70));
        Require(points[0] == new System.Numerics.Vector3(20, 30, 40) && points[1] == new System.Numerics.Vector3(50, 60, 70) &&
            points[2] == new System.Numerics.Vector3(2, 5, 4), "Cached sight targets replaced the permanent quarter-height candidate.");
        var tested = 0;
        Require(FalloutActorSight.AnyClear(points, point => { tested++; return point == points[2]; }) && tested == 3,
            "Blocked center/head/torso discarded an actor with a clear quarter-height sight candidate.");
        tested = 0;
        Require(!FalloutActorSight.AnyClear(points, _ => { tested++; return false; }) && tested == 3,
            "Fully blocked actor sight invented a clear candidate.");
        points = FalloutActorSight.Points(root, 8, null, null);
        Require(points[0].Y == 9 && points[1].Y == 7 && points[2].Y == 5 &&
            FalloutActorSight.InCone(new(0, 0, -1), new(0, 0, -1), 90) &&
            !FalloutActorSight.InCone(new(1, 0, 0), new(0, 0, -1), 90),
            "Source sight fractions or horizontal view cone changed.");
        Reject(() => FalloutActorSight.Points(root, float.NaN, null, null));
        Reject(() => FalloutVatsActions.OrdinaryRanged(10));

        static void Refuse(Action action)
        {
            try { action(); } catch (InvalidOperationException) { return; }
            throw new InvalidDataException("Invalid VATS action/publication transition was accepted.");
        }
    }

    private static void VerifyScripts(FalloutPluginStack records)
    {
        foreach (var shared in new[] { false, true })
        {
            var owner = new FalloutPlayerActorValues(records); owner.BindConstantModifiers((_, _) => []);
            var vitals = FalloutPlayerVitals.FromActorValues(records, owner);
            vitals.Damage(23.75f, 6, 1);
            vitals.Publish(vitals.State with { ActionPoints = 3, ExperiencePoints = 17, RadiationRads = 75 });
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var karmaCalls = new List<double>();
            void Reward(double value)
            {
                karmaCalls.Add(value);
                var reward = FalloutKarma.Reward(records, owner.KarmaInteger(), FalloutActorValueCommands.Integer(value));
                owner.CommitKarmaReward(owner.PrepareKarmaReward(reward.Amount));
            }
            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ => throw new InvalidDataException("Unexpected player effect."),
                ReadActorValue: (actor, name, kind) => actor == Key(0x14) ? owner.Read(FalloutPlayerActorValues.PoolValue(name), kind) :
                    throw new InvalidDataException("Player values fabricated a placed reference."),
                ChangeActorValue: (actor, name, operation, value) =>
                {
                    Require(actor == Key(0x14), "Source player write had a different receiver."); owner.Change(name, operation, value);
                }, ResetPlayerHealth: vitals.ResetHealth, RewardKarma: Reward));
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0);
            scripts.Host = new((_, _) => throw new InvalidOperationException("Unexpected player stage."), name => owner.ReadCurrent(FalloutPlayerActorValues.PoolValue(name)),
                shared ? executor.ExecuteProgram : null,
                ReadPlayerActorValue: (name, kind) => owner.Read(FalloutPlayerActorValues.PoolValue(name), kind), ChangePlayerActorValue: owner.Change,
                ResetPlayerHealth: vitals.ResetHealth, RewardKarma: Reward);
            scripts.Advance(0);
            Require(scripts.Capture().Instances.Single() is { Error: null } && quests.Variable(Key(0x501), 1) == 14 &&
                quests.Variable(Key(0x501), 2) == 10 && quests.Variable(Key(0x501), 3) == 20 && owner.ReadBase(5) == 14 &&
                owner.ReadCurrent(5) == 20 && owner.ReadPermanent(5) == 10 && world.InstanceCount == 0,
                "Shared/fallback scripts did not preserve raw current, bounded permanent or BASE/ModAV/ForceAV authority.");
            Require(karmaCalls.SequenceEqual(new double[] { 12, -1 }) && owner.ReadCurrent(23) == 11,
                "Shared/fallback RewardKarma lost signed argument conversion, player authority or ordinary AV write restriction.");
            Require(owner.ReadCurrent(21) == 130 && owner.ReadCurrent(72) == 1,
                "Shared/fallback scripts did not mutate the saved movement actor-value pools.");
            Require(vitals.State.ExactHitPoints == vitals.State.MaximumHitPoints && vitals.State.LimbDamage is null &&
                vitals.State.ActionPoints == 3 && vitals.State.ExperiencePoints == 17 && vitals.State.RadiationRads == 75,
                "Shared/fallback ResetHealth failed to cure HP and limbs or altered AP, XP or radiation.");
            var script = records.GetEffective(Key(0x500)); var quest = records.GetEffective(Key(0x501));
            var reset = FalloutGameModeProgram.Read("begin GameMode\nplayer.ResetHealth\nend");
            var unbound = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ => throw new InvalidDataException("Unbound reset fabricated a player.")));
            var retained = JsonSerializer.Serialize(vitals.State);
            Reject(() => unbound.ExecuteProgram(quest, script, reset, 0));
            Require(retained == JsonSerializer.Serialize(vitals.State) && world.InstanceCount == 0,
                "Unbound ResetHealth mutated vitals or created a placed player reference.");
            vitals.Damage(vitals.State.MaximumHitPoints);
            var dead = JsonSerializer.Serialize(vitals.State);
            Reject(vitals.ResetHealth);
            Require(dead == JsonSerializer.Serialize(vitals.State), "ResetHealth substituted for player resurrection.");
            var failing = FalloutGameModeProgram.Read("begin GameMode\nplayer.SetAV Strength 6\nplayer.ModAV Strength 2147483648\nplayer.SetAV Strength 9\nend");
            Reject(() => executor.ExecuteProgram(quest, script, failing, 0));
            Require(owner.ReadBase(5) == 6 && owner.Capture().Values[5].Permanent == 6,
                "Out-of-range integer coercion replayed or rolled back the executed base prefix.");
        }
    }

    private static void VerifyVitalWrites(FalloutPluginStack records)
    {
        var owner = new FalloutPlayerActorValues(records); owner.BindConstantModifiers((_, _) => []);
        var vitals = FalloutPlayerVitals.FromActorValues(records, owner);
        using var world = new FalloutReferenceWorld(records);
        var player = Key(0x14);
        void Write(string name, string operation, double value)
        {
            if (!vitals.TryChangeActorValue(name, operation, value)) owner.Change(name, operation, value);
        }
        void Command(string operation, string name, double value, bool referenceHost = false)
        {
            var function = FalloutScriptActorValues.Function(operation, () => player, player,
                () => throw new InvalidDataException("Player write requested a placed reference world."),
                (_, _, _) => throw new InvalidDataException("Write unexpectedly performed a read."),
                referenceWrite: referenceHost ? (_, field, op, amount) => Write(field, op, amount) : null,
                playerWrite: referenceHost ? null : Write)!;
            function.Invoke([new(0, name), new(value)]);
        }
        vitals.Damage(11.25f);
        var injured = vitals.State;
        Command("restoreactorvalue", "Health", 3.5);
        Require(vitals.State.ExactHitPoints == injured.ExactHitPoints + 3.5f && world.InstanceCount == 0,
            "Player RestoreAV used a world reference or lost fractional health.");
        Command("damageav", "Health", 1.25, referenceHost: true);
        Require(vitals.State.ExactHitPoints == injured.ExactHitPoints + 2.25f,
            "Reference-host player DamageAV bypassed actual vitals.");
        Command("damageactorvalue", "PerceptionCondition", 40);
        var anatomy = FalloutBodyPartData.Read(records.GetEffective(Key(0x1d)));
        Require(Math.Abs(anatomy.LimbCondition(25, vitals.State.MaximumHitPoints, vitals.State.LimbDamage) - 60) < .001f,
            "Anatomical damage wrote raw HP instead of source percentage units.");
        Command("restoreav", "PerceptionCondition", 15, referenceHost: true);
        Require(Math.Abs(anatomy.LimbCondition(25, vitals.State.MaximumHitPoints, vitals.State.LimbDamage) - 75) < .001f,
            "Reference-host anatomical restoration missed the actual injury owner.");
        var beforeFailure = JsonSerializer.Serialize(vitals.State);
        Reject(() => Command("setav", "PerceptionCondition", 100));
        Reject(() => Command("restoreav", "Health", -1));
        var absent = FalloutScriptActorValues.Function("restoreav", () => player, player,
            () => throw new InvalidDataException("Missing player owner fabricated a reference world."), (_, _, _) => 0)!;
        Reject(() => absent.Invoke([new(0, "Health"), new(1)]));
        Require(JsonSerializer.Serialize(vitals.State) == beforeFailure && world.InstanceCount == 0,
            "Rejected mutation changed player state or fabricated PlayerRef.");
        var saved = JsonSerializer.Deserialize<GameplayVitals>(beforeFailure)!;
        var cold = FalloutPlayerVitals.FromActorValues(records, owner, saved);
        Require(JsonSerializer.Serialize(cold.State) == beforeFailure,
            "Cold player state lost script health/limb changes.");
        Command("restoreav", "PerceptionCondition", float.MaxValue);
        Require(anatomy.LimbCondition(25, vitals.State.MaximumHitPoints, vitals.State.LimbDamage) == 100,
            "Finite large restoration was refused instead of clearing the source injury.");
        Command("restoreav", "Health", float.MaxValue);
        Require(vitals.State.ExactHitPoints == vitals.State.MaximumHitPoints,
            "Restoration exceeded the source health maximum.");
        vitals.Damage(vitals.State.MaximumHitPoints);
        Command("restoreav", "Health", 100);
        Require(vitals.State.ExactHitPoints == 0, "RestoreAV substituted for player resurrection.");
    }

    private static void VerifyKarma(FalloutPluginStack records)
    {
        var owner = new FalloutPlayerActorValues(records); owner.BindConstantModifiers((_, _) => []);
        Require(owner.ReadCurrent(23) == 0 && owner.Capture().KarmaSource?.Owner == Key(7), "Fresh Karma lost its source baseline/provenance.");
        foreach (var command in new[] { "setav", "modav", "forceav", "damageav", "restoreav" }) owner.Change("Karma", command, 100);
        Require(owner.ReadCurrent(23) == 0, "A non-modifiable source Karma value accepted an ordinary script write.");
        Require(FalloutKarma.Reward(records, 995, 20) == new FalloutKarmaReward(5, FalloutHudEventKind.KarmaMinorGained) &&
            FalloutKarma.Reward(records, -995, -20) == new FalloutKarmaReward(-5, FalloutHudEventKind.KarmaMinorLost) &&
            FalloutKarma.Reward(records, 1000, 1) == new FalloutKarmaReward(0, FalloutHudEventKind.KarmaMinorGained) &&
            FalloutKarma.Reward(records, -1000, -1) == new FalloutKarmaReward(0, FalloutHudEventKind.KarmaMinorLost) &&
            FalloutKarma.Reward(records, 0, -10).Notice == FalloutHudEventKind.KarmaMinorLost &&
            FalloutKarma.Reward(records, 0, -11).Notice == FalloutHudEventKind.KarmaMajorLost &&
            FalloutKarma.Reward(records, 0, 10).Notice == FalloutHudEventKind.KarmaMajorGained &&
            FalloutKarma.Reward(records, 1, int.MaxValue).Amount == int.MaxValue,
            "Karma clamp/sign/strict loss threshold or native signed addition changed.");
        foreach (var (value, expected) in new[] { (-750f, FalloutKarmaAlignment.VeryEvil), (-250f, FalloutKarmaAlignment.Evil),
            (-249f, FalloutKarmaAlignment.Neutral), (249f, FalloutKarmaAlignment.Neutral),
            (250f, FalloutKarmaAlignment.Good), (750f, FalloutKarmaAlignment.VeryGood) })
            Require(FalloutKarma.Alignment(records, value) == expected, "Karma alignment lost an inclusive source threshold.");
        owner.AddModifier(23, FalloutActorValuePool.Permanent, .9f);
        owner.AddModifier(23, FalloutActorValuePool.Temporary, .9f);
        owner.AddModifier(23, FalloutActorValuePool.Damage, -.9f);
        Require(owner.KarmaInteger() == 0 && owner.ReadCurrent(23) == .9f, "Reward integer getter truncated the combined float instead of individual pools.");
        owner.CommitKarmaReward(owner.PrepareKarmaReward(25));
        var saved = owner.Capture();
        var cold = new FalloutPlayerActorValues(records, JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(saved))!);
        cold.BindConstantModifiers((_, _) => []);
        Require(cold.Capture() == saved || JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved), "Cold Karma lost its actual pools/source receipt.");
        var old = saved with { Schema = FalloutPlayerActorValues.BeforeKarmaSchema, KarmaSource = null,
            Values = saved.Values.Where(pair => pair.Key != 23).ToDictionary() };
        var historical = new FalloutPlayerActorValues(records, old); historical.BindConstantModifiers((_, _) => []);
        Reject(() => historical.KarmaInteger()); historical.Change("Karma", "modav", 50);
        Require(!historical.Capture().Values.ContainsKey(23), "Old unknown Karma history was fabricated from a new source baseline.");
        Reject(() => new FalloutPlayerActorValues(records, saved with { KarmaSource = null }));
        Reject(() => new FalloutPlayerActorValues(records, saved with { KarmaSource = saved.KarmaSource! with { Sha256 = new string('0', 64) } }));
        VerifyKarmaNoticeDeclarations();
    }

    private static void VerifyKarmaNoticeDeclarations()
    {
        var names = new[] { "sKarmaMajorLost", "sKarmaMinorLost", "sKarmaMajorGained", "sKarmaMinorGained" };
        var code = new byte[46 * names.Length]; var settings = new Dictionary<uint, string>();
        for (var index = 0; index < names.Length; ++index)
        {
            var at = index * 46;
            new byte[] { 0x6a, 0, 0x51, 0xd9, 5 }.CopyTo(code, at); UInt(code, at + 5, 0x300);
            new byte[] { 0xd9, 0x1c, 0x24, 0x6a, 0, 0xb9 }.CopyTo(code, at + 9);
            UInt(code, at + 15, (uint)(0x200 + index)); code[at + 19] = 0xe8;
            new byte[] { 0x50, 0x6a, 0, 0xb9 }.CopyTo(code, at + 24); UInt(code, at + 28, (uint)(0x100 + index));
            code[at + 32] = 0xe8; code[at + 37] = 0x50; code[at + 38] = 0xe8;
            new byte[] { 0x83, 0xc4, 0x18 }.CopyTo(code, at + 43);
            settings[(uint)(0x100 + index)] = names[index]; settings[(uint)(0x200 + index)] = names[index] + "Image";
        }
        var notices = FalloutExecutableStringTable.ReadKarmaNotices(code, settings, address => address == 0x300 ? 3.75f : float.NaN);
        Require(notices.Count == 4 && notices.All(pair => pair.Value.Seconds == 3.75f && pair.Value.ImageSetting == pair.Key + "Image"),
            "Karma notice decoder invented lifetime or crossed authored caption/icon branches.");
        settings[0x200] = "sKarmaMinorLostImage";
        Reject(() => FalloutExecutableStringTable.ReadKarmaNotices(code, settings, _ => 3.75f));
    }

    private static void VerifyTouchEnchantments(FalloutPluginStack records)
    {
        var source = FalloutIngestible.ReadWeaponEnchantment(records, Key(0x690));
        Require(source.Effects.Single() is { ActorValue: 22, Magnitude: 12, Range: 1, EffectiveDuration: 0, AdditionalPresentation: false },
            "Source weapon enchantment lost its touch range, magnitude or MGEF owner.");
        var explosion = FalloutExplosion.Read(records, Key(0x692));
        explosion.RequireRuntimeDamageOwner();
        Require(explosion.TouchEnchantment?.Form == source.Form, "Explosion enchantment did not retain its source declaration.");
        var owner = new FalloutPlayerActorValues(records); owner.BindConstantModifiers((_, _) => []);
        var prepared = owner.PrepareTouchEnchantment(source, _ => throw new InvalidDataException("Unconditional touch invented a condition."));
        Require(owner.ReadCurrent(22) == 190, "Touch preparation applied damage before commit.");
        prepared();
        Require(owner.ReadCurrent(22) == 178, "Delivered touch effect did not reach the actual fatigue pool.");
        var reads = 0;
        var unsupported = source with { Effects = [source.Effects[0], source.Effects[0] with { Duration = 2 }] };
        Reject(() => FalloutInstantTouchEffects.Prepare(records, unsupported, _ => { reads++; return new(190); }, _ => 1));
        Require(reads == 0 && owner.ReadCurrent(22) == 178,
            "An unsupported trailing effect partially applied the enchantment or entered its value stage.");
        Reject(() => FalloutInstantTouchEffects.RequireSourceOwner(source with { Effects = [source.Effects[0] with { Area = 15 }] }));
        Reject(() => FalloutInstantTouchEffects.RequireSourceOwner(source with { Effects = [source.Effects[0] with { AdditionalPresentation = true }] }));
        Reject(() => FalloutIngestible.ReadWeaponEnchantment(records, Key(22)));
        var area = source with { Effects = [source.Effects[0] with { Area = 1 }] };
        var contacts = new[]
        {
            new FalloutTouchEffectContact(Key(0x700), 0, 0, 0), new FalloutTouchEffectContact(Key(0x701), 10, 0, 0),
            new FalloutTouchEffectContact(Key(0x702), 25, 0, 0), new FalloutTouchEffectContact(Key(0x703), 32, 0, 0),
            new FalloutTouchEffectContact(Key(0x704), 12, 1, 0)
        };
        var selected = FalloutTouchEffectContacts.Select(records, area, contacts, new HashSet<FalloutFormKey> { Key(0x700), Key(0x701) },
            (_, target) => target.Reference != Key(0x704));
        Require(selected.Count == 3 && selected.Values.All(effects => effects.Count == 1) && selected.ContainsKey(Key(0x702)) &&
            !selected.ContainsKey(Key(0x703)) && !selected.ContainsKey(Key(0x704)),
            "Magic area lost strict source-foot range/LOS, omitted its secondary contact or multiplied overlapping direct contacts.");
        var duplicates = area with { Effects = [area.Effects[0], area.Effects[0] with { Index = 1 }] };
        Require(FalloutTouchEffectContacts.Select(records, duplicates, contacts, new HashSet<FalloutFormKey> { Key(0x700), Key(0x701) },
            (_, _) => true).Values.All(effects => effects.Count == 1), "Equivalent source effect entries bypassed the caster-local duplicate rule.");
        Reject(() => FalloutInstantTouchEffects.RequireSourceOwner(area with { EnchantmentFlags = 0 }, areaDelivered: true));
        var secondBlast = owner.PrepareTouchEnchantment(area, _ => 1, areaDelivered: true); secondBlast();
        var thirdBlast = owner.PrepareTouchEnchantment(area, _ => 1, areaDelivered: true); thirdBlast();
        Require(owner.ReadCurrent(22) == 154, "Independent blast identities suppressed a fresh instantaneous fatigue application.");
    }

    private static void VerifyFatigue(FalloutPluginStack records)
    {
        var owner = new FalloutPlayerActorValues(records); owner.BindConstantModifiers((_, _) => []);
        var level = 1; owner.BindFatigueLevel(() => level);
        Require(owner.ReadBase(22) == 190, "Player fatigue did not derive from source Endurance and settings.");
        level = 3;
        Require(owner.ReadBase(22) == 210, "Player fatigue retained an obsolete level.");
        owner.Change("Fatigue", "damageav", 9.25);
        owner.AdvanceFatigue(.5);
        Require(owner.ReadCurrent(22) == 201.25f, "Fatigue recovery lost its fractional simulation time.");
        var saved = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        var cold = new FalloutPlayerActorValues(records, saved); cold.BindConstantModifiers((_, _) => []); cold.BindFatigueLevel(() => level);
        cold.AdvanceFatigue(0);
        Require(cold.ReadCurrent(22) == 201.25f, "Cold/paused fatigue changed its saved damage pool.");
        cold.AdvanceFatigue(.25);
        Require(cold.ReadCurrent(22) == 201.5f, "Cold fatigue failed to continue the fractional recovery.");
        cold.Change("Fatigue", "damageav", 1000);
        Require(cold.ReadCurrent(22) == -30, "Fatigue damage ignored the winning source minimum.");
        cold.Change("Fatigue", "restoreav", 5);
        Require(cold.ReadCurrent(22) == -25, "Fatigue restore discarded actual damage history.");
        cold.AdvanceFatigue(25);
        Require(cold.ReadCurrent(22) == 210, "Fatigue zero crossing did not clear its native recovery deficit.");
        Reject(() => FalloutPlayerActorValues.Validate(saved with { Schema = FalloutPlayerActorValues.BeforeFatigueSchema }));
        Require(FalloutActorFatigueSource.Actor(records, Key(28), null) == 190,
            "Non-autocalculated NPC fatigue substituted the authored player/health formula.");
        Require(FalloutActorFatigueSource.Actor(records, Key(0x680), null) == 140 &&
            FalloutActorFatigueSource.Actor(records, Key(0x681), null) == 0,
            "Autocalculated NPC or creature fatigue used the player formula.");
        Require(records.NumericSettings.Set("fFatigueReturnMult", 1), "Synthetic fatigue multiplier was absent.");
        var damaged = new FalloutActorValue(100, Damage: -1);
        Reject(() => FalloutFatigue.Advance(records, damaged, 1));
        Require(records.NumericSettings.Set("fFatigueReturnMult", 0), "Synthetic fatigue multiplier could not be restored.");
    }

    private static void VerifyMovementValues(FalloutPluginStack records)
    {
        var owner = new FalloutPlayerActorValues(records); owner.BindConstantModifiers((_, _) => []);
        Require(owner.ReadBase(21) == 100 && owner.ReadCurrent(72) == 0, "Movement defaults ignored ACBS or invented an ignore-limb flag.");
        owner.Change("SpeedMult", "setav", 125); owner.AddModifier(21, FalloutActorValuePool.Temporary, -3.5f);
        owner.Change("IgnoreCrippledLimbs", "modav", 1);
        var saved = owner.Capture();
        var cold = new FalloutPlayerActorValues(records,
            JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(saved))!);
        cold.BindConstantModifiers((_, _) => []);
        Require(cold.ReadCurrent(21) == 121.5f && cold.ReadPermanent(21) == 125 && cold.ReadCurrent(72) == 1,
            "Cold movement values lost their distinct saved pools or inherited a SPECIAL clamp.");
        var old = saved with { Schema = FalloutPlayerActorValues.SpecialOnlySchema,
            Values = saved.Values.Where(pair => pair.Key is >= 5 and <= 11).ToDictionary(), KarmaSource = null };
        var migrated = new FalloutPlayerActorValues(records, old); migrated.BindConstantModifiers((_, _) => []);
        Require(migrated.ReadCurrent(21) == 100 && migrated.ReadCurrent(72) == 0 &&
            old.Values.All(pair => migrated.Capture().Values[pair.Key] == pair.Value),
            "Seven-pool migration lost old values or guessed newly supported movement values.");
        Reject(() => new FalloutPlayerActorValues(records, saved with { Schema = FalloutPlayerActorValues.SpecialOnlySchema }));
        Reject(() => new FalloutPlayerActorValues(records, old with { StatsSha256 = new string('0', 64) }));
    }

    private static void VerifyCold(FalloutPluginStack records, FalloutPlayerActorValues owner, FalloutPlayerInventory inventory,
        FalloutGlobalState globals, FalloutPlayerVitals vitals)
    {
        var snapshot = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(owner.Capture()))!;
        var cold = new FalloutPlayerActorValues(records, snapshot); var coldSkills = Skills(records, cold, inventory, globals);
        var coldVitals = FalloutPlayerVitals.FromActorValues(records, cold,
            JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals.State))!);
        Require(Enumerable.Range(5, 7).All(value => owner.ReadBase(value) == cold.ReadBase(value) &&
            owner.ReadCurrent(value) == cold.ReadCurrent(value) && owner.ReadPermanent(value) == cold.ReadPermanent(value)) &&
            coldSkills.Value("Guns") == 19 && coldVitals.State == vitals.State && snapshot.Values[5].Permanent == .5f,
            "Cold player values lost pools, double-counted source effects or restored stale derived consumers.");
        var before = JsonSerializer.Serialize(cold.Capture());
        foreach (var invalid in new[]
        {
            snapshot with { Reference = 0x15 }, snapshot with { PlayerSha256 = new string('0', 64) },
            snapshot with { StatsWinner = "Wrong.esp" }, snapshot with { Values = snapshot.Values.Where(pair => pair.Key != 11).ToDictionary() },
            snapshot with { Values = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Key == 6 ? pair.Value with { Damage = float.NaN } : pair.Value) },
            snapshot with { Values = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Key == 6 ? pair.Value with { Base = 3.25f } : pair.Value) },
            snapshot with { Values = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Key == 6 ? pair.Value with { Base = MathF.BitIncrement(2147483648f) } : pair.Value) },
        })
        {
            Reject(() => cold.Restore(invalid)); Require(JsonSerializer.Serialize(cold.Capture()) == before, "Failed pool restore partially changed live values.");
        }
        Reject(() => cold.AddModifier(5, FalloutActorValuePool.Temporary, float.PositiveInfinity));
        Reject(() => cold.WriteBaseInteger(4, 5)); Reject(() => cold.Change("Guns", "setav", 5));
        Require(JsonSerializer.Serialize(cold.Capture()) == before, "Invalid mutation changed a supported player pool.");
        var legacy = new FalloutPlayerActorValues(records, legacy: new(4, 5, 6, 7, 8, 9, 10));
        legacy.BindConstantModifiers((_, _) => []);
        Require(legacy.BaseSpecial == new FalloutNativeSpecialState(4, 5, 6, 7, 8, 9, 10) &&
            legacy.Capture().Values.Values.All(value => value.Permanent == 0 && value.Temporary == 0 && value.Damage == 0),
            "Legacy seven-integer migration created current values or modifier history.");
        cold.WriteBaseInteger(5, -5);
        Require(cold.ReadBase(5) == -5 && cold.ReadPermanent(5) == 1 && cold.ReadCurrent(5) == -3.25f && cold.ReadBoundedCurrent(5) == 1,
            "A menu allocation clamp was imposed on the source BASE setter or raw script current getter.");
        foreach (var boundary in new[] { int.MinValue, int.MaxValue })
        {
            cold.WriteBaseInteger(5, boundary);
            var boundarySnapshot = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(cold.Capture()))!;
            var boundaryCold = new FalloutPlayerActorValues(records, boundarySnapshot);
            Require(cold.ReadBase(5) == (float)boundary && cold.BaseSpecial.Strength == boundary &&
                boundaryCold.ReadBase(5) == cold.ReadBase(5) && boundaryCold.BaseSpecial == cold.BaseSpecial,
                "Signed BASE boundary failed its Float32 storage or legacy integer/cold projection.");
        }
    }

    private static void VerifyTemplate(string directory)
    {
        var acbs = new byte[24]; acbs[8] = 1; acbs[22] = 2; Float(acbs, 16, -12.75f);
        var inherited = Actor(); Float(inherited, 16, 77.75f);
        File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Values.esm"),
            Record("NPC_", 7, Field("ACBS", acbs), Field("TPLT", BitConverter.GetBytes(28u))),
            Record("NPC_", 28, Field("ACBS", inherited), Field("DATA", [100, 0, 0, 0, 3, 4, 5, 6, 7, 8, 9]))));
        using var records = FalloutPluginStack.Load(directory, ["Values.esm", "Patch.esp"]);
        var owner = new FalloutPlayerActorValues(records);
        Require(owner.Source.PlayerWinner == "Patch.esp" && owner.Source.StatsOwner == Key(28) && owner.ReadBase(5) == 3 && owner.ReadBase(11) == 9,
            "Winning player template stats were replaced with the direct NPC bytes.");
        Require(owner.ReadBase(23) == -12 && owner.Capture().KarmaSource?.Owner == Key(7),
            "Karma inherited stats instead of its traits group, or lost native truncation.");
        acbs[22] = 1;
        File.WriteAllBytes(Path.Combine(directory, "Traits.esp"), Join(Header("Values.esm"),
            Record("NPC_", 7, Field("ACBS", acbs), Field("TPLT", BitConverter.GetBytes(28u)))));
        using var traits = FalloutPluginStack.Load(directory, ["Values.esm", "Patch.esp", "Traits.esp"]);
        Require(FalloutKarmaSource.Read(traits, Key(7)) is { Owner.ObjectId: 28, Initial: 77 },
            "Karma ignored actual traits inheritance or kept fractional source BASE storage.");
    }

    private static byte[] Fixture()
    {
        var condition = new byte[28]; Float(condition, 4, 1); UInt(condition, 8, 74); UInt(condition, 12, 19);
        var header = new byte[20]; UInt(header, 12, 3); header[16] = 1;
        var script = "short first\nshort second\nshort third\nbegin GameMode\nplayer.SetAV Strength 14\nplayer.ModAV Strength 2\n" +
            "set first to player.GetBaseAV Strength\nset second to player.GetPermanentActorValue Strength\n" +
            "player.ForceAV Strength 20\nset third to player.GetAV Strength\nplayer.ResetHealth\nplayer.SetAV SpeedMult 137\nplayer.ModAV SpeedMult -7\nplayer.SetAV IgnoreCrippledLimbs 1\nplayer.SetAV Karma 700\nplayer.ModAV Karma 5\nplayer.DamageAV Karma 2.4\nplayer.RestoreAV Karma 3.4\nplayer.ForceAV Karma -50\nplayer.RewardKarma 12.9\nRewardKarma -1\nend";
        return Join(Header(),
            Record("GMST", 0x660, Field("EDID", Text("iKarmaMin")), Field("DATA", BitConverter.GetBytes(-1000))),
            Record("GMST", 0x661, Field("EDID", Text("iKarmaMax")), Field("DATA", BitConverter.GetBytes(1000))),
            Record("GMST", 0x662, Field("EDID", Text("iKarmaChangeThreshold")), Field("DATA", BitConverter.GetBytes(10))),
            Setting(0x663, "fAlignEvilMaxKarma", -250), Setting(0x664, "fAlignVeryEvilMaxKarma", -750),
            Setting(0x665, "fAlignGoodMinKarma", 250), Setting(0x666, "fAlignVeryGoodMinKarma", 750),
            Record("RADS", 0x610, Field("DATA", Join(BitConverter.GetBytes(200u), new byte[4]))),
            Record("RADS", 0x611, Field("DATA", Join(BitConverter.GetBytes(1001u), new byte[4]))),
            Record("RADS", 0x612, Field("DATA", Join(BitConverter.GetBytes(600u), new byte[4]))), Record("NPC_", 7, Field("ACBS", Actor()), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5]),
                Field("SPLO", BitConverter.GetBytes(21u)), Field("SPLO", BitConverter.GetBytes(24u))), Record("RACE", 10),
            Record("NPC_", 28, Field("ACBS", Actor()), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
            Record("NPC_", 0x680, Field("ACBS", FatigueActor()), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])),
            Record("CREA", 0x681, Field("ACBS", Actor())),
            Record("ENCH", 0x690, Field("ENIT", TouchDeclaration()), TouchEffect()), TouchModifier(), TouchExplosion(),
            Record("GLOB", 19, Field("EDID", Text("Switch")), Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(0f))),
            Modifier(16, 5), Modifier(17, 6), Modifier(18, 10), Modifier(45, 13),
            Record("SPEL", 21, Field("SPIT", Declaration(4)), Effect(16, 2)),
            Record("SPEL", 24, Field("SPIT", Declaration(4)), Effect(18, 1), Field("CTDA", condition)),
            Record("ENCH", 22, Field("ENIT", Declaration(3)), Effect(17, 3), Effect(45, 50), Field("CTDA", condition)),
            Record("ARMO", 23, Field("EDID", Text("Outfit")), Field("DATA", new byte[12]),
                Field("BMDT", [4, 0, 0, 0, 0, 0, 0, 0]), Field("EITM", BitConverter.GetBytes(22u))),
            Record("BPTD", 0x1d, VitalPart()), Record("WEAP", 0x47),
            Setting(100, "fAVDHealthEnduranceOffset", 5), Setting(101, "fAVDHealthEnduranceMult", 20), Setting(102, "fAVDHealthLevelMult", 5),
            Setting(103, "fAVDActionPointsBase", 65), Setting(104, "fAVDActionPointsMult", 3),
            Setting(0x670, "fAVDFatigueBase", 90), Setting(0x671, "fAVDFatigueEnduranceMult", 20),
            Setting(0x672, "fAVDFatigueLevelMult", 10), Setting(0x673, "fAVDNPCFatigueBase", 90),
            Setting(0x674, "fAVDNPCFatigueEnduranceMult", 20), Setting(0x675, "fAVDNPCFatigueLevelMult", 10),
            Setting(0x676, "fMinFatigue", -30), Setting(0x677, "fFatigueReturnBase", 1), Setting(0x678, "fFatigueReturnMult", 0),
            Setting(0x679, "fMagicUnitsPerFoot", 22),
            Record("GMST", 105, Field("EDID", Text("iXPBase")), Field("DATA", BitConverter.GetBytes(200))),
            Record("GMST", 106, Field("EDID", Text("iXPBumpBase")), Field("DATA", BitConverter.GetBytes(150))),
            Setting(110, "fAVDCarryWeightsBase", 150), Setting(111, "fAVDCarryWeightMult", 20),
            Setting(107, "fAVDSkillSmallGunsBase", 2), Setting(108, "fAVDSkillPrimaryBonusMult", 2), Setting(109, "fAVDSkillLuckBonusMult", .5f),
            Record("SCPT", 0x500, Field("SCHR", header), Local(1, "first"), Local(2, "second"), Local(3, "third"),
                Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Text(script))),
            Record("QUST", 0x501, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x500u))));
    }

    private static byte[] VitalPart()
    {
        var data = new byte[84]; Float(data, 0, 1); data[5] = 1; data[6] = 25; data[7] = 25;
        Float(data, 24, 1); Float(data, 40, 1); Float(data, 80, 1);
        return Join(Field("BPNN", Text("Head")), Field("BPNT", Text("Head")), Field("BPND", data));
    }

    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Actor() { var result = new byte[24]; result[8] = 1; result[14] = 100; return result; }
    private static byte[] FatigueActor() { var result = Actor(); result[0] = 0x10; result[4] = 10; result[8] = 3; return result; }
    private static byte[] TouchDeclaration() { var result = Declaration(2); result[12] = 2; return result; }
    private static byte[] TouchModifier()
    {
        var data = new byte[72]; UInt(data, 0, 0x75); UInt(data, 16, uint.MaxValue); UInt(data, 68, 22);
        return Record("MGEF", 0x691, Field("DATA", data));
    }
    private static byte[] TouchEffect()
    {
        var data = new byte[20]; UInt(data, 0, 12); UInt(data, 12, 1);
        return Join(Field("EFID", BitConverter.GetBytes(0x691u)), Field("EFIT", data));
    }
    private static byte[] TouchExplosion()
    {
        var data = new byte[52]; Float(data, 4, 10); Float(data, 8, 128);
        return Record("EXPL", 0x692, Field("DATA", data), Field("EITM", BitConverter.GetBytes(0x690u)));
    }
    private static byte[] Declaration(uint type) { var result = new byte[16]; UInt(result, 0, type); return result; }
    private static byte[] Modifier(uint id, uint value) { var result = new byte[72]; UInt(result, 0, 2); UInt(result, 68, value); return Record("MGEF", id, Field("DATA", result)); }
    private static byte[] Effect(uint id, uint magnitude) { var result = new byte[20]; UInt(result, 0, magnitude); return Join(Field("EFID", BitConverter.GetBytes(id)), Field("EFIT", result)); }
    private static byte[] Setting(uint id, string name, float value) => Record("GMST", id, Field("EDID", Text(name)), Field("DATA", BitConverter.GetBytes(value)));
    private static byte[] Local(uint index, string name) { var result = new byte[24]; UInt(result, 0, index); return Join(Field("SLSD", result), Field("SCVR", Text(name))); }
    private static FalloutFormKey Key(uint id) => new("Values.esm", id);
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
    private static void UInt(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Float(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        UInt(result, 4, (uint)data.Length); UInt(result, 12, id); data.CopyTo(result, 24); return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported player actor value state was accepted.");
    }
}
