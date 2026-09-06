using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSayToCommand(string SpeakerEditorId, string TargetEditorId, string TopicEditorId);
internal sealed record FalloutDialogueResponse(byte Number, string Text, uint Emotion, int EmotionValue,
    FalloutFormKey? Sound, FalloutFormKey? SpeakerAnimation, FalloutFormKey? ListenerAnimation, byte[] SourceBytes);
internal sealed record FalloutDialogueInfo(FalloutPluginRecord Record, FalloutFormKey Quest, byte Type,
    byte NextSpeaker, byte Flags, byte Flags2, IReadOnlyList<byte[]> Conditions,
    IReadOnlyList<FalloutDialogueResponse> Responses, string BeginScript, string EndScript);

/// <summary>Winning INFO data and file order, with explicit PNAM insertion.</summary>
internal sealed partial class FalloutDialogueTopic
{
    internal FalloutPluginRecord Topic { get; }
    internal IReadOnlyList<FalloutDialogueInfo> Infos { get; }

    private FalloutDialogueTopic(FalloutPluginRecord topic, IReadOnlyList<FalloutDialogueInfo> infos)
    {
        Topic = topic;
        Infos = infos;
    }

    internal static FalloutDialogueTopic Read(FalloutPluginStack stack, string editorId)
    {
        var topic = Find(stack, "DIAL", editorId);
        var order = new List<FalloutFormKey>();
        foreach (var context in stack.Plugins)
        {
            foreach (var record in context.Plugin.Records.Where(record => record.Signature == "INFO" &&
                record.Groups.Any(group => group.Type == 7 && record.Plugin.AdjustFormId(group.LabelAsUInt32) == topic.FormKey)))
            {
                var previous = record.ReadSubrecords().Where(field => field.Signature == "PNAM").ToArray();
                if (previous.Length > 1 || (previous.Length == 1 && previous[0].Data.Length != 4))
                    throw new InvalidDataException($"INFO {record.FormKey} has invalid PNAM.");
                if (record.IsDeleted) { order.Remove(record.FormKey); continue; }
                if (previous.Length == 0)
                {
                    if (!order.Contains(record.FormKey)) order.Add(record.FormKey);
                    continue;
                }
                order.Remove(record.FormKey);
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(previous[0].Data.Span);
                var insertion = raw == 0 ? 0 : order.IndexOf(record.Plugin.AdjustFormId(raw)) + 1;
                if (raw != 0 && insertion == 0)
                    throw new NotSupportedException($"INFO {record.FormKey} PNAM precedes an unavailable INFO.");
                order.Insert(insertion, record.FormKey);
            }
        }
        var infos = order.Where(key => stack.TryGetEffective(key, out _)).Select(key => Decode(stack.GetEffective(key))).ToArray();
        if (infos.Length == 0) throw new InvalidDataException($"DIAL {topic.FormKey} has no winning INFO.");
        return new FalloutDialogueTopic(topic, infos);
    }

    internal FalloutDialogueInfo? Select(FalloutFormKey speakerBase, IReadOnlySet<FalloutFormKey> said,
        Func<FalloutFormKey, float> questStage, Func<FalloutCondition, float>? context = null)
    {
        foreach (var info in Infos)
        {
            if ((info.Flags & 4) != 0 && said.Contains(info.Record.FormKey)) continue;
            if (!ConditionsPass(info, speakerBase, questStage, context)) continue;
            // SayTo owns one complete INFO and finishes after its responses and
            // end script. Goodbye requires no further conversational turn here;
            // it must not suppress the authored line. Random and other routing
            // flags still require their own selection owners.
            if (info.Type != 1 || info.NextSpeaker != 0 || (info.Flags & ~5) != 0 || info.Flags2 != 0)
                throw new NotSupportedException($"INFO {info.Record.FormKey} needs its conversation/random/flag owner.");
            return info;
        }
        return null;
    }

    internal static IReadOnlyList<FalloutSayToCommand> SayToCommands(string script)
    {
        var commands = new List<FalloutSayToCommand>();
        foreach (var line in CodeLines(script))
        {
            var match = SayToPattern().Match(line);
            if (match.Success)
                commands.Add(new FalloutSayToCommand(match.Groups["speaker"].Value,
                    match.Groups["target"].Value, match.Groups["topic"].Value));
            else if (line.Contains("sayto", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Unsupported source SayTo command: {line}");
        }
        return commands;
    }

    internal static IEnumerable<string> CodeLines(string script) => script.Split('\n')
        .Select(line => line.Split(';')[0].Trim()).Where(line => line.Length != 0);

    internal static FalloutPluginRecord Find(FalloutPluginStack stack, string signature, string editorId)
    {
        var matches = stack.EffectiveRecords(signature).Where(record => record.ReadSubrecords().Any(field =>
            field.Signature == "EDID" && Text(field.Data.Span).Equals(editorId, StringComparison.OrdinalIgnoreCase))).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidDataException($"Expected one winning {signature} with EDID {editorId}, found {matches.Length}.");
    }

    internal static FalloutFormKey RequiredForm(FalloutPluginRecord record, string signature)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == signature).ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException($"{record.FormKey} requires one {signature} FormID.");
        return record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
    }

    internal static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end != bytes.Length - 1) throw new InvalidDataException("Dialogue source text is not null-terminated.");
        return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes[..end]);
    }

    internal static string ScriptText(ReadOnlySpan<byte> bytes)
    {
        // SCTX has a declared byte extent; unlike EDID, its terminal null is optional.
        if (bytes.Length != 0 && bytes[^1] == 0) bytes = bytes[..^1];
        if (bytes.IndexOf((byte)0) >= 0) throw new InvalidDataException("Embedded null in source script.");
        return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes);
    }

    private static FalloutDialogueInfo Decode(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data.ToArray();
        if (data.Length is not (3 or 4)) throw new InvalidDataException($"INFO {record.FormKey} DATA size is unsupported.");
        var responses = new List<FalloutDialogueResponse>();
        var conditions = new List<byte[]>();
        var begin = new List<string>();
        var end = new List<string>();
        var afterNext = false;
        for (var index = 0; index < fields.Length; ++index)
        {
            var field = fields[index];
            if (field.Signature == "NEXT") { afterNext = true; continue; }
            if (field.Signature == "SCTX")
            {
                (afterNext ? end : begin).Add(ScriptText(field.Data.Span));
                continue;
            }
            if (field.Signature == "CTDA") { conditions.Add(field.Data.ToArray()); continue; }
            if (field.Signature != "TRDT") continue;
            var response = field.Data.ToArray();
            if (response.Length is not (16 or 20 or 24)) throw new InvalidDataException($"INFO {record.FormKey} TRDT size is unsupported.");
            string? text = null;
            FalloutFormKey? speakerAnimation = null, listenerAnimation = null;
            while (index + 1 < fields.Length && fields[index + 1].Signature is "NAM1" or "NAM2" or "NAM3" or "SNAM" or "LNAM")
            {
                var next = fields[++index];
                if (next.Signature == "NAM1") text = text is null ? Text(next.Data.Span) : throw new InvalidDataException("Duplicate response text.");
                if (next.Signature is "SNAM" or "LNAM")
                {
                    if (next.Data.Length != 4) throw new InvalidDataException("Invalid response animation FormID.");
                    var key = record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(next.Data.Span));
                    if (next.Signature == "SNAM") speakerAnimation = key; else listenerAnimation = key;
                }
            }
            var sound = response.Length < 20 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(16));
            responses.Add(new FalloutDialogueResponse(response[12], text ?? throw new InvalidDataException("Missing response text."),
                BinaryPrimitives.ReadUInt32LittleEndian(response), BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(4)),
                sound == 0 ? null : record.Plugin.AdjustFormId(sound), speakerAnimation, listenerAnimation, response));
        }
        if (responses.Count == 0 || responses.Select(response => response.Number).Distinct().Count() != responses.Count)
            throw new InvalidDataException($"INFO {record.FormKey} has absent/duplicate responses.");
        return new FalloutDialogueInfo(record, RequiredForm(record, "QSTI"), data[0], data[1], data[2],
            data.Length == 4 ? data[3] : (byte)0, conditions, responses, string.Join('\n', begin), string.Join('\n', end));
    }

    private static bool ConditionsPass(FalloutDialogueInfo info, FalloutFormKey speaker,
        Func<FalloutFormKey, float> questStage, Func<FalloutCondition, float>? context) =>
        FalloutCondition.AllPass(info.Conditions.Select(bytes => FalloutCondition.Read(info.Record, bytes)).ToArray(), condition =>
        {
            if (condition.RunOn == 0)
            {
                if (condition.Function == 72) return condition.FormArgument1 == speaker ? 1 : 0;
                if (condition.Function == 58) return questStage(condition.FormArgument1);
            }
            return context?.Invoke(condition) ?? throw new NotSupportedException(
                $"INFO {info.Record.FormKey} condition {condition.Function} RunOn {condition.RunOn} is unbound.");
        }, evaluateRunOn: true);
    [GeneratedRegex(@"^(?<speaker>[A-Za-z0-9_]+)\.sayto\s+(?<target>[A-Za-z0-9_]+)\s+(?<topic>[A-Za-z0-9_]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SayToPattern();
}
