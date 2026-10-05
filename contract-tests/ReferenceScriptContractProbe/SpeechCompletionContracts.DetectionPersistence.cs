using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SpeechCompletionContracts
{
    private const string DetectionSource = Locals + "begin GameMode\nif talking == 1\nset talking to 2\n" +
        "set completions to completions + 1\nset resultPrefix to GetRandomPercent\nSayTo Speaker Topic 1\n" +
        "CreateDetectionEvent Speaker 50\nset action to GetActionRef\nendif\nend\n" +
        "begin SayToDone Other\nset completions to 9999\nend\n" +
        "begin SayToDone Topic\nset talking to 0\nset completions to completions + 10\nend\n" +
        "begin SayToDone\nset completions to completions + 100\nend";

    private static T JsonCopy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private static void DetectionPersistence(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var actor = world.Get(Key(0x930)); actor.Write(1, 1);
        var effects = new List<FalloutReferenceScriptEffect>();
        var scripts = Scripts(records, world, effects);
        var placement = new FalloutReferencePlacement(Key(0x800), [123, -456, 789], [0, 0, 0]);
        world.AdvanceDetection(7.25f);
        world.Detection.Bind(_ => placement.Copy(), null);
        var failed = scripts.Dispatch(actor.Reference, "GameMode", elapsedSeconds: .25);
        var frame = actor.ScriptStoppedFrame;
        Require(failed.Error is not null && frame is { PreparedDetection: not null } && frame.Event == "GameMode" &&
            frame.ElapsedSeconds == .25 && frame.PreparedDetection.ObservedSeconds == 7.25f && actor.Read(2) == 1 &&
            effects.Count == 1 && effects[0].Kind == FalloutReferenceEffectKind.SayTo,
            "Source failure did not retain its original prepared request after consuming the prefix.");
        var random = JsonSerializer.Serialize(world.ScriptValues.Capture());
        var selectedRandom = actor.Read(5);
        var completion = Receipt(actor.Reference);
        var identity = FalloutFinishedSpeechSourceBinding.Capture(records, actor, completion);
        var audioEnds = 0; var results = 0; var settled = 0;
        var owner = new FalloutSpeechCompletionEvents();
        Reject(() => owner.CompleteFinished(completion, identity.Source, () =>
        {
            ++audioEnds; ++results;
            scripts.ExecuteResult(FalloutDialogueTopic.Decode(records.GetEffective(Key(0x600))), actor.Reference, false);
        }, pending => Deliver(scripts, pending), () => ++settled));
        Require(owner.HasCapturableFinishedFailure && audioEnds == 1 && results == 1 && settled == 0 &&
            actor.Read(1) == 2 && actor.Read(2) == 1 && world.Get(Key(0x900)).Read(5) == 1,
            "Finished speech lost its committed audio/result boundary or entered SayToDone before GameMode recovery.");
        var failureSnapshot = JsonCopy(owner.CaptureFinishedFailure());
        var references = RoundTrip(world.Capture()); var detection = JsonCopy(world.CaptureDetection()!);
        var scriptValues = JsonCopy(world.ScriptValues.Capture());
        world.UnloadCell(Key(0x800)); scripts.UnloadCell(Key(0x800));
        using var cold = new FalloutReferenceWorld(records); cold.ScriptValues.Restore(scriptValues);
        cold.Restore(references); cold.RestoreDetection(detection);
        var coldScripts = Scripts(records, cold, effects);
        // Current pose and calendar may differ. The retained command uses the
        // original request and simulation timestamp, never a fresh observation.
        cold.Detection.Bind(_ => new(Key(0x801), [999, 999, 999], [0, 0, 0]), _ => FalloutDetectionProcessLevel.High);
        var coldOwner = new FalloutSpeechCompletionEvents();
        coldOwner.RestoreFinishedFailure(failureSnapshot, FalloutFinishedSpeechSourceBinding.Speakers(records),
            retained => FalloutFinishedSpeechSourceBinding.Require(records, cold.Retained(retained.Speaker), retained));
        Require(coldScripts.CanResumeSpeechCompletion(completion), "Cold stopped detection suffix was not admitted by its original source frame.");
        coldOwner.ResumeFinishedFailure(pending =>
        {
            var result = coldScripts.ResumeSpeechCompletion(pending);
            if (result.Error is not null) throw new NotSupportedException(result.Error);
        }, _ => ++settled);
        var resumed = cold.Retained(actor.Reference); var created = cold.Detection.Capture().Events.Single();
        Require(!coldOwner.Active && coldOwner.Error is null && resumed.ScriptError is null && resumed.ScriptStoppedFrame is null &&
            resumed.CompletedScriptContinuation?.Error == frame!.Error && resumed.Read(1) == 0 && resumed.Read(2) == 111 &&
            resumed.Read(5) == selectedRandom && cold.Retained(Key(0x900)).Read(5) == 1 &&
            random == JsonSerializer.Serialize(cold.ScriptValues.Capture()) && effects.Count == 1 && audioEnds == 1 && results == 1 && settled == 1 &&
            created.Request.Position.SequenceEqual(placement.Position) && created.CreatedSeconds == 7.25f && created.Request.SoundLevel == 50 &&
            created.Request.RequestedType == 3 && !cold.IsResident(actor.Reference),
            "Cold recovery replayed a prefix, changed RNG/pose/clock or dropped a matching SayToDone block.");
        Reject(() => coldOwner.ResumeFinishedFailure(_ => ++results));
        Reject(() => coldOwner.CompleteFinished(completion, identity.Source, () => ++audioEnds, _ => ++results));
        coldOwner.CompleteFinished(new(actor.Reference, completion.Topics, completion.Info, 2), identity.Source,
            () => ++audioEnds, _ => ++results);
        Require(audioEnds == 2 && results == 2, "Cold completed generation blocked an actual newer voice.");
        Reject(() => cold.Detection.RequireConsumer(created.Request.Owner));

        // Source/drift/malformed rejections are atomic; a source body hash alone
        // cannot admit another INFO or fabricated original position/cursor.
        foreach (var invalid in new[]
        {
            failureSnapshot with { Receipt = failureSnapshot.Receipt with { Generation = 0 } },
            failureSnapshot with { Receipt = failureSnapshot.Receipt with { Info = Key(0x602) } },
            failureSnapshot with { Receipt = failureSnapshot.Receipt with { Source = identity.Source with { InfoSha256 = new('0', 64) } } },
            failureSnapshot with { CompletedVoices = [new(actor.Reference, completion.Generation)] },
            failureSnapshot with { CompletedVoices = [new(Key(0x999), 1)] }
        })
        {
            var rejected = new FalloutSpeechCompletionEvents();
            Reject(() => rejected.RestoreFinishedFailure(invalid, FalloutFinishedSpeechSourceBinding.Speakers(records),
                retained => FalloutFinishedSpeechSourceBinding.Require(records, cold.Retained(retained.Speaker), retained)));
            Require(!rejected.Active && rejected.Error is null, "Rejected source receipt partially committed restoration.");
        }
        foreach (var invalid in new[]
        {
            references.Select(value => value.Reference == actor.Reference ? value with { ScriptStoppedFrame = frame! with { Statement = int.MaxValue } } : value).ToArray(),
            references.Select(value => value.Reference == actor.Reference ? value with { ScriptStoppedFrame = frame! with { SourceSha256 = new('0', 64) } } : value).ToArray(),
            references.Select(value => value.Reference == actor.Reference ? value with { ScriptStoppedFrame = frame! with {
                PreparedDetection = frame.PreparedDetection! with { Position = [float.NaN, 0, 0] } } } : value).ToArray()
        })
        {
            using var rejected = new FalloutReferenceWorld(records); Reject(() => rejected.Restore(invalid));
            Require(rejected.InstanceCount == 0, "Invalid invocation partially restored a world.");
        }
        using var legacy = new FalloutReferenceWorld(records);
        legacy.Restore(references.Select(value => value with { ScriptStoppedFrame = null }).ToArray());
        legacy.RestoreDetection(detection);
        legacy.Detection.Bind(_ => placement.Copy(), _ => FalloutDetectionProcessLevel.High);
        var legacyScripts = Scripts(records, legacy, []);
        Require(!legacyScripts.CanResumeSpeechCompletion(completion), "Legacy failure manufactured a source invocation.");
        Reject(() => legacyScripts.ResumeSpeechCompletion(completion));

        var effectsFailure = new FalloutSpeechCompletionEvents();
        Reject(() => effectsFailure.CompleteFinished(completion, identity.Source,
            () => throw new NotSupportedException("Incomplete INFO end results."), _ => ++results));
        Require(!effectsFailure.HasCapturableFinishedFailure, "Unfinished result prefix was promoted to ended speech.");
        Reject(() => effectsFailure.CaptureFinishedFailure());
        var notifyFailure = new FalloutSpeechCompletionEvents();
        Reject(() => notifyFailure.CompleteFinished(completion, identity.Source, () => ++audioEnds, _ => ++results,
            () => throw new NotSupportedException("Opaque settled callback stopped.")));
        Require(!notifyFailure.HasCapturableFinishedFailure && !notifyFailure.Active,
            "Settled notification recreated a replayable source receipt.");
        Reject(() => notifyFailure.Complete(completion, _ => ++results));
        var queued = new FalloutSpeechCompletionEvents(); queued.Mark(Key(0x900), Key(0x200));
        Reject(() => queued.CompleteFinished(completion, identity.Source, () => ++audioEnds,
            _ => throw new NotSupportedException("Ended failure with pending opaque sibling.")));
        Require(!queued.HasCapturableFinishedFailure, "Pending sibling completion was silently dropped for saving.");
        Console.WriteLine("OPENNV_DETECTION_SPEECH_CONTINUATION_CONTRACT_PASS sourceCursor=true originalRequest=true " +
            "audioResultsRandomOnce=true matchingBlocks=true offCellCold=true sourceDriftAtomic=true legacyUnretainedRefused=true consumerUnbound=true");
    }

    private static void DetectionEventStateContracts()
    {
        var actor = Key(0x900); var location = Key(0x930); var cell = Key(0x800);
        FalloutDetectionEvents Owner() => new(10, 1, key => key == actor, key => key == location, key => key == cell);
        var owner = Owner(); owner.Bind(_ => new(cell, [1, 2, 3], [0, 0, 0]), null); owner.Advance(1);
        var request = owner.Prepare(actor, location, int.MinValue, int.MaxValue);
        Reject(() => owner.Create(request, "CreateDetectionEvent", 3));
        foreach (var level in Enum.GetValues<FalloutDetectionProcessLevel>().Where(value => value != FalloutDetectionProcessLevel.High))
            Require(owner.Create(request, level) is null && owner.Capture().Events.Count == 0, "Proven lower process allocated an event.");
        owner.Create(request, FalloutDetectionProcessLevel.High); var initial = owner.Capture();
        owner.Create(request with { SoundLevel = int.MaxValue, RequestedType = -100 }, FalloutDetectionProcessLevel.High);
        Require(owner.Capture().Events.Count == 1 && owner.Capture().Events[0].Request.SoundLevel == int.MaxValue &&
            owner.Capture().Events[0].InitialAuxiliaryValue == -1, "High process did not replace its source event.");
        var saved = JsonCopy(owner.Capture()); var cold = Owner(); cold.Restore(saved);
        Require(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(cold.Capture()), "Cold source event changed clock or requested type.");
        cold.Advance(10); Require(cold.Capture().Events.Count == 1, "Source event expired at equality instead of strict age.");
        cold.Advance(.001f); Require(cold.Capture().Events.Count == 0 && cold.Capture().Revision == saved.Revision,
            "Source event did not expire without falsely recording consumer success.");
        var lifetime = 10f;
        var live = new FalloutDetectionEvents(() => lifetime, 1, key => key == actor, key => key == location, key => key == cell);
        live.Bind(_ => new(cell, [1, 2, 3], [0, 0, 0]), _ => FalloutDetectionProcessLevel.High);
        live.Create(live.Prepare(actor, location, 50, 3), "CreateDetectionEvent", 2);
        live.Advance(9); lifetime = 9;
        live.Advance(0); Require(live.Capture().Events.Count == 1, "Changed live lifetime lost strict equality admission.");
        lifetime = 8; live.Advance(0);
        Require(live.Capture().Events.Count == 0, "Existing detection event retained a stale copied lifetime.");
        var beforeInvalid = JsonSerializer.Serialize(live.Capture()); lifetime = float.NaN;
        Reject(() => live.Advance(1));
        Require(JsonSerializer.Serialize(live.Capture()) == beforeInvalid, "Invalid live lifetime partially advanced source state.");
        foreach (var invalid in new[]
        {
            initial with { SimulationSeconds = float.NaN }, initial with { Revision = 0 },
            initial with { Events = [initial.Events[0], initial.Events[0]] },
            initial with { Events = [initial.Events[0] with { CreatedSeconds = 999 }] },
            initial with { Events = [initial.Events[0] with { InitialAuxiliaryValue = 0 }] },
            initial with { Events = [initial.Events[0] with { Request = request with { Owner = Key(0x999) } }] }
        })
        { var rejected = Owner(); Reject(() => rejected.Restore(invalid)); Require(rejected.Capture().Revision == 0, "Malformed event partially committed."); }
        Console.WriteLine("OPENNV_DETECTION_EVENT_STATE_CONTRACT_PASS highReplacement=true signedArguments=true requestedTypeProvenance=true " +
            "lowerProcessNoAllocation=true unknownRefused=true strictExpiry=true liveLifetime=true floatClockCold=true receiverBoundary=true");
    }

    private static void DetectionLifetimeContracts(FalloutPluginStack records)
    {
        const string setting = "fDetectionEventExpireTime";
        var original = records.NumericSettings.Float(setting);
        using var world = new FalloutReferenceWorld(records);
        var actor = Key(0x930); var placement = world.Placement(actor);
        world.Detection.Bind(_ => placement.Copy(), _ => FalloutDetectionProcessLevel.High);
        world.Detection.Create(world.Detection.Prepare(actor, actor, 50, 3), "CreateDetectionEvent", 2);
        try
        {
            world.AdvanceDetection(3);
            Require(world.CaptureDetection()!.Events.Count == 1 && records.NumericSettings.Set(setting, 2),
                "Live numeric lifetime override was refused or the original event expired early.");
            world.AdvanceDetection(0);
            Require(world.CaptureDetection()!.Events.Count == 0 && world.CaptureDetection()!.SimulationSeconds == 3,
                "Shared detection expiry did not observe a source numeric-setting change.");
        }
        finally { _ = records.NumericSettings.Set(setting, original); }
        Console.WriteLine("OPENNV_DETECTION_LIFETIME_CONTRACT_PASS sharedNumericSetting=true existingEventRefresh=true simulationClockUnchanged=true");
    }
}
