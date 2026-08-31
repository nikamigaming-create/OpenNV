using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.World.Interactions;

internal sealed partial class ScriptedActivatorInstance : PickupInstance
{
    private readonly Dictionary<string, int> _eventStates =
        new(StringComparer.OrdinalIgnoreCase);
    private ScriptedActivatorEvent? _pending;
    private double _remainingSeconds;
    private Func<ScriptedActivatorEvent, bool>? _authorize;
    private Action<ScriptedActivatorEvent>? _apply;

    internal ScriptedActivatorContract Contract { get; private set; } = null!;

    internal void Configure(
        string referenceFormId,
        string baseFormId,
        string baseEditorId,
        ScriptedActivatorContract contract)
    {
        Contract = contract;
        base.Configure(
            referenceFormId,
            baseFormId,
            baseEditorId,
            displayName: null,
            recordType: "ACTI",
            count: 0,
            weapon: null);
        Name = $"SCRIPTED_ACTIVATOR_{referenceFormId}_{baseEditorId}";
        foreach (var source in contract.Events)
            _eventStates.Add(source.Event, 0);
    }

    internal void Bind(
        Func<ScriptedActivatorEvent, bool> authorize,
        Action<ScriptedActivatorEvent> apply)
    {
        _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    internal bool Activate()
    {
        // The source script's OnActivate block is intentionally empty. It still
        // consumes activation rather than falling through to pickup collection.
        return true;
    }

    internal override bool BeginHold()
    {
        if (!base.BeginHold())
            return false;
        BeginEvent("grab");
        return true;
    }

    internal override void Drop()
    {
        var held = IsHeld;
        base.Drop();
        if (held)
            BeginEvent("release");
    }

    public override void _Process(double delta)
    {
        if (_pending is null)
            return;
        _remainingSeconds -= delta;
        if (_remainingSeconds > 0.0)
            return;
        var completed = _pending;
        _pending = null;
        _eventStates[completed.Event] = 2;
        _apply?.Invoke(completed);
    }

    private void BeginEvent(string eventName)
    {
        if (_pending is not null || !_eventStates.TryGetValue(eventName, out var state) || state != 0)
            return;
        var source = Contract.Events.SingleOrDefault(value =>
            value.Event.Equals(eventName, StringComparison.OrdinalIgnoreCase));
        if (source is null || _authorize?.Invoke(source) != true)
            return;
        _eventStates[eventName] = 1;
        _pending = source;
        _remainingSeconds = source.DelaySeconds;
    }
}

internal sealed record ScriptedActivatorContract(
    string ScriptFormId,
    string ScriptEditorId,
    IReadOnlyList<ScriptedActivatorEvent> Events)
{
    internal static ScriptedActivatorContract Read(JsonElement source)
    {
        if (source.GetProperty("type").GetString() != "scripted-activator" ||
            source.GetProperty("support").GetString() != "delayed-objective-events")
            throw new InvalidOperationException("Scripted activator contract is unsupported.");
        var script = source.GetProperty("script");
        var formId = script.GetProperty("formId").GetString()!;
        var editorId = script.GetProperty("editorId").GetString()!;
        var events = source.GetProperty("events").EnumerateArray()
            .Select(ScriptedActivatorEvent.Read)
            .ToArray();
        if (string.IsNullOrWhiteSpace(formId) || string.IsNullOrWhiteSpace(editorId) ||
            events.Length != 2 || events.Select(value => value.Event).Distinct(
                StringComparer.OrdinalIgnoreCase).Count() != events.Length ||
            !events.Any(value => value.Event.Equals("grab", StringComparison.OrdinalIgnoreCase)) ||
            !events.Any(value => value.Event.Equals("release", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Scripted activator event contract is incomplete.");
        return new ScriptedActivatorContract(formId, editorId, events);
    }
}

internal sealed record ScriptedActivatorEvent(
    string Event,
    ScriptedActivatorObjectiveGuard Guard,
    double DelaySeconds,
    IReadOnlyList<ScriptedActivatorCommand> Commands)
{
    internal static ScriptedActivatorEvent Read(JsonElement source)
    {
        var eventName = source.GetProperty("event").GetString()!;
        var delay = source.GetProperty("delaySeconds").GetDouble();
        var commands = source.GetProperty("commands").EnumerateArray()
            .Select(ScriptedActivatorCommand.Read)
            .ToArray();
        if (eventName is not "grab" and not "release" || !double.IsFinite(delay) || delay < 0.0 ||
            commands.Length == 0)
            throw new InvalidOperationException("Scripted activator event is invalid.");
        return new ScriptedActivatorEvent(
            eventName,
            ScriptedActivatorObjectiveGuard.Read(source.GetProperty("guard")),
            delay,
            commands);
    }
}

internal sealed record ScriptedActivatorObjectiveGuard(
    string QuestFormId,
    string QuestEditorId,
    int ObjectiveIndex,
    string State)
{
    internal static ScriptedActivatorObjectiveGuard Read(JsonElement source)
    {
        var result = new ScriptedActivatorObjectiveGuard(
            source.GetProperty("questFormId").GetString()!,
            source.GetProperty("questEditorId").GetString()!,
            source.GetProperty("objectiveIndex").GetInt32(),
            source.GetProperty("state").GetString()!);
        if (string.IsNullOrWhiteSpace(result.QuestFormId) ||
            string.IsNullOrWhiteSpace(result.QuestEditorId) || result.ObjectiveIndex < 0 ||
            result.State != "displayed")
            throw new InvalidOperationException("Scripted activator objective guard is invalid.");
        return result;
    }
}

internal sealed record ScriptedActivatorCommand(
    string Kind,
    string QuestFormId,
    string QuestEditorId,
    int? Stage,
    int? Index,
    string? State,
    bool? Enabled)
{
    internal static ScriptedActivatorCommand Read(JsonElement source)
    {
        var kind = source.GetProperty("kind").GetString()!;
        var result = new ScriptedActivatorCommand(
            kind,
            source.GetProperty("questFormId").GetString()!,
            source.GetProperty("questEditorId").GetString()!,
            source.TryGetProperty("stage", out var stage) ? stage.GetInt32() : null,
            source.TryGetProperty("index", out var index) ? index.GetInt32() : null,
            source.TryGetProperty("state", out var state) ? state.GetString() : null,
            source.TryGetProperty("enabled", out var enabled) ? enabled.GetBoolean() : null);
        if (string.IsNullOrWhiteSpace(result.QuestFormId) ||
            string.IsNullOrWhiteSpace(result.QuestEditorId) ||
            result.Kind == "setStage" && result.Stage is < 0 or null ||
            result.Kind == "objective" &&
            (result.Index is < 0 or null || result.State != "completed" || result.Enabled != true) ||
            result.Kind is not "setStage" and not "objective")
            throw new InvalidOperationException("Scripted activator command is invalid.");
        return result;
    }
}
