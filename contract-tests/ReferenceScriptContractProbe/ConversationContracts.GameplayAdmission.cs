using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class ConversationContracts
{
    private static void GameplayAdmission(FalloutPluginStack records)
    {
        using var healthy = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var effects = new List<(FalloutFormKey Info, bool Begin)>();
        var said = new HashSet<FalloutFormKey>();
        FalloutConversation Create(FalloutReferenceWorld world, HashSet<FalloutFormKey>? sourceSaid = null) =>
            new(records, quests, condition => throw new InvalidDataException("Admission fixture queried an unauthored condition."),
                (info, begin) => effects.Add((info.Record.FormKey, begin)), sourceSaid,
                requireGameplayAdmission: world.RequireNoFunctionalFailure);

        // Run Immediately has already executed End. No later result callback
        // remains to notice a shared failure before AddedTopics/choices mutate.
        var control = Create(healthy); control.Start(Key(0x900), Key(0x81a));
        var line = control.EnterResponse(); control.CompleteResponse(line);
        Require(control.Phase == "choices" && control.AddedTopics.SequenceEqual([Key(0x802)]) &&
            control.Choices.Any(choice => choice.Topic == Key(0x802)) &&
            effects.SequenceEqual([(Key(0x9a6), true), (Key(0x9a6), false)]),
            "Healthy admitted immediate dialogue changed its exactly-once results or source AddedTopics.");
        effects.Clear();
        using var stopped = new FalloutReferenceWorld(records);
        var late = Create(stopped); late.Start(Key(0x900), Key(0x81a));
        var completedVoice = late.EnterResponse(); var info = late.Info; var generation = late.ResponseGeneration;
        var committedResults = effects.ToArray();
        Stop(stopped);
        Reject(() => late.CompleteResponse(completedVoice));
        Require(late.Phase == "failed" && late.Info == info && late.ResponseIndex == 0 && late.ResponseGeneration == generation &&
            late.PendingResponse == completedVoice && late.AddedTopics.Count == 0 && late.Choices.Count == 0 &&
            late.BeginResultsEntered && late.EndResultsEntered && effects.SequenceEqual(committedResults) &&
            late.Error!.Contains("Authoritative gameplay stopped at authored-Main", StringComparison.Ordinal),
            "Faulted audio completion consumed its publication, added topics, advanced choices or repeated immediate results.");
        Reject(() => late.CompleteResponse(completedVoice)); Reject(() => late.CaptureContinuation());
        Require(late.PendingResponse == completedVoice && effects.SequenceEqual(committedResults),
            "Repeated blocked completion discarded the real stopped voice prefix or replayed dialogue effects.");

        using var choosingWorld = new FalloutReferenceWorld(records);
        var choosing = Create(choosingWorld, said);
        choosing.Start(Key(0x900), Key(0x81c)); choosing.CompleteResponse();
        var offered = choosing.Choices.ToArray(); var previousInfo = choosing.Info; var beforeChoice = effects.Count;
        Require(offered.Single().Topic == Key(0x81b) && !said.Contains(Key(0x9a7)),
            "Offering a SayOnce response consumed its source state before selection.");
        Stop(choosingWorld);
        Reject(() => choosing.Choose(Key(0x81b)));
        Require(choosing.Phase == "failed" && choosing.Info == previousInfo && choosing.Choices.SequenceEqual(offered) &&
            !said.Contains(Key(0x9a7)) && effects.Count == beforeChoice && choosing.Response is null,
            "Faulted choice replaced its INFO/offered rows, recorded SayOnce or executed Begin.");
        var fresh = Create(choosingWorld, said); var speakerBefore = fresh.SpeakerBase;
        Reject(() => fresh.Start(Key(0x900), Key(0x81b)));
        Require(fresh.Phase == "failed" && fresh.SpeakerBase == speakerBefore && fresh.Info is null &&
            fresh.ResponseGeneration == 0 && !said.Contains(Key(0x9a7)) && effects.Count == beforeChoice,
            "Blocked Start stored its speaker outside the owned mutation or consumed a SayOnce result.");

        var chosen = Create(healthy, said); chosen.Start(Key(0x900), Key(0x81c)); chosen.CompleteResponse();
        chosen.Choose(Key(0x81b));
        Require(chosen.Phase == "speaking" && chosen.Info!.Record.FormKey == Key(0x9a7) && said.Contains(Key(0x9a7)),
            "Healthy admitted choice lost actual SayOnce/Begin ownership.");
        chosen.CompleteResponse(); Require(chosen.Phase == "closed", "Healthy admitted goodbye did not return.");

        using var voiceWorld = new FalloutReferenceWorld(records);
        var unentered = Create(voiceWorld); unentered.Start(Key(0x900), Key(0x81b)); Stop(voiceWorld);
        Reject(() => unentered.EnterResponse());
        Require(unentered.PendingResponse is null && unentered.ResponseIndex == 0 && unentered.ResponseGeneration == 1,
            "A blocked voice entry published an entered source audio lease.");
        var savedClosed = new FalloutConversationSnapshot(FalloutConversationSnapshot.ExpectedSchema, "closed", 5,
            null, null, null, 0, [Key(0x802)], [], null, null);
        var cold = Create(voiceWorld);
        Reject(() => cold.RestoreContinuation(savedClosed, Key(0x900)));
        Require(cold.ResponseGeneration == 0 && cold.AddedTopics.Count == 0 && cold.SpeakerBase == default,
            "Cold restoration mutated a stopped gameplay owner before admission.");
        Console.WriteLine("OPENNV_CONVERSATION_GAMEPLAY_ADMISSION_PASS actualSharedFault=true immediateAddedTopicBlocked=true " +
            "stoppedResponseRetained=true choiceSayOnceBlocked=true speakerStoreGuarded=true voiceEntryBlocked=true " +
            "coldRestoreBlocked=true healthyResultsUnchanged=true nativeAudioFinished=SEPARATE_OWNER");

        static void Stop(FalloutReferenceWorld world) =>
            world.RetainFunctionalFailure(new InvalidOperationException("authored-frame-child-failure"), "authored-Main");
    }
}
