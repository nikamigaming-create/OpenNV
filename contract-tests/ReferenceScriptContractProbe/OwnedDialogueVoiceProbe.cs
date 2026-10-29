using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class OwnedDialogueVoiceProbe
{
    internal static void Unrecorded(string root, string savePath)
    {
        var saveBytes = File.ReadAllBytes(savePath);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(saveBytes) ??
            throw new InvalidDataException("Owned speech checkpoint is absent.");
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(root), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var reference = records.RuntimeFormKey(0x1629a7);
        var actor = (saved.References ?? throw new InvalidDataException("Owned speech references are absent."))
            .Single(snapshot => snapshot.Reference == reference);
        var templates = new FalloutActorTemplateSelection(actor.Templates ??
            throw new InvalidDataException("Owned speech retained template selection is absent."));
        var speaker = FalloutDialogueSpeaker.Read(records, actor.Base, templates);
        if (speaker.TraitsOwner != records.RuntimeFormKey(0x101c8d) || speaker.VoiceType != records.RuntimeFormKey(0x2d))
            throw new InvalidDataException("Owned speech differs from its reached retained actor/voice source.");
        var conditionOwner = records.GetEffective(records.RuntimeFormKey(0x175766));
        var voiceCondition = FalloutCondition.Read(conditionOwner).First(condition =>
            condition.Kind == Function.GetIsVoiceType && condition.FormArgument1 == speaker.VoiceType);
        var shared = new FalloutConditionRuntime(records, new(records))
        { Identity = subject => subject == reference ? speaker : null };
        var dialogue = new FalloutDialogueConditions(records, new(records), reference, speaker);
        if (shared.Evaluate(voiceCondition, new(reference)) != 1 || dialogue.Evaluate(voiceCondition) != 1 ||
            shared.Evaluate(voiceCondition with { RunOn = 1 }, new(records.RuntimeFormKey(0x14), reference)) != 1)
            throw new InvalidDataException("Owned source voice predicate lost retained traits or shared subject context.");
        var record = records.GetEffective(records.RuntimeFormKey(0x17af3d));
        var info = FalloutDialogueTopic.Decode(record);
        var index = new FalloutDialogueVoiceIndex(content.ResourcePathsUnder("sound/voice"));
        if (info.Responses.Count != 1 || index.ResolveOrUnrecorded(speaker, info, 0) is not null)
            throw new InvalidDataException("Owned unrecorded response no longer has its absent-audio/absent-LIP disposition.");
        var clock = new FalloutUnrecordedSpeech(info.Responses[0].Text,
            FalloutGameSettingFloats.Read(records, "fAIDefaultSpeechMult"));
        var sourceText = record.ReadSubrecords().Single(field => field.Signature == "NAM1").Data;
        if (clock.TextBytes != sourceText.Length - 1 || clock.DurationSeconds <= 0 ||
            clock.Advance(clock.DurationSeconds + 1) || !clock.Advance(0))
            throw new InvalidDataException("Owned unrecorded response changed its source byte count or next-tick completion.");
        if (!SHA256.HashData(File.ReadAllBytes(savePath)).SequenceEqual(SHA256.HashData(saveBytes)))
            throw new InvalidDataException("Read-only speech audit changed its checkpoint.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "OPENNV_OWNED_UNRECORDED_SPEECH_PASS", reference = reference.ToString(),
            actor = speaker.Actor.ToString(), traits = speaker.TraitsOwner.ToString(),
            voice = speaker.VoiceType.ToString(), info = record.FormKey.ToString(), winner = record.Plugin.Name,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())),
            voicePredicate = conditionOwner.FormKey.ToString(), sharedVoiceContexts = true,
            checkpointSha256 = Convert.ToHexString(SHA256.HashData(saveBytes)),
            timing = clock.State, boundary = "source-and-clock-only; ordinary speech effects and matched retail timing remain unverified"
        }));
    }

    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var index = new FalloutDialogueVoiceIndex(content.ResourcePathsUnder("sound/voice"));
        var speakers = new Dictionary<FalloutFormKey, FalloutDialogueSpeaker>();
        var gaps = new Dictionary<string, int>(StringComparer.Ordinal);
        var bound = 0; var overridden = 0; var decoded = 0; var scoped = 0;
        foreach (var record in records.EffectiveRecords("INFO"))
        {
            try
            {
                var info = FalloutDialogueTopic.Decode(record); decoded++;
                var targets = info.Speaker is { } specified ? new[] { specified } : info.Conditions.Select(bytes => FalloutCondition.Read(record, bytes))
                    .Where(condition => condition.RunOn == 0 && condition.Kind == Function.GetIsID && condition.Comparison == 1 && (condition.Flags & 0xe0) == 0)
                    .Select(condition => condition.FormArgument1).Distinct().ToArray();
                // This audits declared actor/line bindings, independently of
                // live condition eligibility, quest execution or final audio.
                foreach (var target in targets)
                {
                    scoped++;
                    if (!speakers.TryGetValue(target, out var speaker)) speakers.Add(target, speaker = FalloutDialogueSpeaker.Read(records, target));
                    for (var response = 0; response < info.Responses.Count; response++)
                    {
                        try
                        {
                            var voice = index.Resolve(speaker, info, response);
                            if (!content.TryRead(voice.AudioPath, null, out var audio, out _) || audio.Length < 4)
                                throw new InvalidDataException("Voice resource is absent or empty.");
                            if (!content.TryRead(voice.LipPath, null, out var lip, out _) || lip.Length < 12)
                                throw new InvalidDataException("Exact voice LIP resource is absent or empty.");
                            bound++; if (!record.Plugin.Name.Equals(record.FormKey.OwnerPlugin, StringComparison.OrdinalIgnoreCase)) overridden++;
                        }
                        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { Gap(error); }
                    }
                }
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException or InvalidOperationException) { Gap(error); }
        }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { kind = "OPENNV_OWNED_DIALOGUE_VOICES", decodedInfos = decoded,
            declaredActorBindings = scoped, distinctSpeakers = speakers.Count, voiceAndLipResources = bound, overriddenInfosWithOriginalVoices = overridden,
            gaps = gaps.OrderByDescending(pair => pair.Value).Take(12), unresolved = gaps.Values.Sum(),
            eligibility = "not-evaluated", audioPlayback = "not-measured", parity = "unmeasured" }));
        if (bound == 0) throw new InvalidOperationException("No owned speaker/response binding succeeded.");
        void Gap(Exception error)
        {
            var category = error.Message.Contains("exact voice candidates", StringComparison.Ordinal) ? "missing-or-ambiguous-exact-voice" :
                error.Message.Contains("SOUN response owner", StringComparison.Ordinal) ? "explicit-SOUN-response" : error.Message;
            gaps[category] = gaps.GetValueOrDefault(category) + 1;
        }
    }
}
