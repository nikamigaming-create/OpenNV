using OpenNV.Runtime.Content;

internal static class OwnedDialogueVoiceProbe
{
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
                    .Where(condition => condition.RunOn == 0 && condition.Function == 72 && condition.Comparison == 1 && (condition.Flags & 0xe0) == 0)
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
