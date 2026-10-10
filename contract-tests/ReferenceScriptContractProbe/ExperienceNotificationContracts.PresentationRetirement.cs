using OpenNV.Runtime.Gameplay.State;

internal static partial class ExperienceNotificationContracts
{
    private static void PresentationCancellation()
    {
        using var fixture = new Fixture();
        using var owner = fixture.Owner();
        var view = owner.AttachPresentation(fixture.Contract);
        fixture.Experience.Reward(35);
        var before = owner.Capture(new(0, 0));
        var lifetime = new FalloutExperiencePresentationLifetime();
        string? cancellation = null;
        var retirements = 0;
        var ticks = 0;
        void Retire() { retirements++; owner.DetachPresentation(view); }
        if (lifetime.BeginUpdate(() => cancellation, Retire))
        { owner.Tick(new(0, 0), FalloutExperienceNotificationAdmission.Held, false, true); ticks++; }
        cancellation = "authored actual campaign failure";
        for (var update = 0; update < 2; update++)
            if (lifetime.BeginUpdate(() => cancellation, Retire))
            { owner.Tick(new(1, 1), FalloutExperienceNotificationAdmission.Unowned, false, true); ticks++; }
        lifetime.Retire("actual session teardown", Retire);
        Require(owner.SaveBlocker == "experience-notification-owner" && !owner.IsSettled &&
            owner.Observe().State == FalloutExperienceNotificationFact.Unowned,
            "Detaching an actual view fabricated native ownership or advancement readiness.");
        Reject(() => owner.Capture(new(0, 0)));
        // Inspect the retained source state only after a genuinely new authored
        // presentation lease. This emits no Presented/frame/sound completion.
        var rebound = owner.AttachPresentation(fixture.Contract);
        var after = owner.Capture(new(0, 0));
        Require(ticks == 1 && retirements == 1 && lifetime.RetirementEntered && lifetime.RetirementReturned &&
            lifetime.Cancellation == cancellation && lifetime.Failure is null,
            "Retired native update was replayed or its real cancellation prefix was lost.");
        Require(after.Pending.SequenceEqual(before.Pending) && after.LastOrdinal == before.LastOrdinal &&
            after.RetiredThrough == before.RetiredThrough && after.Display == before.Display &&
            after.Sounds.SequenceEqual(before.Sounds) && after.SoundRandomState == before.SoundRandomState &&
            owner.Failure is null && !owner.IsSettled,
            "Native cancellation fabricated an XP/frame/sound completion or discarded genuine pending state.");
        owner.DetachPresentation(rebound);
        using var cold = fixture.Owner(after);
        var coldView = cold.AttachPresentation(fixture.Contract);
        var restored = cold.Capture(new(0, 0));
        Require(restored.Pending.SequenceEqual(before.Pending) && restored.LastOrdinal == before.LastOrdinal &&
            restored.RetiredThrough == before.RetiredThrough && restored.SoundRandomState == before.SoundRandomState &&
            !cold.IsSettled && coldView != view, "Cold presentation invented notification completion or replayed its source queue.");
        cold.DetachPresentation(coldView);

        using var soundFixture = new Fixture();
        using var soundOwner = soundFixture.Owner();
        var soundView = soundOwner.AttachPresentation(soundFixture.Contract);
        soundFixture.Experience.Reward(5);
        soundOwner.Tick(new(0, 0), FalloutExperienceNotificationAdmission.Ready, false, true);
        var sound = soundOwner.PendingSounds(soundView).Single();
        soundOwner.SoundStarted(soundView, sound);
        new FalloutExperiencePresentationLifetime().Retire("actual native voice cancellation",
            () => soundOwner.DetachPresentation(soundView));
        var nextSoundView = soundOwner.AttachPresentation(soundFixture.Contract);
        Require(soundOwner.SaveBlocker == "experience-notification-active-sound" && !soundOwner.IsSettled,
            "Cancellation fabricated the finite sound's missing native Finished receipt.");
        Reject(() => soundOwner.Capture(new(0, 0)));
        soundOwner.DetachPresentation(nextSoundView);

        using var unknownFixture = new Fixture();
        using var unknown = unknownFixture.Owner();
        unknown.AttachPresentation(unknownFixture.Contract);
        Reject(() => unknown.Tick(new(0, 0), FalloutExperienceNotificationAdmission.Unowned, false, true));
        Require(unknown.Failure is not null, "A genuine absent surface was silently treated as cancellation.");

        var fault = new IOException("authored failure after actual view detach");
        var failed = new FalloutExperiencePresentationLifetime();
        var calls = 0;
        void FailingRetire() { calls++; throw fault; }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { failed.Retire("actual cancellation", FailingRetire); throw new InvalidOperationException("Faulted retirement was accepted."); }
            catch (IOException observed) when (ReferenceEquals(observed, fault)) { }
        }
        Require(calls == 1 && failed.RetirementEntered && !failed.RetirementReturned && failed.Failure is not null,
            "Unknown retirement exception lost its committed prefix or replayed cleanup.");

        var getterFault = new IOException("authored actual cancellation reader failure");
        var unread = new FalloutExperiencePresentationLifetime();
        var reads = 0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                unread.BeginUpdate(() => { reads++; throw getterFault; }, () => throw new InvalidOperationException("Reader failure fabricated cancellation."));
                throw new InvalidOperationException("An unowned cancellation observation was accepted.");
            }
            catch (IOException observed) when (ReferenceEquals(observed, getterFault)) { }
        }
        var cleanup = 0;
        unread.Retire("actual later native teardown", () => cleanup++);
        Require(reads == 1 && cleanup == 1 && unread.RetirementReturned && unread.Failure is not null,
            "Unknown callback failure was retried, replaced or prevented genuine later cleanup.");
        Console.WriteLine("OPENNV_EXPERIENCE_PRESENTATION_RETIREMENT_PASS actualViewDetach=true staleTick=false pendingPreserved=true coldFreshView=true activeSoundRefused=true completionFabricated=false missingSurfaceRefused=true retirementPrefix=true nativeExecution=false");
    }
}
