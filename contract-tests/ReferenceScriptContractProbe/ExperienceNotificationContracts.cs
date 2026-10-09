using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class ExperienceNotificationContracts
{
    internal static void Run()
    {
        ActualEventsAndRetirement(); ColdPhaseAndMissingCue(); PausedLevelClock();
        LostEventAndUnownedRetarget(); ClockLeaseAndEpoch(); SourceFrameProducers();
        Require(FalloutExperienceHudSource.Integer("+%i", 35, 16) == "+35", "Source integer formatting changed.");
        Reject(() => FalloutExperienceHudSource.Integer("%i", 100, 3));
        Console.WriteLine("OPENNV_EXPERIENCE_NOTIFICATION_CONTRACT_PASS actualXpEvent=true queuedDelta=true " +
            "drawRetirement=true pendingSoundGuard=true activeSoundSaveBlocker=true coldFreshReceipt=true " +
            "missingCueRefused=true queueHoleRefused=true pausedLevelClock=true newProcessEpoch=true " +
            "lostEventRefused=true activeRetargetUnknown=true nativePublication=unverified");
    }

    private static void ActualEventsAndRetirement()
    {
        using var fixture = new Fixture();
        using var owner = fixture.Owner(); var view = owner.AttachPresentation(fixture.Contract);
        Require(owner.Observe().State == FalloutExperienceNotificationFact.Held, "Registration fabricated an idle draw.");
        owner.PresentedIdle(view, owner.IdleRevision(view));
        Require(owner.IsSettled, "A genuine current empty-frame receipt did not settle the empty owner.");
        fixture.Experience.Reward(25); fixture.Experience.Reward(10);
        var queued = owner.Capture(new(0, 0));
        Require(queued.Pending.Count == 2 && queued.Pending[0].Previous == 0 && queued.Pending[1].Published == 35 &&
            !owner.IsSettled && queued.Display is null, "Actual XP publications were omitted or capture consumed them.");
        Reject(() => owner.PresentedIdle(view, 0));
        owner.Tick(new(10, 10), FalloutExperienceNotificationAdmission.Ready, false, true);
        var first = owner.Frame(view)!;
        Require(first.Amount == 35 && first.PointerFraction == 0 && first.Sequence == 1, "Queued actual deltas did not own the meter.");
        Reject(() => owner.Presented(first, new(10, 10)));
        var sound = owner.PendingSounds(view).Single(); owner.SoundStarted(view, sound);
        Require(owner.SaveBlocker == "experience-notification-active-sound", "A living voice was capturable as complete.");
        Reject(() => owner.Capture(new(10, 10)));
        owner.SoundFinished(view, sound.Sequence, sound.EditorId);
        owner.Presented(first, new(10, 10));
        long clock = 10;
        for (var index = 0; index < 3; ++index)
        {
            clock += 1000; owner.Tick(new(clock, clock), FalloutExperienceNotificationAdmission.Ready, false, true);
            var frame = owner.Frame(view)!;
            if (index == 2) Require(!owner.IsSettled && frame.Phase == FalloutExperienceNotificationPhase.AwaitingHiddenFrame,
                "A scheduled hide fabricated notification retirement.");
            owner.Presented(frame, new(clock, clock));
        }
        Require(owner.IsSettled && owner.Capture(new(clock, clock)).RetiredThrough == 2,
            "Actual hidden-frame receipt did not retire the exact publication prefix.");
        fixture.Experience.Reward(-5);
        Require(owner.IsSettled && owner.Capture(new(clock, clock)).RetiredThrough == 3,
            "The source's idle signed decrease created a fabricated positive notice.");
        owner.DetachPresentation(view);
        Require(owner.Observe().State == FalloutExperienceNotificationFact.Unowned, "Retiring a native view fabricated readiness.");
    }

    private static void ColdPhaseAndMissingCue()
    {
        using var fixture = new Fixture();
        using var owner = fixture.Owner(); var view = owner.AttachPresentation(fixture.Contract);
        fixture.Experience.Reward(35);
        owner.Tick(new(10, 10), FalloutExperienceNotificationAdmission.Ready, false, true);
        var unplayed = owner.Capture(new(10, 10));
        Require(unplayed.Display is { PhasePresented: false }, "Capture published an unseen phase.");
        Reject(() => fixture.Owner(unplayed with { Sounds = [] }));
        Reject(() => fixture.Owner(unplayed with { Contract = new string('c', 64) }));
        Reject(() => fixture.Owner(unplayed with { Display = null }));
        var cue = owner.PendingSounds(view).Single(); owner.SoundStarted(view, cue);
        owner.SoundFinished(view, cue.Sequence, cue.EditorId); owner.Presented(owner.Frame(view)!, new(10, 10));
        var retained = JsonSerializer.Deserialize<FalloutExperienceNotificationSnapshot>(JsonSerializer.Serialize(owner.Capture(new(60, 60))))!;
        using var cold = fixture.Owner(retained); var coldView = cold.AttachPresentation(fixture.Contract);
        Reject(() => cold.Presented(owner.Frame(view)!, new(60000, 60)));
        cold.Tick(new(60000, 60), FalloutExperienceNotificationAdmission.Ready, false, true);
        Require(cold.Frame(coldView)!.Phase == FalloutExperienceNotificationPhase.MeterFadeIn &&
            cold.Capture(new(60000, 60)).Display!.ElapsedMilliseconds == retained.Display!.ElapsedMilliseconds,
            "Process downtime burned or retired an unseen cold phase.");
        cold.Presented(cold.Frame(coldView)!, new(60000, 60));
        cold.Tick(new(61000, 1060), FalloutExperienceNotificationAdmission.Ready, false, true);
        Require(cold.Frame(coldView)!.Phase == FalloutExperienceNotificationPhase.MeterSweep,
            "Fresh cold receipt did not admit the retained source clock.");

        using var queuedFixture = new Fixture(); using var queuedOwner = queuedFixture.Owner();
        queuedOwner.AttachPresentation(queuedFixture.Contract); queuedFixture.Experience.Reward(5); queuedFixture.Experience.Reward(7);
        var queued = queuedOwner.Capture(new(0, 0));
        Reject(() => FalloutExperienceNotifications.ValidateSnapshot(queued with { Pending = [queued.Pending[1]] }));
        Reject(() => FalloutExperienceNotifications.ValidateSnapshot(queued with { Pending =
            [queued.Pending[0], queued.Pending[1] with { Previous = 4 }] }));
    }

    private static void PausedLevelClock()
    {
        using var fixture = new Fixture(); using var owner = fixture.Owner();
        var view = owner.AttachPresentation(fixture.Contract); fixture.Experience.Reward(200);
        long wall = 0, level = 0;
        owner.Tick(new(wall, level), FalloutExperienceNotificationAdmission.Ready, true, true); PresentWithSounds(owner, view, new(wall, level));
        for (var index = 0; index < 3; ++index)
        {
            wall += 1000; level += 1000;
            owner.Tick(new(wall, level), FalloutExperienceNotificationAdmission.Ready, true, true);
            PresentWithSounds(owner, view, new(wall, level));
        }
        Require(owner.Frame(view) is { Phase: FalloutExperienceNotificationPhase.LevelFadeIn, LevelTextStarted: true },
            "Actual threshold request did not join its distinct level notice.");
        wall += 201; level += 201; owner.Tick(new(wall, level), FalloutExperienceNotificationAdmission.Ready, true, true);
        owner.Presented(owner.Frame(view)!, new(wall, level));
        Require(owner.Frame(view)!.Phase == FalloutExperienceNotificationPhase.LevelHold, "Level fade did not use its source duration.");
        wall += 10000; owner.Tick(new(wall, level), FalloutExperienceNotificationAdmission.Held, true, true);
        owner.Tick(new(wall, level), FalloutExperienceNotificationAdmission.Ready, true, true);
        Require(owner.Frame(view)!.Phase == FalloutExperienceNotificationPhase.LevelHold,
            "Wall-clock fade time also advanced the paused level-text timer.");
        wall += 300; level += 300; owner.Tick(new(wall, level), FalloutExperienceNotificationAdmission.Ready, true, true);
        var fading = owner.Frame(view)!;
        Require(fading.Phase == FalloutExperienceNotificationPhase.LevelFadeOut && fading.LevelTimestamp == level + fixture.Source.LevelTextNextTimestampOffset,
            "The genuine level timer did not own strict hold expiry and source timestamp publication.");
        Reject(() => fixture.Owner(owner.Capture(new(wall, level)) with { Sounds =
            owner.Capture(new(wall, level)).Sounds.Where(sound => sound.EditorId != fixture.Source.LevelSound).ToArray() }));
    }

    private static void LostEventAndUnownedRetarget()
    {
        using var fixture = new Fixture(); using var owner = fixture.Owner();
        owner.AttachPresentation(fixture.Contract); fixture.Vitals.Publish(fixture.Vitals.State with { ExperiencePoints = 5 });
        Require(owner.Observe().State == FalloutExperienceNotificationFact.Unowned && owner.Failure is not null,
            "XP changed without its event and still advertised readiness.");
        using var active = new Fixture(); using var activeOwner = active.Owner(); activeOwner.AttachPresentation(active.Contract);
        active.Experience.Reward(5); activeOwner.Tick(new(0, 0), FalloutExperienceNotificationAdmission.Ready, false, true);
        Reject(() => active.Experience.Reward(1));
        Require(active.Vitals.State.ExperiencePoints == 6 && activeOwner.Failure is not null,
            "Unowned in-flight source retarget silently discarded or rolled back the actual gameplay publication.");
    }

    private static void ClockLeaseAndEpoch()
    {
        var clock = new FalloutExperienceUiClock(7); var owner = clock.Attach(100, paused: true);
        Require(clock.Read(500) == new FalloutExperienceClockReading(500, 7), "Initially paused level timer advanced.");
        clock.Pause(owner, 600, paused: false);
        Require(clock.Read(700) == new FalloutExperienceClockReading(700, 107), "Unpause lost its exact elapsed clock.");
        Reject(() => clock.Pause(Guid.NewGuid(), 800, paused: true));
        Reject(() => clock.Read(699));
        clock.Detach(owner); Reject(() => clock.Read(1000));
        var cold = new FalloutExperienceUiClock(107); var coldOwner = cold.Attach(50000, paused: false);
        Require(cold.Read(50001).LevelMilliseconds == 108, "New-process wall epoch added offline elapsed time.");
        cold.Detach(coldOwner);
    }

    private static void PresentWithSounds(FalloutExperienceNotifications owner, Guid view, FalloutExperienceClockReading clock)
    {
        // This is a pure owner contract. It exercises distinct explicit receipt
        // admissions; it is not native playback, a GPU frame or a parity claim.
        foreach (var cue in owner.PendingSounds(view)) { owner.SoundStarted(view, cue); owner.SoundFinished(view, cue.Sequence, cue.EditorId); }
        owner.Presented(owner.Frame(view)!, clock);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("An invalid XP notification owner/continuation was admitted.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("opennv-experience-notifications-");
        internal FalloutPluginStack Records { get; }
        internal FalloutPlayerVitals Vitals { get; }
        internal FalloutPlayerExperience Experience { get; }
        internal string Contract => new('b', 64);
        internal FalloutExperienceHudDeclaration Source { get; } = new(new('a', 64), new('d', 64),
            .3f, .7f, .4f, .2f, 500, 900, 31, 37, "+%i", "%i", "AuthoredOpacity", "AuthoredGain", "AuthoredLevel");
        internal int? CombatGroupTargets = 0;
        internal FalloutAdvancementFrameDeclaration FrameSource => new(Source.ExecutableSha256, new('e', 64), false, 1, false, 1008);
        internal Fixture()
        {
            var actor = new byte[24]; actor[8] = 1;
            File.WriteAllBytes(Path.Combine(_directory.FullName, "Notifications.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 7, Field("ACBS", actor), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5]), Field("RNAM", BitConverter.GetBytes(8u))),
                Record("RACE", 8),
                Setting(100, "fAVDHealthEnduranceMult", BitConverter.GetBytes(20f)), Setting(101, "fAVDHealthLevelMult", BitConverter.GetBytes(5f)),
                Setting(102, "fAVDActionPointsBase", BitConverter.GetBytes(65f)), Setting(103, "fAVDActionPointsMult", BitConverter.GetBytes(3f)),
                Setting(104, "iXPBase", BitConverter.GetBytes(200)), Setting(105, "iXPBumpBase", BitConverter.GetBytes(150)),
                Setting(106, "iMaxCharacterLevel", BitConverter.GetBytes(3)),
                Setting(107, "fAVDHealthEnduranceOffset", BitConverter.GetBytes(5f))));
            Records = FalloutPluginStack.Load(_directory.FullName, ["Notifications.esm"]);
            var values = new FalloutPlayerActorValues(Records);
            var skills = new FalloutPlayerSkills(Records, () => values.BaseSpecial, _ => false, () => [],
                null, new FalloutPlayerInventory(), new("Notifications.esm", 7), () => new("Notifications.esm", 8),
                () => false, actorValues: values);
            values.BindConstantModifiers(skills.Modifiers);
            Vitals = FalloutPlayerVitals.FromActorValues(Records, values);
            Experience = new(Records, Vitals, () => []); Experience.BindPerkConditions(_ => 1);
        }
        internal FalloutExperienceNotifications Owner(FalloutExperienceNotificationSnapshot? restore = null) =>
            new(Source, FrameSource, () => new(CombatGroupTargets, "authored-actual-group-count"), Contract,
                Experience, () => Vitals.State.Level, () => Vitals.State.ExperiencePoints,
                Vitals.ExperienceThreshold, () => unchecked((int)FalloutGameSettingIntegers.Read(Records, "iMaxCharacterLevel")), 123, restore);
        public void Dispose() { Records.Dispose(); _directory.Delete(true); }
        private static byte[] Setting(uint id, string name, byte[] data) => Record("GMST", id, Field("EDID", Encoding.ASCII.GetBytes(name + '\0')), Field("DATA", data));
        private static byte[] Record(string tag, uint id, params byte[][] fields)
        {
            var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(tag).CopyTo(result, 0);
            BitConverter.GetBytes((uint)data.Length).CopyTo(result, 4); BitConverter.GetBytes(id).CopyTo(result, 12); data.CopyTo(result, 24); return result;
        }
        private static byte[] Field(string tag, byte[] data)
        {
            var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(tag).CopyTo(result, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
        }
        private static byte[] Join(params byte[][] parts) => parts.SelectMany(value => value).ToArray();
    }
}
