using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class LevelUpMenuPublicationContracts
{
    internal static void Run()
    {
        ColdPublication(); PresentationFailure(); PresentationFailure(ordinary: true); ChangedEligibility(); ChangedRank(); CompletedRetirementFailure();
        Console.WriteLine("OPENNV_LEVEL_UP_PUBLICATION_CONTRACT_PASS cold=true admitted=true once=true sourceFault=true staleEligibility=true staleRank=true noReplay=true nativeUi=unexecuted");
    }
    private static void ColdPublication()
    {
        var fixture = new Fixture(); var warm = fixture.Open();
        Require(fixture.LevelWrites == 1 && fixture.Presentations == 1 && !warm.PublishPendingMenu(), "Warm publication replayed a request.");
        warm.Menu!.ChangeSkill(32, 1);
        var snapshot = Cold(warm.Capture()); var coldFixture = fixture.Clone(); var cold = coldFixture.Owner(snapshot);
        coldFixture.Ready = false;
        Require(!cold.PublishPendingMenu() && coldFixture.Presentations == 0 && coldFixture.LevelWrites == 1 &&
            cold.Menu!.Assigned == 1, "Cold presentation bypassed activity admission or consumed progress.");
        coldFixture.Ready = true;
        Require(cold.PublishPendingMenu() && !cold.PublishPendingMenu() && coldFixture.Presentations == 1 &&
            coldFixture.LevelWrites == 1 && coldFixture.SkillWrites == 1 && coldFixture.RankWrites == 0 && cold.Menu!.Generation == snapshot.Generation,
            "Cold publication replayed level, budget, skill or perk operations.");
    }
    private static void PresentationFailure(bool ordinary = false)
    {
        var fixture = new Fixture { PresentationFault = true, OrdinaryFault = ordinary }; var owner = fixture.Owner();
        fixture.Xp = 200; owner.EarnedExperience(0, 200);
        Reject(() => owner.TryOpen(false));
        Require(owner.Error is not null && owner.Menu is not null && fixture.Level == 2 && fixture.LevelWrites == 1 && fixture.Presentations == 1,
            "A failed native publication lost its consumed level or request prefix.");
        var coldFixture = fixture.Clone(); var cold = coldFixture.Owner(Cold(owner.Capture()));
        Reject(() => cold.PublishPendingMenu()); Reject(() => cold.TryOpen(false));
        Require(coldFixture.LevelWrites == 1 && coldFixture.Presentations == 0 && coldFixture.RankWrites == 0,
            "Cold publication retried a failed source request.");
    }
    private static void ChangedEligibility()
    {
        var fixture = new Fixture(); var owner = fixture.Open(); var menu = owner.Menu!;
        Allocate(menu); menu.Continue(); Require(menu.SelectPerk(Fixture.Perk), "Authored eligible perk could not be selected.");
        fixture.Eligible = false;
        Require(!menu.CanContinue && !menu.Continue() && !menu.Completed && fixture.RankWrites == 0 &&
            !menu.Capture().CompletionPrepared,
            "A prior selection fabricated acceptance after live conditions changed.");
        var coldFixture = fixture.Clone(); var cold = coldFixture.Owner(Cold(owner.Capture()));
        Require(!cold.Menu!.CanContinue && !cold.Menu.Continue() && coldFixture.RankWrites == 0,
            "Cold selected-perk state bypassed current eligibility.");
        coldFixture.Eligible = true;
        Require(cold.Menu.Continue() && coldFixture.Rank == 1 && coldFixture.RankWrites == 1,
            "A genuine eligible rank could not finish its retained allocation.");
    }
    private static void ChangedRank()
    {
        var fixture = new Fixture(); var owner = fixture.Open(); var menu = owner.Menu!;
        Allocate(menu); menu.Continue(); menu.SelectPerk(Fixture.Perk);
        fixture.Rank = 3; // A distinct authoritative owner acquired the remaining ranks.
        Require(!menu.CanContinue && !menu.Continue() && fixture.RankWrites == 0 && !menu.Completed,
            "A stale perk choice exceeded its original rank declaration.");
        var state = menu.PerkState(Fixture.Perk);
        Require(state.Rank == 3 && !state.Enabled && state.Selected, "Read-only menu rank state rewrote selection or acquired rank.");
    }
    private static void CompletedRetirementFailure()
    {
        var fixture = new Fixture(); var owner = fixture.Open(); var session = owner.Menu!;
        Allocate(session); session.Continue(); session.SelectPerk(Fixture.Perk); session.Continue();
        Require(session.Completed && owner.FinishMenu() && owner.Menu is null && owner.OwnsNativeMenuReceipt(session),
            "Actual completed native retirement lost its original process-local session receipt.");
        owner.RetainNativeMenuFailure(session, new IOException("Authored input release failed after completion."));
        var state = Cold(owner.Capture()); var coldFixture = fixture.Clone(); var cold = coldFixture.Owner(state);
        Require(state.Error is not null && state.Menu is null && !cold.OwnsNativeMenuReceipt(session),
            "Cold progression erased the post-completion fault or fabricated a retired native object receipt.");
        Reject(() => cold.TryOpen(false)); Reject(() => cold.PublishPendingMenu());
        Require(coldFixture.LevelWrites == 1 && coldFixture.RankWrites == 1 && coldFixture.Presentations == 0,
            "A completed retirement fault replayed advancement, acquisition or native publication.");
    }

    private static void Allocate(FalloutLevelUpMenuSession menu)
    { while (menu.Assigned < menu.Budget) Require(menu.ChangeSkill(32, 1), "Authored skill allocation was refused."); }
    private static FalloutPlayerAdvancementSnapshot Cold(FalloutPlayerAdvancementSnapshot value) =>
        JsonSerializer.Deserialize<FalloutPlayerAdvancementSnapshot>(JsonSerializer.Serialize(value))!;
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or IOException) { return; }
        throw new InvalidOperationException("An unsupported menu request was accepted.");
    }
    private sealed class Fixture
    {
        internal static readonly FalloutFormKey Perk = new("Authored.esm", 0x900);
        private static readonly FalloutPlayerAdvancementSource Source = new(new("Authored.esm", 7), new string('a', 64),
            new("Authored.esm", 7), new string('b', 64), PlayerAdvancementRateContracts.Receipt(PlayerAdvancementRateContracts.HalfCeiling));
        internal int Level = 1, Xp, Rank, LevelWrites, SkillWrites, RankWrites, Presentations, Completions;
        internal float Skill = 20;
        internal bool Ready = true, Eligible = true, PresentationFault, OrdinaryFault;
        internal FalloutPlayerAdvancement Owner(FalloutPlayerAdvancementSnapshot? restore = null) => new(Source,
            new(() => Level, () => Xp, level => (level - 1) * 200, value => { Level = value; LevelWrites++; },
                () => 6, () => new(Source.SkillRate, 60, 1, 1, 8), () => new(Ready),
                gainedLevel => new([32], slot => Skill, (slot, value) => { Skill = value; SkillWrites++; },
                    slot => Math.Clamp((int)Skill, 0, 100), slot => false, value => value,
                    [new(Perk, new string('d', 64), 3)], perk => Eligible, perk => Rank,
                    (_, value) => { Rank = value; RankWrites++; Completions++; }),
                _ => { Presentations++; if (PresentationFault) throw OrdinaryFault ? new IOException("Authored publication callback fault.") :
                    new NotSupportedException("Authored source publication fault."); }), restore);
        internal FalloutPlayerAdvancement Open()
        {
            var owner = Owner(); Xp = 200; owner.EarnedExperience(0, Xp);
            Require(owner.TryOpen(false), "Authored earned menu was not opened."); return owner;
        }
        internal Fixture Clone() => new()
        {
            Level = Level, Xp = Xp, Rank = Rank, LevelWrites = LevelWrites, SkillWrites = SkillWrites,
            RankWrites = RankWrites, Completions = Completions, Skill = Skill, Ready = Ready, Eligible = Eligible,
            PresentationFault = PresentationFault, OrdinaryFault = OrdinaryFault,
        };
    }
}
