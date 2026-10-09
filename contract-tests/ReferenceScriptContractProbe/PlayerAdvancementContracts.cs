using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PlayerAdvancementContracts
{
    internal static void Run()
    {
        PlayerAdvancementRateContracts.Run();
        Rates(); QueuedLevelsAndCold(); SourceUiBoundsAndBudget(); SkillAndResetPrefixes(); PerkFailureAndCold(); SourceAndAdmissionFailures();
        OrdinaryCallbackPrefixes();
        Console.WriteLine("OPENNV_PLAYER_ADVANCEMENT_CONTRACT_PASS queued=true rates=true skillWrites=true resetPrefix=true perkPrefix=true cold=true noReplay=true");
    }

    private static void SourceUiBoundsAndBudget()
    {
        var adjusted = new Fixture { BudgetAdjustment = 2 }; var perkBudget = Open(adjusted);
        Require(perkBudget.Menu!.Budget == 13, "An admitted skill-point perk increase was restricted to the base budget.");
        var capped = new Fixture { BudgetAdjustment = 2, BudgetLimit = 4 }; var capacity = Open(capped);
        Require(capacity.Menu!.Budget == 4, "The admitted source skill capacity did not reduce the adjusted budget.");

        var edge = new Fixture { Tagged = true, TaggedMultiplier = 2 }; edge.Skills[32] = 99;
        var overflow = Open(edge);
        Require(overflow.Menu!.ChangeSkill(32, 1) && edge.Skills[32] == 101 && overflow.Menu.Assigned == 1 &&
            !overflow.Menu.ChangeSkill(32, 1), "Tagged overflow was lost or the source displayed cap did not hide the increase.");
        edge.Tagged = false;
        Require(overflow.Menu.ChangeSkill(32, -1) && edge.Skills[32] == 100 && overflow.Menu.Assigned == 0 &&
            overflow.Menu.AllocatedDeltas[32] == 1, "A changed tag rewrote the source row delta or page counter.");
        var writes = edge.SkillWrites;
        overflow.Menu.Reset();
        Require(edge.Skills[32] == 99 && overflow.Menu.Assigned == 0 && edge.SkillWrites == writes + 2,
            "Reset skipped a retained row delta or the source zero-delta row write.");
    }

    private static void Rates()
    {
        var fnv = new FalloutLevelUpRules(PlayerAdvancementRateContracts.HalfFloor, 50, 2, 1, 10);
        var restored = new FalloutLevelUpRules(PlayerAdvancementRateContracts.HalfCeiling, 60, 1, 1, 8);
        Require(fnv.SkillPoints(5, 2) == 13 && fnv.SkillPoints(5, 3) == 12 && fnv.SkillPoints(6, 2) == 13 &&
            restored.SkillPoints(6, 2) == 11 && restored.SkillPoints(5, 3) == 11 &&
            restored.SkillPoints(99, 2) == 13 && restored.SkillPoints(-2, 2) == 9,
            "Skill rate, parity or permanent-intelligence bounds differ from the admitted source contracts.");
        Reject(() => (fnv with { SkillRate = fnv.SkillRate with { Divisor = 0 } }).SkillPoints(6, 2));
        Reject(() => (fnv with { LevelsPerPerk = 0 }).SkillPoints(6, 2));
    }

    private static void QueuedLevelsAndCold()
    {
        var fixture = new Fixture();
        var owner = fixture.Owner();
        fixture.Experience = 700; owner.EarnedExperience(0, 700);
        Require(owner.Pending && !owner.TryOpen(true) && fixture.Level == 1 && fixture.Presentations == 0,
            "Chargen exit was confused with synchronous level-up.");
        fixture.Ready = false;
        Require(!owner.TryOpen(false) && fixture.Level == 1, "An engine frame admission delay consumed a level.");
        fixture.Ready = true;
        Require(owner.TryOpen(false) && fixture.Level == 2 && owner.Menu!.Budget == 11 && fixture.Experience == 700 &&
            fixture.Presentations == 1 && owner.SaveBlocker == "player-level-up-menu", "One admitted update did not consume exactly one level.");
        var menu = owner.Menu!;
        Require(!menu.Continue() && !menu.ChangeSkill(32, -1), "Unspent points or unallocated subtraction was admitted.");
        for (var index = 0; index < 3; index++) Require(menu.ChangeSkill(32, 1), "Source skill point was refused.");
        Require(fixture.Skills[32] == 23 && menu.Assigned == 3 && !owner.TryOpen(false),
            "Skill clicks were delayed until submit or opened a second menu.");
        var snapshot = Cold(owner.Capture());
        var coldFixture = fixture.Clone(); var cold = coldFixture.Owner(snapshot);
        Require(coldFixture.Level == 2 && cold.Menu!.Assigned == 3 && coldFixture.Presentations == 0,
            "Cold validation replayed level consumption or presentation.");
        for (var index = 3; index < 11; index++) Require(cold.Menu!.ChangeSkill(32, 1), "Cold point allocation failed.");
        Require(cold.Menu!.Continue() && cold.Menu.Page == FalloutLevelUpPage.Perks && coldFixture.Rank == 0,
            "Perk page was skipped or selection effects were applied before completion.");
        Require(cold.Menu.SelectPerk(Fixture.Perk) && cold.Menu.Continue() && coldFixture.Rank == 1 && coldFixture.PerkWrites == 1,
            "Selected source perk did not acquire exactly one rank.");
        Require(cold.FinishMenu() && cold.Pending && cold.Menu is null && coldFixture.Level == 2,
            "Completing one menu opened another inline or lost queued XP.");
        var completed = Cold(cold.Capture()); var nextFixture = coldFixture.Clone(); var next = nextFixture.Owner(completed);
        Require(next.TryOpen(false) && nextFixture.Level == 3 && next.Menu!.Budget == 11 && nextFixture.PerkWrites == 1,
            "Queued cold level did not retain the first perk commit exactly once.");
        Require(next.Menu!.ChangeSkill(40, 1), "Source second skill row failed.");
        next.Menu.Reset(); Require(nextFixture.Skills[40] == 25 && next.Menu.Assigned == 0, "Reset restored unrelated earlier changes.");
    }

    private static void SkillAndResetPrefixes()
    {
        var fixture = new Fixture(); var owner = Open(fixture);
        fixture.SkillFailure = 32;
        Reject(() => owner.Menu!.ChangeSkill(32, 1));
        Require(fixture.Skills[32] == 21 && owner.Menu!.Assigned == 1 && owner.Error is not null,
            "A failed skill suffix discarded the consumed counter or base write.");
        var coldFixture = fixture.Clone(); var cold = coldFixture.Owner(Cold(owner.Capture()));
        Reject(() => cold.Menu!.ChangeSkill(32, 1));
        Require(coldFixture.SkillWrites == fixture.SkillWrites && coldFixture.Skills[32] == 21,
            "Cold failed skill allocation replayed its consumed prefix.");

        var resetFixture = new Fixture { Tagged = true, TaggedMultiplier = 2 }; var reset = Open(resetFixture);
        reset.Menu!.ChangeSkill(32, 1); reset.Menu.ChangeSkill(40, 1);
        resetFixture.Skills[32] += 5; // An independent shared-owner write is retained.
        resetFixture.Tagged = false;
        resetFixture.SkillFailure = 40;
        Reject(reset.Menu.Reset);
        Require(resetFixture.Skills[32] == 25 && resetFixture.Skills[40] == 25 && reset.Menu.Assigned == 0 && reset.Error is not null,
            "Reset guessed old bases/current tag multiplier or lost its source counter prefix.");
        var resetColdFixture = resetFixture.Clone(); var resetCold = resetColdFixture.Owner(Cold(reset.Capture()));
        Reject(resetCold.Menu!.Reset);
        Require(resetColdFixture.SkillWrites == resetFixture.SkillWrites, "Cold failed reset replayed row effects.");
    }

    private static void PerkFailureAndCold()
    {
        var fixture = new Fixture(); var owner = Open(fixture); Allocate(owner.Menu!);
        Require(owner.Menu!.Continue() && owner.Menu.SelectPerk(Fixture.Perk) && fixture.Rank == 0,
            "Perk selection mutated the acquired rank.");
        fixture.PerkFailure = true;
        Reject(() => owner.Menu.Continue());
        Require(fixture.Rank == 1 && fixture.PerkWrites == 1 && owner.Error is not null && !owner.Menu.Completed,
            "A source acquisition suffix failure cleared its prefix or completed the menu.");
        var clone = fixture.Clone(); var cold = clone.Owner(Cold(owner.Capture()));
        Reject(() => cold.Menu!.Continue()); Reject(() => cold.FinishMenu());
        Require(clone.PerkWrites == 1 && clone.Rank == 1, "A cold failed perk receipt was replayed or rolled back.");
        var changedRank = clone.Clone(); changedRank.Rank = 0;
        Reject(() => changedRank.Owner(Cold(owner.Capture())));

        var unavailable = new Fixture { PerkAvailable = false }; var disabled = Open(unavailable); Allocate(disabled.Menu!);
        Require(disabled.Menu!.Continue() && !disabled.Menu.SelectPerk(Fixture.Perk) && !disabled.Menu.Continue(),
            "Unavailable source perk was selected or silently waived.");
    }

    private static void SourceAndAdmissionFailures()
    {
        var fixture = new Fixture(); var owner = Open(fixture);
        var state = Cold(owner.Capture());
        Reject(() => fixture.Owner(state with { Source = state.Source with { StatsSha256 = new string('b', 64) } }));
        var changed = fixture.Clone(); changed.Skills[32] += 1;
        Reject(() => changed.Owner(state));
        Reject(() => fixture.Owner(state with { ObservedLevel = 1 }));
        Reject(() => fixture.Owner(state with { Menu = state.Menu! with { AssignedSkillPoints = 1 } }));

        var absent = new Fixture { AdmissionFailure = "Source UI/notification admission owner is absent." };
        var blocked = absent.Owner(); absent.Experience = 200; blocked.EarnedExperience(0, 200);
        Reject(() => blocked.TryOpen(false));
        Require(absent.Level == 1 && blocked.Pending && blocked.Error is not null, "Absent admission mutated player level.");
        var coldAbsent = absent.Clone().Owner(Cold(blocked.Capture())); Reject(() => coldAbsent.TryOpen(false));

        var levelPrefix = new Fixture { LevelFailure = true }; var failed = levelPrefix.Owner();
        levelPrefix.Experience = 200; failed.EarnedExperience(0, 200);
        Reject(() => failed.TryOpen(false));
        Require(levelPrefix.Level == 2 && failed.Error is not null && failed.Menu is null, "A failed level prefix was lost.");
        var clone = levelPrefix.Clone(); var restored = clone.Owner(Cold(failed.Capture()));
        Reject(() => restored.TryOpen(false));
        Require(clone.LevelWrites == 1 && clone.Level == 2, "Cold failed menu opening advanced the level again.");
    }

    private static void OrdinaryCallbackPrefixes()
    {
        var level = new Fixture { LevelFailure = true, OrdinaryFault = true }; var opening = level.Owner();
        level.Experience = 200; opening.EarnedExperience(0, 200);
        ExpectIo(() => opening.TryOpen(false)); Reject(() => opening.TryOpen(false));
        var openingState = Cold(opening.Capture());
        Require(openingState.Generation == 1 && openingState.OpeningLevel == 2 && openingState.Error is not null &&
            level.Level == 2 && level.LevelWrites == 1, "An ordinary callback fault lost or retried its committed level attempt.");
        var levelCold = level.Clone(); var coldOpening = levelCold.Owner(openingState);
        Reject(() => coldOpening.TryOpen(false));
        Require(levelCold.LevelWrites == 1, "Cold ordinary level failure replayed its source prefix.");

        var skill = new Fixture { OrdinaryFault = true }; var allocating = Open(skill); skill.SkillFailure = 32;
        ExpectIo(() => allocating.Menu!.ChangeSkill(32, 1));
        var skillState = Cold(allocating.Capture());
        Require(skillState.Menu!.Operation is { Kind: FalloutLevelUpOperationKind.SkillClick, Skill: 32 } &&
            allocating.Menu!.Assigned == 1 && skill.Skills[32] == 21 && skillState.Menu.Error is not null,
            "An ordinary skill callback fault discarded its exact counter/write attempt.");
        var skillCold = skill.Clone(); var coldAllocating = skillCold.Owner(skillState);
        Reject(() => coldAllocating.Menu!.ChangeSkill(32, 1));
        Require(skillCold.SkillWrites == skill.SkillWrites && skillCold.Skills[32] == 21,
            "Cold ordinary skill failure replayed or rolled back its committed allocation.");

        var reset = new Fixture { OrdinaryFault = true }; var resetting = Open(reset);
        resetting.Menu!.ChangeSkill(32, 1); resetting.Menu.ChangeSkill(40, 1); reset.SkillFailure = 40;
        ExpectIo(resetting.Menu.Reset);
        var resetState = Cold(resetting.Capture());
        Require(resetState.Menu!.Operation is { Kind: FalloutLevelUpOperationKind.SkillResetWrite, Skill: 40 } &&
            resetting.Menu.Assigned == 0 && reset.Skills[32] == 20 && reset.Skills[40] == 25 && resetState.Menu.Error is not null,
            "An ordinary reset callback fault discarded its committed counter/row prefix.");
        var resetCold = reset.Clone(); var coldResetting = resetCold.Owner(resetState);
        Reject(coldResetting.Menu!.Reset);
        Require(resetCold.SkillWrites == reset.SkillWrites, "Cold ordinary reset failure retried a source row.");

        var perk = new Fixture { OrdinaryFault = true }; var acquiring = Open(perk); Allocate(acquiring.Menu!);
        acquiring.Menu!.Continue(); acquiring.Menu.SelectPerk(Fixture.Perk); perk.PerkFailure = true;
        ExpectIo(() => acquiring.Menu.Continue());
        var perkState = Cold(acquiring.Capture());
        Require(perkState.Menu!.Operation is { Kind: FalloutLevelUpOperationKind.PerkAcquisition, Perk: { } selected } &&
            selected == Fixture.Perk && perkState.Menu.PerkAttempted && perkState.Menu.PerkObservedRank == 1 &&
            perk.Rank == 1 && perk.PerkWrites == 1 && perkState.Menu.Error is not null,
            "An ordinary perk callback fault lost its acquired-rank prefix or attempted operation.");
        var perkCold = perk.Clone(); var coldAcquiring = perkCold.Owner(perkState);
        Reject(() => coldAcquiring.Menu!.Continue());
        Require(perkCold.Rank == 1 && perkCold.PerkWrites == 1, "Cold ordinary perk failure replayed acquisition.");

        var empty = new Fixture { OrdinaryFault = true, EmptyMessage = true }; var blank = Open(empty); empty.SkillFailure = 32;
        ExpectIo(() => blank.Menu!.ChangeSkill(32, 1));
        var blankState = Cold(blank.Capture());
        var emptyCold = empty.Clone(); var coldBlank = emptyCold.Owner(blankState);
        Reject(() => coldBlank.Menu!.ChangeSkill(32, 1));
        Require(!string.IsNullOrWhiteSpace(blankState.Menu!.Error) && emptyCold.SkillWrites == empty.SkillWrites,
            "An empty ordinary exception message erased the failure or invalidated its cold prefix receipt.");
    }

    private static void ExpectIo(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new InvalidOperationException("Authored ordinary callback did not retain and rethrow its original IOException.");
    }

    private static FalloutPlayerAdvancement Open(Fixture fixture)
    {
        var owner = fixture.Owner(); fixture.Experience = 200; owner.EarnedExperience(0, 200);
        Require(owner.TryOpen(false), "Synthetic source player did not open its earned menu."); return owner;
    }
    private static void Allocate(FalloutLevelUpMenuSession menu)
    { for (var index = 0; index < menu.Budget; index++) Require(menu.ChangeSkill(32, 1), "Synthetic allocation failed."); }
    private static FalloutPlayerAdvancementSnapshot Cold(FalloutPlayerAdvancementSnapshot state) =>
        JsonSerializer.Deserialize<FalloutPlayerAdvancementSnapshot>(JsonSerializer.Serialize(state))!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or OverflowException) { return; }
        throw new InvalidOperationException("Unadmitted advancement state was accepted.");
    }

    private sealed class Fixture
    {
        internal static readonly FalloutFormKey Perk = new("Synthetic.esm", 0x801);
        private static readonly FalloutPlayerAdvancementSource Source = new(new("Synthetic.esm", 7), new string('a', 64),
            new("Synthetic.esm", 7), new string('c', 64), PlayerAdvancementRateContracts.Receipt(PlayerAdvancementRateContracts.HalfCeiling));
        internal int Level = 1, Experience, Rank, PerkWrites, SkillWrites, LevelWrites, Presentations, TaggedMultiplier = 1,
            BudgetAdjustment, BudgetLimit = int.MaxValue;
        internal Dictionary<int, float> Skills = new() { [32] = 20, [40] = 25 };
        internal bool Ready = true, PerkAvailable = true, Tagged, PerkFailure, LevelFailure, OrdinaryFault, EmptyMessage;
        internal int? SkillFailure;
        internal string? AdmissionFailure;
        internal FalloutPlayerAdvancement Owner(FalloutPlayerAdvancementSnapshot? restore = null) => new(Source,
            new(() => Level, () => Experience, level => checked((level - 1) * (200 + (level - 2) * 75)),
                level => { Level = level; LevelWrites++; if (LevelFailure) throw Failure("Level source suffix is unowned."); },
                () => 6, () => new(Source.SkillRate, 60, 1, TaggedMultiplier, 8),
                () => new(Ready, AdmissionFailure), _ => Menu(), _ => Presentations++), restore);
        private FalloutLevelUpMenuBinding Menu() => new([32, 40], value => Skills[value],
            (value, amount) => { Skills[value] = amount; SkillWrites++; if (SkillFailure == value) throw Failure("Skill source suffix is unowned."); },
            value => Math.Clamp(checked((int)Skills[value]), 0, 100), _ => Tagged,
            amount => Math.Min(checked(amount + BudgetAdjustment), BudgetLimit),
            [new(Perk, new string('e', 64), 3)], _ => PerkAvailable, _ => Rank,
            (_, rank) => { Rank = rank; PerkWrites++; if (PerkFailure) throw Failure("Perk source suffix is unowned."); });
        private Exception Failure(string message) => OrdinaryFault ? new IOException(EmptyMessage ? "" : message) : new NotSupportedException(message);
        internal Fixture Clone() => new()
        {
            Level = Level, Experience = Experience, Rank = Rank, PerkWrites = PerkWrites, SkillWrites = SkillWrites,
            LevelWrites = LevelWrites, Skills = new(Skills), TaggedMultiplier = TaggedMultiplier, Ready = Ready,
            BudgetAdjustment = BudgetAdjustment, BudgetLimit = BudgetLimit,
            PerkAvailable = PerkAvailable, Tagged = Tagged, PerkFailure = PerkFailure, LevelFailure = LevelFailure,
            SkillFailure = SkillFailure, AdmissionFailure = AdmissionFailure, OrdinaryFault = OrdinaryFault, EmptyMessage = EmptyMessage,
        };
    }
}
