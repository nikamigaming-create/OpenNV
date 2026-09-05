using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSourceMessage(FalloutFormKey Form, string Title, string Text,
    bool Modal, IReadOnlyList<string> Buttons, FalloutFormKey? Icon = null, uint? DisplaySeconds = null, bool AutomaticTime = false)
{
    internal static FalloutSourceMessage Read(FalloutPluginRecord record)
    {
        if (record.Signature != "MESG") throw new InvalidDataException("ShowMessage target is not MESG.");
        var fields = record.ReadSubrecords().ToArray();
        string Text(string name, bool required = false)
        {
            var matches = fields.Where(field => field.Signature == name).ToArray();
            if (matches.Length > 1 || required && matches.Length != 1) throw new InvalidDataException($"MESG {name} is ambiguous or missing.");
            return matches.Length == 0 ? "" : FalloutDialogueTopic.Text(matches[0].Data.Span);
        }
        var flags = fields.Single(field => field.Signature == "DNAM").Data;
        if (flags.Length != 4) throw new InvalidDataException("MESG flag extent is invalid.");
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(flags.Span);
        if ((bits & ~3u) != 0 || fields.Any(field => field.Signature == "CTDA"))
            throw new NotSupportedException($"MESG {record.FormKey} flags or conditional buttons need an owner.");
        var icon = fields.SingleOrDefault(field => field.Signature == "INAM").Data;
        if (icon.Length != 4) throw new InvalidDataException("MESG icon extent is invalid.");
        var iconId = BinaryPrimitives.ReadUInt32LittleEndian(icon.Span);
        var time = fields.SingleOrDefault(field => field.Signature == "TNAM").Data;
        if (time.Length != 0 && time.Length != 4) throw new InvalidDataException("MESG time extent is invalid.");
        var seconds = time.Length == 0 ? (uint?)null : BinaryPrimitives.ReadUInt32LittleEndian(time.Span);
        if ((bits & 3) == 0 && seconds is null or 0) throw new NotSupportedException("Timed MESG has no positive display time.");
        return new(record.FormKey, Text("FULL"), Text("DESC", true), (bits & 1) != 0,
            fields.Where(field => field.Signature == "ITXT").Select(field => FalloutDialogueTopic.Text(field.Data.Span)).ToArray(),
            iconId == 0 ? null : record.Plugin.AdjustFormId(iconId), seconds, (bits & 2) != 0);
    }
}

internal sealed record FalloutQuestScriptSnapshot(FalloutFormKey Quest, FalloutFormKey Script,
    double Remaining, long Executions, string? Error);
internal sealed record FalloutQuestScriptsSnapshot(IReadOnlyList<FalloutQuestScriptSnapshot> Instances,
    IReadOnlyList<FalloutFormKey> Messages, FalloutHudNotificationsSnapshot? Notifications = null);

internal sealed class FalloutQuestScripts
{
    // Engine-created player reference; it is not a placed record in an ESM.
    private const uint PlayerReferenceRuntimeFormId = 0x14;
    private sealed class Instance(FalloutPluginRecord quest, FalloutPluginRecord script, FalloutGameModeProgram program, double delay)
    {
        internal readonly FalloutPluginRecord Quest = quest, Script = script;
        internal readonly FalloutGameModeProgram Program = program;
        internal readonly double Delay = delay;
        internal double Remaining;
        internal string? Error;
        internal long Executions;
    }

    private readonly FalloutPluginStack _records;
    private readonly FalloutQuestState _quests;
    private readonly List<Instance> _instances = [];
    private readonly Dictionary<FalloutFormKey, Dictionary<string, uint>> _variables = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyDictionary<string, FalloutPluginRecord>> _references = [];
    private readonly Dictionary<FalloutFormKey, string> _unbound = [];
    private readonly FalloutPlayerInventory _inventory;
    private readonly FalloutGlobalState? _globals;
    private readonly Queue<FalloutSourceMessage> _messages = [];

    internal object State => new
    {
        quests = _instances.Select(instance => new { quest = instance.Quest.FormKey.ToString(), script = instance.Script.FormKey.ToString(), instance.Executions, instance.Remaining, instance.Error }).ToArray(),
        unbound = _unbound.Select(pair => new { quest = pair.Key.ToString(), error = pair.Value }).ToArray(),
        inventory = _inventory.Items,
        messages = _messages.ToArray(),
        notifications = _inventory.Notifications.Capture(),
        scheduling = "source-delay; native ordering unverified",
    };
    internal IReadOnlyList<FalloutCampaignItem> Inventory => _inventory.Items;
    internal bool TryTakeMessage(out FalloutSourceMessage? message) => _messages.TryDequeue(out message);

    internal FalloutQuestScriptsSnapshot Capture(FalloutSourceMessage? displayed = null) => new(
        _instances.Select(instance => new FalloutQuestScriptSnapshot(instance.Quest.FormKey, instance.Script.FormKey,
            instance.Remaining, instance.Executions, instance.Error)).ToArray(),
        (displayed is null ? Enumerable.Empty<FalloutFormKey>() : [displayed.Form]).Concat(_messages.Select(message => message.Form)).ToArray(),
        _inventory.Notifications.Capture());

    internal void Restore(FalloutQuestScriptsSnapshot snapshot)
    {
        if (_instances.Any(instance => instance.Executions != 0) || _messages.Count != 0)
            throw new InvalidOperationException("Script restoration requires a fresh owner.");
        var states = snapshot.Instances.ToDictionary(instance => instance.Quest);
        if (states.Count != _instances.Count || _instances.Any(instance => !states.ContainsKey(instance.Quest.FormKey)))
            throw new InvalidDataException("Saved quest script owners differ from the winning source graph.");
        foreach (var instance in _instances)
        {
            var state = states[instance.Quest.FormKey];
            if (state.Script != instance.Script.FormKey || !double.IsFinite(state.Remaining) || state.Remaining < 0 ||
                state.Remaining > instance.Delay || state.Executions < 0)
                throw new InvalidDataException("Saved quest script scheduling is invalid.");
        }
        var messages = snapshot.Messages.Select(form => FalloutSourceMessage.Read(_records.GetEffective(form))).ToArray();
        if (messages.Any(message => !message.Modal)) throw new NotSupportedException("Saved message needs a timed HUD owner.");
        if (snapshot.Notifications is { } notifications) _inventory.Notifications.Restore(notifications);
        foreach (var instance in _instances)
        {
            var state = states[instance.Quest.FormKey];
            instance.Remaining = state.Remaining;
            instance.Executions = state.Executions;
            instance.Error = state.Error;
            if (state.Error is not null) _unbound[instance.Quest.FormKey] = state.Error;
        }
        foreach (var message in messages) _messages.Enqueue(message);
    }

    internal FalloutQuestScripts(FalloutPluginStack records, FalloutQuestState quests, IReadOnlySet<FalloutFormKey> claimedQuests,
        FalloutPlayerInventory inventory, FalloutGlobalState? globals = null)
    {
        _records = records;
        _quests = quests;
        _inventory = inventory;
        _globals = globals;
        foreach (var quest in records.EffectiveRecords("QUST"))
        {
            if (claimedQuests.Contains(quest.FormKey)) continue;
            try
            {
                var fields = quest.ReadSubrecords().ToArray();
                var data = fields.Single(field => field.Signature == "DATA").Data;
                if (data.Length != 8) throw new NotSupportedException("Quest DATA version is unbound.");
                if ((data.Span[0] & 1) == 0 || !fields.Any(field => field.Signature == "SCRI")) continue;
                var script = records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
                if (script.Signature != "SCPT") throw new InvalidDataException("Quest script is not SCPT.");
                var source = script.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
                if (source.Length != 1) throw new NotSupportedException("Quest script source is absent or ambiguous.");
                var program = FalloutGameModeProgram.Read(source[0].Data.Span);
                var delay = BinaryPrimitives.ReadSingleLittleEndian(data.Span[4..]);
                // DATA flag 4 selects the authored processing delay. Zero is a
                // valid delay (every GameMode frame), not a missing setting.
                if ((data.Span[0] & 0x10) == 0)
                    throw new NotSupportedException("Default quest scheduling needs an engine delay owner.");
                if (!float.IsFinite(delay) || delay < 0) throw new InvalidDataException("Quest delay is invalid.");
                _instances.Add(new(quest, script, program, delay));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException)
            { _unbound[quest.FormKey] = error.Message; }
        }
    }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var instance in _instances)
        {
            if (instance.Error is not null) continue;
            instance.Remaining -= seconds;
            if (instance.Remaining > 0) continue;
            instance.Remaining = instance.Delay;
            try { Execute(instance); ++instance.Executions; }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
            { instance.Error = error.Message; _unbound[instance.Quest.FormKey] = error.Message; }
        }
    }

    private void Execute(Instance instance)
    {
        var writes = new Dictionary<(FalloutFormKey Quest, uint Index), double>();
        var globalWrites = new Dictionary<FalloutFormKey, float>();
        var additions = new Dictionary<FalloutFormKey, FalloutCampaignItem>();
        var messages = new List<FalloutSourceMessage>();
        var notifications = new List<FalloutHudEvent>();
        FalloutPluginRecord? TryForm(string name)
        {
            if (!_references.TryGetValue(instance.Script.FormKey, out var references))
            {
                var map = new Dictionary<string, FalloutPluginRecord>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in instance.Script.ReadSubrecords().Where(field => field.Signature == "SCRO"))
                {
                    if (field.Data.Length != 4) throw new InvalidDataException("Script reference extent is invalid.");
                    var key = instance.Script.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                    if (_records.RuntimeFormId(key) == PlayerReferenceRuntimeFormId) continue;
                    var record = _records.GetEffective(key);
                    var id = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "EDID").Data;
                    if (id.Length != 0) map.TryAdd(FalloutDialogueTopic.Text(id.Span), record);
                }
                _references[instance.Script.FormKey] = references = map;
            }
            return references.GetValueOrDefault(name);
        }
        FalloutPluginRecord Form(string name) => TryForm(name) ?? throw new NotSupportedException($"Script reference {name} is unbound.");
        FalloutFormKey? Global(string name)
        {
            if (TryForm(name) is not { Signature: "GLOB" } record) return null;
            if (_globals is null) throw new NotSupportedException($"Script global {name} has no shared state owner.");
            var source = FalloutGlobal.Read(record);
            // GLOB operands always widen their Float32 storage into the expression
            // value. FNAM does not use the separate local-variable coercion path.
            if (instance.Script.ReadSubrecords().Any(field => field.Signature == "SCVR" &&
                string.Equals(FalloutDialogueTopic.Text(field.Data.Span), name, StringComparison.OrdinalIgnoreCase)))
                throw new NotSupportedException($"Script operand {name} has ambiguous local/global binding.");
            return source.Form;
        }
        (FalloutFormKey Quest, uint Index) Variable(string name)
        {
            var split = name.Split('.');
            var quest = split.Length == 1 ? instance.Quest : split.Length == 2 ? Form(split[0]) : throw new NotSupportedException("Script variable path is unbound.");
            if (quest.Signature != "QUST") throw new NotSupportedException("Reference script variables need an instance owner.");
            if (!_variables.TryGetValue(quest.FormKey, out var variables))
            {
                variables = new(StringComparer.OrdinalIgnoreCase);
                var script = _records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
                uint? index = null;
                foreach (var field in script.ReadSubrecords())
                {
                    if (field.Signature == "SLSD")
                    {
                        if (field.Data.Length != 24) throw new InvalidDataException("Script variable extent is invalid.");
                        index = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                    }
                    if (field.Signature == "SCVR")
                    {
                        if (index is null || !variables.TryAdd(FalloutDialogueTopic.Text(field.Data.Span), index.Value))
                            throw new InvalidDataException("Script variable identity is ambiguous.");
                        index = null;
                    }
                }
                _variables[quest.FormKey] = variables;
            }
            return variables.TryGetValue(split[^1], out var value) ? (quest.FormKey, value) : throw new NotSupportedException($"Script operand {name} has no variable owner.");
        }
        double Read(string name)
        {
            if (Global(name) is { } global)
                return globalWrites.TryGetValue(global, out var pending) ? pending : _globals!.Get(global);
            var key = Variable(name);
            return writes.TryGetValue(key, out var value) ? value : _quests.Variable(key.Quest, key.Index);
        }
        void Write(string name, double value)
        {
            if (Global(name) is { } global)
            {
                _ = _globals!.Get(global);
                var stored = (float)value;
                if (!float.IsFinite(stored)) throw new InvalidDataException("Script global exceeds Float32 storage.");
                globalWrites[global] = stored;
                return;
            }
            var key = Variable(name);
            _ = _quests.Variable(key.Quest, key.Index);
            if (!double.IsFinite(value)) throw new InvalidDataException("Script variable exceeds its runtime representation.");
            writes[key] = value;
        }
        instance.Program.Execute(Read, Write, (command, arguments) =>
        {
            switch (command.ToLowerInvariant())
            {
                case "short" or "int" or "long" or "float" when arguments.Count == 1: _ = Variable(arguments[0]); break;
                case "showmessage" when arguments.Count == 1:
                    var message = FalloutSourceMessage.Read(Form(arguments[0]));
                    if (message.Modal) messages.Add(message);
                    else notifications.Add(new(FalloutHudEventKind.Message, message.Form, 0, instance.Quest.FormKey, instance.Script.FormKey));
                    break;
                case "player.additem" when arguments.Count is 2 or 3:
                    if (!instance.Script.ReadSubrecords().Any(field => field.Signature == "SCRO" && field.Data.Length == 4 &&
                        _records.RuntimeFormId(instance.Script.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span))) == PlayerReferenceRuntimeFormId))
                        throw new InvalidDataException("Player command has no bound engine reference in SCRO.");
                    var item = Form(arguments[0]);
                    var numericCount = FalloutGameModeProgram.Evaluate([arguments[1]], Read);
                    if (numericCount != Math.Truncate(numericCount)) throw new NotSupportedException("Fractional item additions are unbound.");
                    var count = checked((int)numericCount);
                    if (count <= 0) throw new NotSupportedException("Non-positive item additions are unbound.");
                    var previous = additions.GetValueOrDefault(item.FormKey) ?? _inventory.Item(item.FormKey);
                    var request = new FalloutCampaignInventoryRequest(_records.RuntimeFormId(item.FormKey), arguments[0], item.Signature,
                        checked((previous?.Count ?? 0) + count));
                    additions[item.FormKey] = FalloutCampaignInventoryResolver.Resolve(_records, [request], null).Items.Single();
                    var silent = arguments.Count == 3 ? FalloutGameModeProgram.Evaluate([arguments[2]], Read) : 0;
                    if (silent is not (0 or 1)) throw new NotSupportedException("AddItem silent argument is not a boolean.");
                    if (silent == 0) notifications.Add(new(FalloutHudEventKind.ItemAdded, item.FormKey, count, instance.Quest.FormKey, instance.Script.FormKey));
                    break;
                default: throw new NotSupportedException($"Reached script command {command} with {arguments.Count} arguments has no owner.");
            }
        });
        // Validate the entire reached block before publishing any effects.
        FalloutHudNotifications.Validate(notifications);
        foreach (var (key, value) in writes) _quests.SetVariable(key.Quest, key.Index, value);
        foreach (var (form, value) in globalWrites) _globals!.Set(form, value);
        _inventory.Publish(additions.Values);
        foreach (var message in messages) _messages.Enqueue(message);
        _inventory.Notifications.Publish(notifications);
    }
}
