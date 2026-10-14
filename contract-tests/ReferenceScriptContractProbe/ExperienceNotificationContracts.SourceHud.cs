using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class ExperienceNotificationContracts
{
    internal static void VerifySourceHudPublication(FalloutExperienceSourceHud source)
    {
        using var fixture = new Fixture();
        using var owner = SourceHudOwner(fixture, source);
        owner.BindSourceHud(source);
        var before = owner.Capture(new(0, 0));
        var beforeSource = before.SourceHud ?? throw new InvalidDataException("Source HUD omitted its actual constructor publication.");
        Require(owner.SaveBlocker is null && !owner.IsSettled && !before.IdlePresented && !before.FrameGate.Ready &&
            before.FrameGate.Flags == FalloutExperienceSourceFlags.Meter && beforeSource.Factory == source.Factory && beforeSource.FrameRefusal is null,
            "Source constructor ownership fabricated an idle draw, frame-ready flag or renderer dependency.");
        var view = owner.AttachPresentation(fixture.Contract);
        Require(!owner.CurrentIdleSubmission(view, owner.IdleRevision(view)), "A renderer claimed an unowned source idle update.");
        Reject(() => owner.PresentedIdle(view, owner.IdleRevision(view)));
        Reject(() => owner.SoundStarted(view, new(1, fixture.Source.GainSound, FalloutExperienceSoundState.Pending)));
        Require(!owner.IsSettled && owner.Failure is null, "Legacy rendering/audio callbacks fabricated current source completion.");
        owner.DetachPresentation(view);
        var after = owner.Capture(new(0, 0));
        Require(JsonSerializer.Serialize(after) == JsonSerializer.Serialize(before) &&
            owner.Observe().State == FalloutExperienceNotificationFact.Held && owner.SaveBlocker is null,
            "Retiring a graphics view discarded the actual XP source constructor or invented source completion.");
        fixture.Experience.Reward(5);
        Require(owner.SaveBlocker == FalloutExperienceSourceHud.FrameBoundary && owner.Failure is null,
            "A real pending XP award bypassed its missing source UI/gradual frame owner.");
        Reject(() => owner.Tick(new(10, 10), FalloutExperienceNotificationAdmission.Ready, false, true));
        Require(owner.Failure is null && owner.SaveBlocker == FalloutExperienceSourceHud.FrameBoundary && !owner.IsSettled,
            "An unbound source UI frame consumed the award or fabricated a rendered completion.");
    }

    internal static void VerifySourceHudRetiredHistory(FalloutExperienceSourceHud warmSource, FalloutExperienceSourceHud coldSource)
    {
        FalloutExperienceNotificationSnapshot retained;
        using (var warm = new Fixture())
        {
            warm.Experience.Reward(5);
            using var owner = SourceHudOwner(warm, warmSource);
            owner.BindSourceHud(warmSource);
            Require(warm.Experience.Reward(-1) == -1, "Authored signed XP publication did not reach its actual event owner.");
            retained = JsonSerializer.Deserialize<FalloutExperienceNotificationSnapshot>(JsonSerializer.Serialize(owner.Capture(new(0, 0)))) ??
                throw new InvalidDataException("Retired XP event omitted its actual cold state.");
            Require(retained.LastOrdinal == 1 && retained.RetiredThrough == 1 && retained.ObservedExperience == 4 &&
                retained.Pending.Count == 0 && retained.Display is null && owner.SaveBlocker is null,
                "A real idle XP reduction invented a pending meter or lost its retired original event.");
        }
        using (var cold = new Fixture())
        {
            cold.Experience.Reward(retained.ObservedExperience);
            using var owner = SourceHudOwner(cold, coldSource, retained);
            Reject(() => owner.BindSourceHud(warmSource));
            owner.BindSourceHud(coldSource);
            var current = owner.Capture(new(0, 0));
            Require(current.LastOrdinal == retained.LastOrdinal && current.RetiredThrough == retained.RetiredThrough &&
                current.ObservedExperience == retained.ObservedExperience && current.SourceHud?.Factory == coldSource.Factory &&
                current.FrameGate == retained.FrameGate && !current.IdlePresented && !owner.IsSettled && owner.SaveBlocker is null,
                "Cold XP publication rejected genuine retired history, replayed its event or fabricated a source frame.");
            Reject(() => owner.BindSourceHud(coldSource));
        }
        using (var currentEvent = new Fixture())
        {
            currentEvent.Experience.Reward(retained.ObservedExperience);
            using var owner = SourceHudOwner(currentEvent, coldSource, retained);
            currentEvent.Experience.Reward(-1);
            Reject(() => owner.BindSourceHud(coldSource));
            Require(!owner.HasSourceHud && owner.Failure is null,
                "A new current XP event was mistaken for immutable historical publication.");
        }
        Reject(() => FalloutExperienceNotifications.ValidateSnapshot(retained with { IdlePresented = true }));
        Console.WriteLine("OPENNV_EXPERIENCE_SOURCE_COLD_HISTORY_PASS actualSignedEvent=true freshOwner=true " +
            "historyNotReplayed=true constructorFlagUnchanged=true newCurrentEventRefused=true legacyFrameRefused=true gameplay=UNEXECUTED");
    }

    internal static void VerifyEmptySourceFrameRefusal(FalloutExperienceSourceHud source)
    {
        using var fixture = new Fixture(); using var owner = SourceHudOwner(fixture, source);
        owner.BindSourceHud(source);
        var before = owner.Capture(new(0, 0));
        var beforeSource = before.SourceHud ?? throw new InvalidDataException("Empty source frame omitted its actual constructor publication.");
        Require(source.Capture().FrameRefusal is null && owner.SaveBlocker is null,
            "Renderer absence invented a reached source-frame failure.");
        Reject(() => owner.Tick(new(9, 8), FalloutExperienceNotificationAdmission.Held, true, false));
        var after = source.Capture();
        Require(owner.Failure is null && owner.SaveBlocker == FalloutExperienceSourceHud.FrameBoundary &&
            after.FrameRefusal is { LevelRequestPending: true, CharacterGenerationEnded: false,
                Admission: FalloutExperienceNotificationAdmission.Held, DeliveredClock.TilesMilliseconds: 9, DeliveredClock.LevelMilliseconds: 8 } &&
            JsonSerializer.Serialize(after.Tiles) == JsonSerializer.Serialize(beforeSource.Tiles) &&
            after.LocalTimerConstructor == beforeSource.LocalTimerConstructor &&
            owner.Observe().State == FalloutExperienceNotificationFact.Held,
            "Reached empty source-frame failure did not block saving or changed source time, flags or tile state.");
        Reject(() => owner.Capture(new(9, 8)));
    }

    private static FalloutExperienceNotifications SourceHudOwner(Fixture fixture, FalloutExperienceSourceHud source,
        FalloutExperienceNotificationSnapshot? saved = null) => new(fixture.Source with { ExecutableSha256 = source.Engine },
        fixture.FrameSource with { ExecutableSha256 = source.Engine }, () => new(0, "authored-actual-group-count"),
        fixture.Contract, fixture.Experience, () => fixture.Vitals.State.Level, () => fixture.Vitals.State.ExperiencePoints,
        fixture.Vitals.ExperienceThreshold, () => unchecked((int)FalloutGameSettingIntegers.Read(fixture.Records, "iMaxCharacterLevel")), 123, saved);
}
