using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSayToCommand(string SpeakerEditorId, string TargetEditorId, string TopicEditorId);
internal sealed record FalloutDialogueResponse(byte Number, string Text, uint Emotion, int EmotionValue,
    FalloutFormKey? Sound, FalloutFormKey? SpeakerAnimation, FalloutFormKey? ListenerAnimation, byte[] SourceBytes);
internal sealed record FalloutDialogueInfo(FalloutPluginRecord Record, FalloutFormKey Quest, byte Type,
    byte NextSpeaker, byte Flags, byte Flags2, IReadOnlyList<byte[]> Conditions,
    IReadOnlyList<FalloutDialogueResponse> Responses, string BeginScript, string EndScript)
{
    internal IReadOnlyList<FalloutFormKey> Choices { get; init; } = [];
    internal IReadOnlyList<FalloutFormKey> AddedTopics { get; init; } = [];
    internal IReadOnlyList<FalloutFormKey> FollowUps { get; init; } = [];
    internal string? Prompt { get; init; }
    internal FalloutFormKey? Speaker { get; init; }
}

/// <summary>Winning INFO data and file order, with explicit PNAM insertion.</summary>
internal sealed partial class FalloutDialogueTopic
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FalloutPluginStack,
        IReadOnlyDictionary<FalloutFormKey, IReadOnlyList<FalloutPluginRecord>>> InfoIndexes = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FalloutPluginStack,
        IReadOnlyDictionary<FalloutFormKey, (byte Type, byte Flags, float Priority)>> TopicHeaders = new();
    internal FalloutPluginRecord Topic { get; }
    internal IReadOnlyList<FalloutDialogueInfo> Infos { get; }

    private FalloutDialogueTopic(FalloutPluginRecord topic, IReadOnlyList<FalloutDialogueInfo> infos)
    {
        Topic = topic;
        Infos = infos;
    }

    // DIAL top-level flags expose the default topic list after an unlinked
    // response. INFO/quest conditions are evaluated anew for this speaker and
    // world state; the previous menu is not a source of available topics.
    internal static IEnumerable<FalloutFormKey> DefaultTopics(FalloutPluginStack stack) => Headers(stack)
        .Where(pair => (pair.Value.Flags & 2) != 0 && (pair.Value.Type == 0 || IsGoodbye(stack, pair.Key))).Select(pair => pair.Key);

    // The built-in GOODBYE lives on the conversation tab but is also a
    // top-level player choice. Its eligible INFO and voice belong to the actor.
    internal static bool IsGoodbye(FalloutPluginStack stack, FalloutFormKey topic) => Headers(stack)[topic].Type == 1 &&
        stack.GetEffective(topic).ReadSubrecords().Any(field => field.Signature == "EDID" &&
            Text(field.Data.Span).Equals("GOODBYE", StringComparison.OrdinalIgnoreCase));

    internal static float Priority(FalloutPluginStack stack, FalloutFormKey topic) => Headers(stack).TryGetValue(topic, out var header)
        ? header.Priority : throw new InvalidDataException($"Dialogue choice {topic} is not a winning DIAL.");

    private static IReadOnlyDictionary<FalloutFormKey, (byte Type, byte Flags, float Priority)> Headers(FalloutPluginStack stack) =>
        TopicHeaders.GetValue(stack, records => records.EffectiveRecords("DIAL").ToDictionary(record => record.FormKey, record =>
        {
            var fields = record.ReadSubrecords().ToArray();
            var data = fields.Where(field => field.Signature == "DATA").ToArray();
            var priorities = fields.Where(field => field.Signature == "PNAM").ToArray();
            if (data.Length != 1 || data[0].Data.Length is not (1 or 2) || priorities.Length > 1 ||
                priorities.Length == 1 && priorities[0].Data.Length != 4)
                throw new InvalidDataException($"DIAL {record.FormKey} has invalid type/priority metadata.");
            var type = data[0].Data.Span[0];
            var flags = data[0].Data.Length == 2 ? data[0].Data.Span[1] : (byte)0;
            // Legacy built-in topics omit PNAM; the field's source default is 50.
            var priority = priorities.Length == 0 ? 50 : BinaryPrimitives.ReadSingleLittleEndian(priorities[0].Data.Span);
            if (type > 7 || (flags & ~3) != 0 || !float.IsFinite(priority))
                throw new NotSupportedException($"DIAL {record.FormKey} type, flags or priority are unsupported.");
            return (type, flags, priority);
        }));

    internal static FalloutDialogueTopic Read(FalloutPluginStack stack, string editorId)
        => Read(stack, Find(stack, "DIAL", editorId).FormKey);

    internal static FalloutDialogueTopic Read(FalloutPluginStack stack, FalloutFormKey form)
    {
        var topic = stack.GetEffective(form);
        if (topic.Signature != "DIAL") throw new InvalidDataException("Dialogue topic is not DIAL.");
        var index = InfoIndexes.GetValue(stack, records => records.Plugins.SelectMany(context => context.Plugin.Records)
            .Where(record => record.Signature == "INFO")
            .GroupBy(record => record.Plugin.AdjustFormId(record.Groups.Single(group => group.Type == 7).LabelAsUInt32))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FalloutPluginRecord>)group.ToArray()));
        var order = new List<FalloutFormKey>();
        foreach (var record in index.GetValueOrDefault(form) ?? [])
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
        var infos = order.Where(key => stack.TryGetEffective(key, out _)).Select(key => Decode(stack.GetEffective(key))).ToArray();
        // Authored topic lists can contain empty placeholders. They remain
        // real DIAL records but contribute no eligible response or choice.
        return new FalloutDialogueTopic(topic, infos);
    }

    internal FalloutDialogueInfo? Select(FalloutFormKey speakerBase, IReadOnlySet<FalloutFormKey> said,
        Func<FalloutFormKey, float> questStage, Func<FalloutCondition, float>? context = null,
        Func<FalloutFormKey, bool>? questEligible = null, Func<FalloutFormKey, int>? questPriority = null,
        bool conversation = false)
    {
        IEnumerable<FalloutDialogueInfo> candidates = questPriority is null ? Infos : Infos.OrderByDescending(info => questPriority(info.Quest));
        foreach (var info in candidates)
        {
            if (!Eligible(info, speakerBase, said, questStage, context, questEligible)) continue;
            // SayTo owns one complete INFO and finishes after its responses and
            // end script. Goodbye requires no further conversational turn here;
            // it must not suppress the authored line. Random and other routing
            // flags still require their own selection owners.
            RequireFlags(info, conversation);
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

    internal static FalloutDialogueInfo Decode(FalloutPluginRecord record)
    {
        if (record.Signature != "INFO") throw new InvalidDataException("Dialogue response target is not INFO.");
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
        IReadOnlyList<FalloutFormKey> Forms(string signature) => fields.Where(field => field.Signature == signature).Select(field =>
            field.Data.Length == 4 ? record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) :
                throw new InvalidDataException($"INFO {record.FormKey} has an invalid {signature} extent.")).ToArray();
        var prompts = fields.Where(field => field.Signature == "RNAM").ToArray();
        if (prompts.Length > 1) throw new InvalidDataException("INFO prompt is duplicated.");
        var speakers = Forms("ANAM");
        if (speakers.Count > 1) throw new InvalidDataException("INFO speaker is duplicated.");
        return new FalloutDialogueInfo(record, RequiredForm(record, "QSTI"), data[0], data[1], data[2],
            data.Length == 4 ? data[3] : (byte)0, conditions, responses, string.Join('\n', begin), string.Join('\n', end))
        {
            Choices = Forms("TCLT"),
            AddedTopics = Forms("NAME"),
            FollowUps = Forms("TCFU"),
            Prompt = prompts.Length == 0 ? null : Text(prompts[0].Data.Span),
            Speaker = speakers.Count == 0 ? null : speakers[0],
        };
    }

    internal static bool ConditionsPass(FalloutDialogueInfo info, FalloutFormKey speaker,
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

    internal static void RequireFlags(FalloutDialogueInfo info, bool conversation)
    {
        if (info.Type != (conversation ? 0 : 1) || info.NextSpeaker != 0 || (info.Flags & ~5) != 0 || info.Flags2 != 0)
            throw new NotSupportedException($"INFO {info.Record.FormKey} needs its conversation/random/flag owner.");
    }

    internal static bool Eligible(FalloutDialogueInfo info, FalloutFormKey speaker, IReadOnlySet<FalloutFormKey> said,
        Func<FalloutFormKey, float> questStage, Func<FalloutCondition, float>? context,
        Func<FalloutFormKey, bool>? questEligible = null) =>
        !((info.Flags & 4) != 0 && said.Contains(info.Record.FormKey)) &&
        (info.Speaker is null || info.Speaker == speaker) && AdmitsSpeaker(info, speaker, context) &&
        (questEligible is null || questEligible(info.Quest)) && ConditionsPass(info, speaker, questStage, context);

    private static bool AdmitsSpeaker(FalloutDialogueInfo info, FalloutFormKey speaker, Func<FalloutCondition, float>? context)
    {
        var conditions = info.Conditions.Select(bytes => FalloutCondition.Read(info.Record, bytes)).ToArray();
        // Pure actor restrictions can prove a line belongs to another actor
        // before unrelated quest/service queries need evaluation. Never split
        // an OR group or move predicates across random queries.
        if (conditions.Any(condition => condition.Function == 77)) return true;
        for (var index = 0; index < conditions.Length; index++)
        {
            var condition = conditions[index];
            if (condition.RunOn != 0 || (condition.Flags & 0x1f) != 0 || index != 0 && (conditions[index - 1].Flags & 1) != 0) continue;
            if (condition.Function == 72)
            {
                if (!FalloutCondition.AllPass([condition], value => value.FormArgument1 == speaker ? 1 : 0)) return false;
            }
            else if (condition.Function is 69 or 70 or 71 or 365 or 427 && context is not null)
            {
                // Some scalar callers lack actor traits. In that case the
                // ordinary ordered evaluator retains the unresolved query.
                try { if (!FalloutCondition.AllPass([condition], context)) return false; }
                catch (NotSupportedException) { }
            }
        }
        return true;
    }
    [GeneratedRegex(@"^(?<speaker>[A-Za-z0-9_]+)\.sayto\s+(?<target>[A-Za-z0-9_]+)\s+(?<topic>[A-Za-z0-9_]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SayToPattern();
}
