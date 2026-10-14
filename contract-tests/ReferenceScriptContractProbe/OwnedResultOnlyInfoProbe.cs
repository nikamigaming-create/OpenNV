using OpenNV.Runtime.Content;

internal static class OwnedResultOnlyInfoProbe
{
    // Parser/capability denominator only. This does not select an INFO, execute
    // a result, create a speaker, change quests or prove ordinary gameplay.
    internal static void Run(string root, string campaign, IReadOnlyList<string>? plugins = null,
        IReadOnlyList<string>? additionalContentRoots = null)
    {
        using var content = RuntimeLiveContentSource.Open(root, campaign, additionalContentRoots,
            activePlugins: plugins is { Count: > 0 } ? plugins : null);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var all = records.EffectiveRecords("INFO");
        var empty = 0; var supported = 0; var compiled = 0; var diagnostic = 0; var absent = 0;
        var scripted = 0; var unsupported = new Dictionary<string, int>(StringComparer.Ordinal);
        var types = new Dictionary<byte, int>();
        foreach (var record in all)
        {
            var info = FalloutDialogueTopic.Decode(record);
            if (info.Responses.Count != 0) continue;
            empty++; types[info.Type] = types.GetValueOrDefault(info.Type) + 1;
            if (info.Type is not (0 or 1)) continue;
            try { FalloutDialogueTopic.RequireFlags(info, conversation: info.Type == 0,
                randomSelection: true, immediateResults: true, allowResultOnly: true); supported++; }
            catch (NotSupportedException)
            {
                var flags = $"type={info.Type};next={info.NextSpeaker};flags={info.Flags};flags2={info.Flags2}";
                unsupported[flags] = unsupported.GetValueOrDefault(flags) + 1;
                continue;
            }
            if (info.Type == 1) scripted++;
            foreach (var begin in new[] { true, false })
            {
                var scope = FalloutScriptScope.Dialogue(record, begin);
                if (scope.Compiled) compiled++;
                else if (FalloutScriptResultReceipt.HasProgram(scope)) diagnostic++;
                else absent++;
            }
        }
        if (supported == 0) throw new InvalidDataException("Selected owned load order has no admitted zero-response player INFO corpus case.");
        Console.WriteLine("OPENNV_OWNED_RESULT_ONLY_INFO_CORPUS_PASS " + System.Text.Json.JsonSerializer.Serialize(new
        {
            campaign, selectedStack = content.StackId, pluginCount = content.PluginSources.Count,
            infoRecords = all.Count, zeroResponse = empty, byType = types,
            admittedHeader = supported, type1NeedsPlayerTopicIngress = scripted, compiledScopes = compiled,
            diagnosticScopes = diagnostic, absentScopes = absent, unsupportedHeaders = unsupported,
            eligibility = "unexecuted", resultPrograms = "unexecuted", ordinaryInput = "unexecuted",
            nativeSaveContinue = "unexecuted", campaignQuestProgress = "unexecuted"
        }));
    }
}
