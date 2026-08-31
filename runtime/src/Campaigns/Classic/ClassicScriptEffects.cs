using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed class ClassicScriptState : IEquatable<ClassicScriptState>
{
    private readonly SortedDictionary<int, int> _locals;
    private readonly HashSet<string> _flags;

    internal ClassicScriptState(
        IEnumerable<KeyValuePair<int, int>>? locals = null,
        IEnumerable<string>? flags = null)
    {
        _locals = [];
        foreach (var row in locals ?? [])
            _locals.Add(row.Key, row.Value);
        _flags = new HashSet<string>(flags ?? [], StringComparer.Ordinal);
    }

    internal int Local(int index) => _locals.GetValueOrDefault(index);
    internal bool Flag(string name) => _flags.Contains(name);
    internal IReadOnlyDictionary<int, int> Locals => _locals;
    internal IReadOnlyCollection<string> Flags => _flags;

    internal object Save() => new
    {
        locals = _locals.Select(row => new { index = row.Key, value = row.Value }).ToArray(),
        flags = _flags.Order(StringComparer.Ordinal).ToArray(),
    };

    internal static ClassicScriptState Restore(JsonElement source)
    {
        var locals = source.GetProperty("locals").EnumerateArray().Select(row =>
            new KeyValuePair<int, int>(
                row.GetProperty("index").GetInt32(),
                row.GetProperty("value").GetInt32())).ToArray();
        if (locals.Select(row => row.Key).Distinct().Count() != locals.Length ||
            locals.Any(row => row.Key < 0))
            throw new InvalidOperationException("Classic script locals are invalid.");
        var flags = source.GetProperty("flags").EnumerateArray()
            .Select(row => row.GetString() ?? "").ToArray();
        if (flags.Any(string.IsNullOrWhiteSpace) ||
            flags.Distinct(StringComparer.Ordinal).Count() != flags.Length)
            throw new InvalidOperationException("Classic script flags are invalid.");
        return new ClassicScriptState(locals, flags);
    }

    internal void SetLocal(int index, int value)
    {
        if (index < 0)
            throw new InvalidOperationException("Classic script local index is invalid.");
        _locals[index] = value;
    }

    internal void SetFlag(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Classic script flag is invalid.");
        _flags.Add(name);
    }

    public bool Equals(ClassicScriptState? other) => other is not null &&
        _locals.SequenceEqual(other._locals) && _flags.SetEquals(other._flags);

    public override bool Equals(object? obj) => Equals(obj as ClassicScriptState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var row in _locals)
        {
            hash.Add(row.Key);
            hash.Add(row.Value);
        }
        foreach (var flag in _flags.Order(StringComparer.Ordinal))
            hash.Add(flag, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

internal sealed class ClassicPlayerStatusState
{
    private readonly HashSet<string> _injuries;

    internal ClassicPlayerStatusState(
        int poison = 0,
        int radiation = 0,
        IEnumerable<string>? injuries = null)
    {
        if (poison < 0 || radiation < 0)
            throw new InvalidOperationException("Classic player status values are invalid.");
        Poison = poison;
        Radiation = radiation;
        _injuries = new HashSet<string>(injuries ?? [], StringComparer.Ordinal);
        if (_injuries.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Classic player injury identity is invalid.");
    }

    internal int Poison { get; private set; }
    internal int Radiation { get; private set; }
    internal IReadOnlySet<string> Injuries => _injuries;

    internal void Apply(ClassicScriptExecution execution)
    {
        if (execution.PlayerPoisonRemoved != Poison ||
            execution.ClearedPlayerInjuries.Any(injury => !_injuries.Contains(injury)))
            throw new InvalidOperationException(
                "Classic script status result does not match authoritative player state.");
        Poison -= execution.PlayerPoisonRemoved;
        _injuries.ExceptWith(execution.ClearedPlayerInjuries);
    }

    internal object Save() => new
    {
        poison = Poison,
        radiation = Radiation,
        injuries = _injuries.Order(StringComparer.Ordinal).ToArray(),
    };

    internal static ClassicPlayerStatusState Restore(JsonElement source)
    {
        var injuries = source.GetProperty("injuries").EnumerateArray()
            .Select(injury => injury.GetString() ?? "").ToArray();
        if (injuries.Any(string.IsNullOrWhiteSpace) ||
            injuries.Distinct(StringComparer.Ordinal).Count() != injuries.Length)
            throw new InvalidOperationException(
                "Classic saved player injuries are invalid.");
        return new ClassicPlayerStatusState(
            source.GetProperty("poison").GetInt32(),
            source.GetProperty("radiation").GetInt32(),
            injuries);
    }
}

internal readonly record struct ClassicScriptContext(
    bool SourceIsPlayer,
    bool CanSeePlayer,
    int GameTime,
    string? PlayerArtFid = null,
    int PlayerCurrentHitPoints = 0,
    int PlayerMaximumHitPoints = 0,
    int PlayerPoison = 0,
    int PlayerRadiation = 0,
    IReadOnlySet<string>? PlayerInjuries = null);

internal readonly record struct ClassicScriptMessage(int? MessageListId, int MessageId);

internal readonly record struct ClassicDialogueReplySegment(
    ClassicScriptMessage? Message,
    bool PlayerName);

internal readonly record struct ClassicDialogueOption(
    ClassicScriptMessage Message,
    string Target,
    int? MinimumIntelligence,
    int? MaximumIntelligence,
    int Reaction);

internal sealed record ClassicScriptExecution(
    bool Executed,
    bool ScriptOverrides,
    IReadOnlyList<ClassicScriptMessage> DisplayMessages,
    string? OpenDialogueNode,
    bool DialogueEnded,
    int PlayerHealing,
    int PlayerPoisonRemoved,
    int GameTimeAdvanceMinutes,
    IReadOnlySet<string> ClearedPlayerInjuries,
    string? NextProcedure,
    bool DestroySelf,
    IReadOnlyList<ClassicDialogueReplySegment> DialogueReply,
    IReadOnlyList<ClassicDialogueOption> DialogueOptions);

internal sealed class ClassicScriptProgram
{
    private const string Schema = "opennv-classic-script-effects/v1";
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Rule>> _events;

    private ClassicScriptProgram(IReadOnlyDictionary<string, IReadOnlyList<Rule>> events) =>
        _events = events;

    internal static ClassicScriptProgram Parse(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != Schema)
            throw new InvalidOperationException("Unexpected classic script-effect schema.");
        var events = new Dictionary<string, IReadOnlyList<Rule>>(StringComparer.Ordinal);
        foreach (var eventProperty in source.GetProperty("events").EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(eventProperty.Name) ||
                !events.TryAdd(eventProperty.Name,
                    eventProperty.Value.EnumerateArray().Select(ParseRule).ToArray()))
                throw new InvalidOperationException("Classic script event identity is invalid.");
        }
        if (events.Count == 0 || events.Any(row => row.Value.Count == 0))
            throw new InvalidOperationException("Classic script program has no executable rules.");
        return new ClassicScriptProgram(events);
    }

    internal bool Execute(string eventName, ClassicScriptState state, ClassicScriptContext context)
        => ExecuteWithActions(eventName, state, context).Executed;

    internal ClassicScriptExecution ExecuteWithActions(
        string eventName,
        ClassicScriptState state,
        ClassicScriptContext context)
    {
        if (!_events.TryGetValue(eventName, out var rules))
            return new ClassicScriptExecution(
                false, false, [], null, false, 0, 0, 0,
                new HashSet<string>(StringComparer.Ordinal), null, false, [], []);
        var matched = rules.Where(rule => rule.Conditions.All(condition =>
            Matches(condition, state, context))).ToArray();
        var scriptOverrides = false;
        var messages = new List<ClassicScriptMessage>();
        string? openDialogueNode = null;
        var dialogueEnded = false;
        var playerHealing = 0;
        var playerPoisonRemoved = 0;
        var gameTimeAdvanceMinutes = 0;
        var clearedPlayerInjuries = new HashSet<string>(StringComparer.Ordinal);
        string? nextProcedure = null;
        var destroySelf = false;
        var dialogueReply = new List<ClassicDialogueReplySegment>();
        var dialogueOptions = new List<ClassicDialogueOption>();
        foreach (var rule in matched)
        {
            foreach (var effect in rule.Effects)
                Apply(
                    effect, state, context, ref scriptOverrides, messages,
                    ref openDialogueNode, ref dialogueEnded, ref playerHealing,
                    ref playerPoisonRemoved, ref gameTimeAdvanceMinutes,
                    clearedPlayerInjuries, ref nextProcedure,
                    ref destroySelf,
                    dialogueReply, dialogueOptions);
        }
        if (dialogueEnded &&
            (openDialogueNode is not null || dialogueReply.Count > 0 || dialogueOptions.Count > 0))
            throw new InvalidOperationException(
                "Classic script produced a contradictory dialogue result.");
        return new ClassicScriptExecution(
            matched.Length > 0,
            scriptOverrides,
            messages,
            openDialogueNode,
            dialogueEnded,
            playerHealing,
            playerPoisonRemoved,
            gameTimeAdvanceMinutes,
            clearedPlayerInjuries,
            nextProcedure,
            destroySelf,
            dialogueReply,
            dialogueOptions);
    }

    private static Rule ParseRule(JsonElement source)
    {
        var conditions = source.GetProperty("all").EnumerateArray()
            .Select(ParseOperation).ToArray();
        var effects = source.GetProperty("then").EnumerateArray()
            .Select(ParseOperation).ToArray();
        if (conditions.Any(row => row.Name is not
                ("source-is-player" or "can-see-player" or "local-equals" or
                 "local-not-equals" or "player-art-fid-in" or
                 "elapsed-game-time-greater-than" or "flag-set")) ||
            effects.Length == 0 || effects.Any(row => row.Name is not
                ("set-local" or "set-flag" or "script-overrides" or "display-message" or
                 "open-dialogue" or "dialogue-reply-message" or
                 "dialogue-reply-player-name" or "dialogue-option" or "dialogue-end" or
                 "heal-player-to-maximum" or "clear-player-poison" or
                 "advance-game-time-by-player-poison" or "clear-player-injuries" or
                 "call-procedure-if-player-radiation-positive" or "destroy-self")))
            throw new InvalidOperationException(
                "Classic script rule mixes conditions and effects.");
        return new Rule(conditions, effects);
    }

    private static Operation ParseOperation(JsonElement source)
    {
        var operation = source.GetProperty("operation").GetString() ?? "";
        if (operation is not ("source-is-player" or "can-see-player" or "local-equals" or
            "local-not-equals" or "set-local" or "set-flag" or "script-overrides" or
            "display-message" or "player-art-fid-in" or "open-dialogue" or
            "dialogue-reply-message" or "dialogue-reply-player-name" or
            "dialogue-option" or "dialogue-end" or "heal-player-to-maximum" or
            "clear-player-poison" or "advance-game-time-by-player-poison" or
            "clear-player-injuries" or
            "call-procedure-if-player-radiation-positive" or
            "elapsed-game-time-greater-than" or "flag-set" or "destroy-self"))
            throw new InvalidOperationException($"Unsupported classic script operation: {operation}");
        int? index = source.TryGetProperty("index", out var indexValue)
            ? indexValue.GetInt32()
            : null;
        int? value = source.TryGetProperty("value", out var valueElement)
            ? valueElement.GetInt32()
            : null;
        var valueFrom = source.TryGetProperty("valueFrom", out var fromElement)
            ? fromElement.GetString()
            : null;
        var flag = source.TryGetProperty("flag", out var flagElement)
            ? flagElement.GetString()
            : null;
        int? messageListId = source.TryGetProperty("messageListId", out var listElement)
            ? listElement.GetInt32()
            : null;
        int? messageId = source.TryGetProperty("messageId", out var messageElement)
            ? messageElement.GetInt32()
            : null;
        var values = source.TryGetProperty("values", out var valuesElement)
            ? valuesElement.EnumerateArray().Select(row => row.GetString() ?? "").ToArray()
            : null;
        var node = source.TryGetProperty("node", out var nodeElement)
            ? nodeElement.GetString()
            : null;
        var target = source.TryGetProperty("target", out var targetElement)
            ? targetElement.GetString()
            : null;
        int? minimumIntelligence = source.TryGetProperty(
            "minimumIntelligence", out var minimumElement)
            ? minimumElement.GetInt32()
            : null;
        int? maximumIntelligence = source.TryGetProperty(
            "maximumIntelligence", out var maximumElement)
            ? maximumElement.GetInt32()
            : null;
        int? reaction = source.TryGetProperty("reaction", out var reactionElement)
            ? reactionElement.GetInt32()
            : null;
        if (index is < 0 ||
            operation is ("local-equals" or "local-not-equals") &&
                (index is null || value is null) ||
            operation is "elapsed-game-time-greater-than" &&
                (index is null || value is null or <= 0) ||
            operation is "set-local" && (index is null || (value is null) == (valueFrom is null)) ||
            valueFrom is not null && valueFrom != "game-time" ||
            operation is "set-flag" && string.IsNullOrWhiteSpace(flag) ||
            operation is "flag-set" && string.IsNullOrWhiteSpace(flag) ||
            operation is ("display-message" or "dialogue-reply-message") &&
                (messageId is null or < 0 || messageListId is < 0) ||
            operation is "player-art-fid-in" &&
                (values is null || values.Length == 0 || values.Any(string.IsNullOrWhiteSpace)) ||
            operation is "advance-game-time-by-player-poison" && value is null or <= 0 ||
            operation is "clear-player-injuries" &&
                (values is null || values.Length == 0 || values.Any(string.IsNullOrWhiteSpace) ||
                 values.Distinct(StringComparer.Ordinal).Count() != values.Length) ||
            operation is "call-procedure-if-player-radiation-positive" &&
                string.IsNullOrWhiteSpace(target) ||
            operation is "open-dialogue" && string.IsNullOrWhiteSpace(node) ||
            operation is "dialogue-option" &&
                (messageId is null or < 0 || messageListId is < 0 ||
                 string.IsNullOrWhiteSpace(target) || reaction is null ||
                 minimumIntelligence is not null && maximumIntelligence is not null))
            throw new InvalidOperationException($"Classic script operation is incomplete: {operation}");
        return new Operation(
            operation, index, value, valueFrom, flag, messageListId, messageId,
            values, node, target, minimumIntelligence, maximumIntelligence, reaction);
    }

    private static bool Matches(
        Operation operation,
        ClassicScriptState state,
        ClassicScriptContext context) => operation.Name switch
        {
            "source-is-player" => context.SourceIsPlayer,
            "can-see-player" => context.CanSeePlayer,
            "local-equals" => state.Local(operation.Index!.Value) == operation.Value,
            "local-not-equals" => state.Local(operation.Index!.Value) != operation.Value,
            "flag-set" => state.Flag(operation.Flag!),
            "elapsed-game-time-greater-than" =>
                context.GameTime >= state.Local(operation.Index!.Value) &&
                context.GameTime - state.Local(operation.Index!.Value) > operation.Value,
            "player-art-fid-in" => operation.Values!.Contains(
                context.PlayerArtFid ?? "", StringComparer.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException(
                $"Classic script effect used as a condition: {operation.Name}"),
        };

    private static void Apply(
        Operation operation,
        ClassicScriptState state,
        ClassicScriptContext context,
        ref bool scriptOverrides,
        ICollection<ClassicScriptMessage> messages,
        ref string? openDialogueNode,
        ref bool dialogueEnded,
        ref int playerHealing,
        ref int playerPoisonRemoved,
        ref int gameTimeAdvanceMinutes,
        ISet<string> clearedPlayerInjuries,
        ref string? nextProcedure,
        ref bool destroySelf,
        ICollection<ClassicDialogueReplySegment> dialogueReply,
        ICollection<ClassicDialogueOption> dialogueOptions)
    {
        switch (operation.Name)
        {
            case "set-local":
                state.SetLocal(operation.Index!.Value,
                    operation.ValueFrom == "game-time" ? context.GameTime : operation.Value!.Value);
                break;
            case "set-flag":
                state.SetFlag(operation.Flag!);
                break;
            case "script-overrides":
                scriptOverrides = true;
                break;
            case "display-message":
                messages.Add(new ClassicScriptMessage(
                    operation.MessageListId,
                    operation.MessageId!.Value));
                break;
            case "heal-player-to-maximum":
                if (context.PlayerCurrentHitPoints < 0 || context.PlayerMaximumHitPoints < 0 ||
                    context.PlayerCurrentHitPoints > context.PlayerMaximumHitPoints ||
                    playerHealing != 0)
                    throw new InvalidOperationException(
                        "Classic script player healing context is invalid.");
                playerHealing = context.PlayerMaximumHitPoints - context.PlayerCurrentHitPoints;
                break;
            case "clear-player-poison":
                if (context.PlayerPoison < 0 || playerPoisonRemoved != 0)
                    throw new InvalidOperationException(
                        "Classic script player poison context is invalid.");
                playerPoisonRemoved = context.PlayerPoison;
                break;
            case "advance-game-time-by-player-poison":
                if (context.PlayerPoison < 0)
                    throw new InvalidOperationException(
                        "Classic script player poison context is invalid.");
                gameTimeAdvanceMinutes = checked(
                    gameTimeAdvanceMinutes + context.PlayerPoison * operation.Value!.Value);
                break;
            case "clear-player-injuries":
                foreach (var injury in context.PlayerInjuries ?? new HashSet<string>())
                {
                    if (operation.Values!.Contains(injury, StringComparer.Ordinal))
                        clearedPlayerInjuries.Add(injury);
                }
                break;
            case "call-procedure-if-player-radiation-positive":
                if (context.PlayerRadiation < 0 || nextProcedure is not null)
                    throw new InvalidOperationException(
                        "Classic script player radiation context is invalid.");
                if (context.PlayerRadiation > 0)
                    nextProcedure = operation.Target;
                break;
            case "destroy-self":
                if (destroySelf)
                    throw new InvalidOperationException(
                        "Classic script requested duplicate world-object destruction.");
                destroySelf = true;
                break;
            case "open-dialogue":
                if (openDialogueNode is not null || dialogueEnded)
                    throw new InvalidOperationException(
                        "Classic script requested multiple dialogue entry nodes.");
                openDialogueNode = operation.Node;
                break;
            case "dialogue-end":
                if (openDialogueNode is not null || dialogueEnded || dialogueReply.Count > 0 ||
                    dialogueOptions.Count > 0)
                    throw new InvalidOperationException(
                        "Classic script requested a contradictory dialogue result.");
                dialogueEnded = true;
                break;
            case "dialogue-reply-message":
                dialogueReply.Add(new ClassicDialogueReplySegment(
                    new ClassicScriptMessage(
                        operation.MessageListId,
                        operation.MessageId!.Value),
                    false));
                break;
            case "dialogue-reply-player-name":
                dialogueReply.Add(new ClassicDialogueReplySegment(null, true));
                break;
            case "dialogue-option":
                dialogueOptions.Add(new ClassicDialogueOption(
                    new ClassicScriptMessage(
                        operation.MessageListId,
                        operation.MessageId!.Value),
                    operation.Target!,
                    operation.MinimumIntelligence,
                    operation.MaximumIntelligence,
                    operation.Reaction!.Value));
                break;
            default:
                throw new InvalidOperationException(
                    $"Classic script condition used as an effect: {operation.Name}");
        }
    }

    private sealed record Rule(IReadOnlyList<Operation> Conditions, IReadOnlyList<Operation> Effects);
    private sealed record Operation(
        string Name,
        int? Index,
        int? Value,
        string? ValueFrom,
        string? Flag,
        int? MessageListId,
        int? MessageId,
        IReadOnlyList<string>? Values,
        string? Node,
        string? Target,
        int? MinimumIntelligence,
        int? MaximumIntelligence,
        int? Reaction);
}
