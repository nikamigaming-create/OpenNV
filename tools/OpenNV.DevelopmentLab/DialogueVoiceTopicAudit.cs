using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

// Source and resource inventory only: selection, timing and dialogue effects
// still require live state and a separate retail behavior contract.
internal static class DialogueVoiceTopicAudit
{
    internal static int Run(FalloutPluginStack records, RuntimeLiveContentSource content, string topicId, string actorId)
    {
        FalloutFormKey Key(string value) => records.RuntimeFormKey(uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        var topic = FalloutDialogueTopic.Read(records, Key(topicId));
        var speaker = FalloutDialogueSpeaker.Read(records, Key(actorId));
        var index = new FalloutDialogueVoiceIndex(content.ResourcePathsUnder("sound/voice"));
        var rows = new List<object>();
        var failures = 0;
        foreach (var info in topic.Infos)
        {
            for (var i = 0; i < info.Responses.Count; i++)
            {
                var response = info.Responses[i];
                FalloutDialogueVoiceBinding? binding = null;
                string? error = null;
                try { binding = index.Resolve(speaker, info, i); }
                catch (Exception failure) when (failure is InvalidDataException or NotSupportedException)
                { error = failure.Message; failures++; }
                rows.Add(new
                {
                    info = info.Record.FormKey.ToString(), info.Record.Plugin.Name, info.Flags, info.Flags2,
                    info.NextSpeaker, conditions = info.Conditions.Count, response = response.Number,
                    textSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response.Text))),
                    textLength = response.Text.Length, explicitSound = response.Sound?.ToString(),
                    audio = binding?.AudioPath, error,
                    eligibility = "uninspected", execution = "uninspected"
                });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-dialogue-voice-topic-audit/v1", topic = topic.Topic.FormKey.ToString(),
            actor = speaker.Actor.ToString(), voice = speaker.VoiceType.ToString(),
            infos = topic.Infos.Count, responses = rows.Count, failures, rows
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
