using System.Buffers.Binary;
using System.Globalization;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutDialogueSpeaker(FalloutFormKey Actor, FalloutFormKey TraitsOwner,
    FalloutFormKey VoiceType, string VoiceName, FalloutFormKey? Race, bool Female)
{
    internal static FalloutDialogueSpeaker Read(FalloutPluginStack records, FalloutFormKey actor)
    {
        var record = records.GetEffective(actor);
        var traits = FalloutAiPackages.TemplateOwner(records, record, 1);
        var voice = records.GetEffective(FalloutDialogueTopic.RequiredForm(traits, "VTCK"));
        if (voice.Signature != "VTYP") throw new InvalidDataException($"Actor {actor} voice is not VTYP.");
        var name = FalloutDialogueTopic.Text(voice.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span);
        if (name.Length == 0 || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
            throw new InvalidDataException("Actor voice type has an invalid resource directory.");
        var acbs = traits.ReadSubrecords().Single(field => field.Signature == "ACBS").Data;
        return new(actor, traits.FormKey, voice.FormKey, name,
            record.Signature == "NPC_" ? FalloutDialogueTopic.RequiredForm(traits, "RNAM") : null,
            record.Signature == "NPC_" && (BinaryPrimitives.ReadUInt32LittleEndian(acbs.Span) & 1) != 0);
    }

    internal static bool AllowsPlayerDialogue(FalloutPluginStack records, FalloutFormKey actor)
    {
        var record = records.GetEffective(actor);
        if (record.Signature == "NPC_") return true;
        if (record.Signature != "CREA") return false;
        var owner = FalloutAiPackages.TemplateOwner(records, record, 128);
        var data = owner.ReadSubrecords().Single(field => field.Signature == "ACBS").Data;
        return (BinaryPrimitives.ReadUInt32LittleEndian(data.Span) & (1u << 21)) != 0;
    }
}

internal sealed record FalloutDialogueVoiceBinding(FalloutFormKey Actor, FalloutFormKey TraitsOwner,
    FalloutFormKey VoiceType, string VoiceName, FalloutFormKey Info, string WinningPlugin,
    byte Response, string AudioPath, string LipPath);

// One immutable directory index per selected content source. A line's local
// INFO identity and response number are scoped to its defining plugin and the
// speaker's inherited VTYP. Winning loose/BSA precedence is applied when read.
internal sealed class FalloutDialogueVoiceIndex
{
    private readonly Dictionary<string, List<string>> _voices = new(StringComparer.OrdinalIgnoreCase);

    internal FalloutDialogueVoiceIndex(IEnumerable<string> paths)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var canonical = path.Replace('\\', '/');
            var parts = canonical.Split('/');
            if (parts.Length != 5 || !parts[0].Equals("sound", StringComparison.OrdinalIgnoreCase) ||
                !parts[1].Equals("voice", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(canonical).ToLowerInvariant() is not (".ogg" or ".wav" or ".mp3")) continue;
            var stem = Path.GetFileNameWithoutExtension(canonical);
            var responseAt = stem.LastIndexOf('_');
            var identityAt = responseAt < 1 ? -1 : stem.LastIndexOf('_', responseAt - 1);
            if (identityAt < 1 || responseAt - identityAt != 9 ||
                !uint.TryParse(stem.AsSpan(identityAt + 1, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ||
                id > 0xffffff || !byte.TryParse(stem.AsSpan(responseAt + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var response)) continue;
            var key = Key(parts[2], parts[3], id, response);
            if (!_voices.TryGetValue(key, out var entries)) _voices.Add(key, entries = []);
            entries.Add(canonical);
        }
    }

    internal FalloutDialogueVoiceBinding Resolve(FalloutDialogueSpeaker speaker, FalloutDialogueInfo info, int responseIndex)
    {
        if ((uint)responseIndex >= info.Responses.Count) throw new ArgumentOutOfRangeException(nameof(responseIndex));
        if (info.Speaker is { } specified && specified != speaker.Actor)
            throw new InvalidDataException($"INFO {info.Record.FormKey} belongs to a different actor.");
        var response = info.Responses[responseIndex];
        if (response.Sound is not null) throw new NotSupportedException($"INFO {info.Record.FormKey}/{response.Number} requires its explicit SOUN response owner.");
        var candidates = _voices.GetValueOrDefault(Key(info.Record.FormKey.OwnerPlugin, speaker.VoiceName,
            info.Record.FormKey.ObjectId, response.Number));
        if (candidates is not { Count: 1 })
            throw new InvalidDataException($"INFO {info.Record.FormKey}/{response.Number}, actor {speaker.Actor}, voice {speaker.VoiceType} ({speaker.VoiceName}) has {candidates?.Count ?? 0} exact voice candidates.");
        return new(speaker.Actor, speaker.TraitsOwner, speaker.VoiceType, speaker.VoiceName, info.Record.FormKey,
            info.Record.Plugin.Name, response.Number, candidates[0], Path.ChangeExtension(candidates[0], ".lip"));
    }

    private static string Key(string plugin, string voice, uint info, byte response)
        => $"{plugin}/{voice}/{info:x8}/{response}";
}
