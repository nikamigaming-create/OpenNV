using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class ExperienceNotificationContracts
{
    private static void SourceFrameProducers()
    {
        InitialAndCompletedFrames(); ActualMenuResetAndNextLevel(); SourceCountAndColdFrame();
        InterfaceActivationFrameContracts.Run();
        Console.WriteLine("OPENNV_ADVANCEMENT_FRAME_PRODUCER_CONTRACT_PASS originalInitialFlag=true " +
            "emptyDrawDoesNotComplete=true levelTextHiddenReceipt=true actualMenuSubmitReset=true " +
            "levelOnlyContinuation=true independentNoticeSequence=true coldFreshFrame=true " +
            "combatGroupCountDistinct=true missingCountRefused=true nativeExecution=unverified");
    }

    private static void InitialAndCompletedFrames()
    {
        using var fixture = new Fixture(); using var owner = fixture.Owner();
        Require(owner.ObserveOriginalUiFrameGate().State == FalloutExperienceNotificationFact.Unowned,
            "Source construction fabricated its live original HUD getter.");
        var view = owner.AttachPresentation(fixture.Contract);
        owner.PresentedIdle(view, owner.IdleRevision(view));
        Require(owner.IsSettled && owner.ObserveOriginalUiFrameGate().State == FalloutExperienceNotificationFact.Held &&
            !owner.Capture(new(0, 0)).FrameGate.Ready,
            "Empty queue or actual empty draw changed the source's initial false frame flag.");
        fixture.Experience.Reward(35); long clock = 0;
        FinishDisplay(owner, view, ref clock, pending: false);
        Require(owner.IsSettled && !owner.Capture(new(clock, clock)).FrameGate.Ready,
            "A meter without level text fabricated advancement completion.");

        using var threshold = new Fixture(); using var due = threshold.Owner();
        var dueView = due.AttachPresentation(threshold.Contract); threshold.Experience.Reward(200);
        long dueClock = 0; FinishDisplay(due, dueView, ref dueClock, pending: true);
        var completed = due.Capture(new(dueClock, dueClock));
        Require(completed.FrameGate is { Ready: true, Flags: FalloutExperienceSourceFlags.Completed,
                CompletedLevel: 2, CompletedSequence: 1 } &&
            due.ObserveOriginalUiFrameGate().State == FalloutExperienceNotificationFact.Satisfied,
            "Actual level-text hidden receipt did not produce the original ready flag.");
        Reject(() => FalloutExperienceNotifications.ValidateSnapshot(completed with
        { FrameGate = completed.FrameGate with { CompletedSequence = 2 } }));
        Reject(() => FalloutExperienceNotifications.ValidateSnapshot(completed with
        { FrameGate = completed.FrameGate with { Flags = FalloutExperienceSourceFlags.Meter } }));
    }

    private static void ActualMenuResetAndNextLevel()
    {
        using var fixture = new Fixture(); using var owner = fixture.Owner();
        var view = owner.AttachPresentation(fixture.Contract); fixture.Experience.Reward(700);
        long clock = 0; FinishDisplay(owner, view, ref clock, pending: true);
        var completed = owner.Capture(new(clock, clock));
        var retainedGate = new FalloutExperienceFrameGate(fixture.FrameSource, completed.FrameGate);
        var unsubmitted = Menu(2);
        Reject(() => retainedGate.CompletedMenuSubmitted(unsubmitted));
        var foreignLevel = Menu(3); Require(foreignLevel.Continue(), "Authored foreign menu did not consume its own submit.");
        Reject(() => retainedGate.CompletedMenuSubmitted(foreignLevel));
        var foreignSource = Menu(2, new('9', 64)); Require(foreignSource.Continue(), "Foreign source did not consume its own submit.");
        Reject(() => retainedGate.CompletedMenuSubmitted(foreignSource));
        var menu = Menu(2); Require(menu.Continue() && menu.Completed, "Actual authored menu submit was not consumed.");
        fixture.Vitals.AdvancePlayerLevel(2);
        owner.CompletedLevelMenuSubmitted(menu);
        var reset = owner.Capture(new(clock, clock));
        Require(reset.FrameGate is { Ready: false, Flags: FalloutExperienceSourceFlags.Meter, ResetGeneration: 1,
                CompletedSequence: null, CompletedLevel: null } && reset.LastOrdinal == 1 && reset.Pending.Count == 0,
            "Completed source menu did not clear its original flag, or invented another XP event.");
        clock += 1; owner.Tick(new(clock, clock), FalloutExperienceNotificationAdmission.Ready, true, true);
        var next = owner.Capture(new(clock, clock));
        Require(next.Display is { Sequence: 2, Amount: 0, Level: 2, MeterPublished: false, LevelTextPublished: true,
                Phase: FalloutExperienceNotificationPhase.LevelFadeIn } &&
            next.LastOrdinal == 1 && next.Display.FirstOrdinal == 1 && next.Display.LastOrdinal == 1 &&
            owner.PendingSounds(view).Single().EditorId == fixture.Source.LevelSound,
            "A retained second level was dropped, replayed an XP award, or published a synthetic gain meter.");
        FinishDisplay(owner, view, ref clock, pending: true);
        Require(owner.Capture(new(clock, clock)).FrameGate is { Ready: true, CompletedLevel: 3, CompletedSequence: 2 },
            "Second original level text did not retain independent sequence and actual requested level.");
    }

    private static void SourceCountAndColdFrame()
    {
        using var blocked = new Fixture { CombatGroupTargets = 1 }; using var held = blocked.Owner();
        var heldView = held.AttachPresentation(blocked.Contract); blocked.Experience.Reward(200);
        long clock = 0;
        for (var step = 0; step < 5; ++step)
        {
            held.Tick(new(clock, clock), FalloutExperienceNotificationAdmission.Ready, true, true);
            PresentWithSounds(held, heldView, new(clock, clock)); clock += 1000;
        }
        Require(held.Frame(heldView) is { Phase: FalloutExperienceNotificationPhase.MeterFadeOut, LevelTextStarted: false } &&
            held.Capture(new(clock, clock)).FrameGate.Flags == FalloutExperienceSourceFlags.WaitingMeter,
            "A nonempty actual source target group published level text.");
        blocked.CombatGroupTargets = null;
        Reject(() => held.Tick(new(clock, clock), FalloutExperienceNotificationAdmission.Ready, true, true));
        Require(held.Failure is not null && held.ObserveOriginalUiFrameGate().State == FalloutExperienceNotificationFact.Unowned,
            "Missing source target count was replaced by public IsInCombat or an invented zero.");

        using var fixture = new Fixture(); using var owner = fixture.Owner();
        var view = owner.AttachPresentation(fixture.Contract); fixture.Experience.Reward(200);
        long sourceClock = 0; FinishDisplay(owner, view, ref sourceClock, pending: true);
        var state = JsonSerializer.Deserialize<FalloutExperienceNotificationSnapshot>(
            JsonSerializer.Serialize(owner.Capture(new(sourceClock, sourceClock))))!;
        using var cold = fixture.Owner(state);
        Require(cold.ObserveOriginalUiFrameGate().State == FalloutExperienceNotificationFact.Unowned,
            "A cold saved flag fabricated a native presentation lease.");
        var coldView = cold.AttachPresentation(fixture.Contract);
        Require(cold.ObserveOriginalUiFrameGate().State == FalloutExperienceNotificationFact.Held,
            "Native registration bypassed the cold current-frame receipt.");
        cold.PresentedIdle(coldView, cold.IdleRevision(coldView));
        Require(cold.ObserveOriginalUiFrameGate().State == FalloutExperienceNotificationFact.Satisfied,
            "A fresh current empty draw lost the persisted actual source completion.");
        Reject(() => fixture.Owner(state with { FrameGate = state.FrameGate with { Contract = new('f', 64) } }));
    }

    private static void FinishDisplay(FalloutExperienceNotifications owner, Guid view, ref long clock, bool pending)
    {
        for (var step = 0; step < 20; ++step)
        {
            owner.Tick(new(clock, clock), FalloutExperienceNotificationAdmission.Ready, pending, true);
            if (owner.Frame(view) is null) { Require(owner.IsSettled, "Absent display was not actually retired."); return; }
            PresentWithSounds(owner, view, new(clock, clock));
            if (owner.Frame(view) is null) return;
            clock += 1000;
        }
        throw new InvalidDataException("Authored source phases did not retire through their explicit receipt operations.");
    }

    private static FalloutLevelUpMenuSession Menu(int level, string? engine = null)
    {
        var rate = PlayerAdvancementRateContracts.HalfFloor;
        var receipt = PlayerAdvancementRateContracts.Receipt(rate) with { EngineSha256 = engine ?? new('a', 64) };
        var source = new FalloutPlayerAdvancementSource(new("Notifications.esm", 7), new('1', 64),
            new("Notifications.esm", 7), new('2', 64), receipt);
        float skill = 20;
        return new(source, 1, level, 0, new(rate, 3, 1, 1, 10),
            new([32], _ => skill, (_, value) => skill = value, _ => checked((int)skill), _ => false,
                budget => budget, [], _ => false, _ => 0, (_, _) =>
                    throw new InvalidOperationException("A menu without perks acquired an invented rank.")), () => { });
    }
}
