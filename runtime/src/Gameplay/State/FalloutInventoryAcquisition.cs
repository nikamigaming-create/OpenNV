using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// All script writes run against the transfer's detached inventories and actual
// source state owners. Unsupported reached operations refuse before publication.
internal sealed class FalloutInventoryAcquisition(FalloutPluginStack records, FalloutReferenceWorld world,
    FalloutPlayerInventory player, FalloutQuestState quests, FalloutQuestScripts scripts,
    Func<FalloutQuestStages> stages, Func<int> level, FalloutGlobalState? globals = null)
{
    private string? _failure;
    internal string? Failure => _failure;
    private void RequireHealthy()
    {
        if (_failure is { } error) throw new InvalidOperationException("Inventory event publication remains failed: " + error);
    }
    private FalloutFormKey Player => records.RuntimeFormKey(0x14);

    internal void Transfer(FalloutPlayerInventory source, FalloutPlayerInventory destination,
        FalloutFormKey sourceReference, FalloutFormKey recipient, IReadOnlyList<FalloutInventoryTransfer> transfers)
    {
        RequireHealthy();
        FalloutPlayerInventory Actual(FalloutFormKey reference) => reference == Player ? player : world.Inventory(reference, level(), globals).Contents;
        if (!ReferenceEquals(Actual(sourceReference), source) || !ReferenceEquals(Actual(recipient), destination))
            throw new InvalidOperationException("Inventory event transfer does not own its actual source/receiver inventories.");
        FalloutInventoryScriptState.ValidateInventory(records, source.Items.Concat(destination.Items));
        using var plan = new Plan(records, world, player, quests, scripts, stages, level, globals, error => _failure = error);
        var lease = new FalloutInventoryChangeLease(() => { plan.Commit(); plan.Accept(); }, plan.Rollback, plan.Complete);
        Execute(plan, () => source.TransferItemsWithPreparedOwner(destination, transfers, lease, (from, to) =>
        {
            plan.Bind(sourceReference, source, from); plan.Bind(recipient, destination, to);
            from.StageEventTransferTo(to, transfers, addition => plan.Prepare(addition, recipient),
                incoming => plan.Dispatch(to, recipient, incoming));
            plan.OriginNotices(transfers.Select(transfer => new FalloutHudEvent(recipient == Player
                ? FalloutHudEventKind.ItemAdded : FalloutHudEventKind.ItemRemoved, transfer.Item, transfer.Count)).ToArray());
            plan.PrepareEffects();
        }));
    }

    internal void Add(FalloutFormKey item, int count, bool silent)
    {
        RequireHealthy();
        using var plan = new Plan(records, world, player, quests, scripts, stages, level, globals, error => _failure = error);
        var lease = new FalloutInventoryChangeLease(() => { plan.Commit(); plan.Accept(); }, plan.Rollback, plan.Complete);
        Execute(plan, () => player.ModifyWithPreparedOwner(staged =>
        {
            plan.Bind(Player, player, staged);
            plan.Add(Player, item, count, silent);
            plan.PrepareEffects();
        }, lease));
    }

    internal void TakePlaced(FalloutFormKey reference, FalloutReferenceInstance instance, FalloutItemVariant variant)
    {
        RequireHealthy();
        var script = FalloutScriptLocals.AttachedScript(records, records.GetEffective(instance.Base));
        if (script is not null && FalloutScriptLocals.ReadDeclarations(script).Count != 0)
            throw new NotSupportedException("Placed inventory-event pickup needs its retained reference-local handoff.");
        var taken = instance.Taken; var changed = false;
        void RequireSource()
        {
            var ownership = world.Ownership(reference);
            if (instance.Taken != taken || !world.CanActivate(reference) || ownership.Owner != variant.Owner ||
                ownership.Global != variant.Global || ownership.FactionRank != variant.FactionRank)
                throw new InvalidOperationException("Placed item changed during inventory-event preparation.");
        }
        using var plan = new Plan(records, world, player, quests, scripts, stages, level, globals, error => _failure = error);
        var lease = new FalloutInventoryChangeLease(() =>
        {
            RequireSource(); plan.Commit(); changed = true; instance.Taken = true; plan.Accept();
        }, () =>
        {
            try { if (changed) instance.Taken = taken; }
            finally { plan.Rollback(); }
        }, plan.Complete);
        Execute(plan, () => player.ModifyWithPreparedOwner(staged =>
        {
            plan.Bind(Player, player, staged);
            plan.Add(Player, instance.Base, 1, false, variant);
            plan.PrepareEffects();
        }, lease));
    }

    private void Execute(Plan plan, Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            if (plan.Accepted || error is FalloutInventoryCleanupException) _failure = error.ToString();
            throw;
        }
    }

    private sealed class Plan : IDisposable
    {
        private readonly FalloutPluginStack _records;
        private readonly FalloutReferenceWorld _world;
        private readonly FalloutPlayerInventory _player;
        private readonly FalloutQuestState _quests, _shadowQuests;
        private readonly FalloutQuestScripts _scripts;
        private readonly Func<FalloutQuestStages> _stages;
        private readonly Func<int> _level;
        private readonly FalloutGlobalState? _globals;
        private readonly Action<string> _failFinalization;
        private readonly long _questRevision, _noteRevision, _cardRevision;
        private readonly string? _globalBaseline;
        private readonly int _sourceLevel;
        private readonly FalloutPlayerNotes _notes;
        private readonly FalloutPlayerCaravanCards _cards;
        private readonly Dictionary<FalloutFormKey, (FalloutPlayerInventory Actual, FalloutPlayerInventory Staged, long Revision, bool Outer)> _inventories = [];
        private readonly List<FalloutQuestObjectiveChange> _objectiveChanges = [];
        private readonly List<FalloutHudEvent> _notices = [];
        private readonly List<FalloutMessageIntent> _messages = [];
        private readonly List<FalloutInventorySoundIntent> _sounds = [];
        private readonly List<FalloutInventoryChangeLease> _leases = [];
        private readonly FalloutScriptExecutionBudget _budget = new();
        private FalloutQuestStages? _shadowStages;
        private IReadOnlyList<FalloutQuestStageResultSnapshot>? _stageBaseline;
        private FalloutQuestScripts.PreparedMessageBatch? _messageBatch;
        private bool _prepared, _completed, _messageCommitted;
        private int _committed;
        internal bool Accepted { get; private set; }
        internal void Accept() => Accepted = true;
        private FalloutFormKey Player => _records.RuntimeFormKey(0x14);

        internal Plan(FalloutPluginStack records, FalloutReferenceWorld world, FalloutPlayerInventory player,
            FalloutQuestState quests, FalloutQuestScripts scripts, Func<FalloutQuestStages> stages,
            Func<int> level, FalloutGlobalState? globals, Action<string> failFinalization)
        {
            _records = records; _world = world; _player = player; _quests = quests;
            _scripts = scripts; _stages = stages; _level = level; _globals = globals; _failFinalization = failFinalization;
            _sourceLevel = level();
            _globalBaseline = globals is null ? null : System.Text.Json.JsonSerializer.Serialize(globals.Capture());
            _questRevision = quests.Revision; _noteRevision = world.PlayerNotes.Revision; _cardRevision = world.PlayerCaravanCards.Revision;
            _shadowQuests = quests.CopyForInventoryEvents();
            _shadowQuests.ObjectiveChanged += change =>
            {
                _objectiveChanges.Add(change);
                if (!_shadowQuests.IsCompleted(change.Quest) && change.After.Displayed &&
                    (!change.Before.Displayed || !change.Before.Completed && change.After.Completed))
                    _notices.Add(new(change.After.Completed ? FalloutHudEventKind.ObjectiveCompleted : FalloutHudEventKind.ObjectiveDisplayed,
                        change.Quest, 0, Quest: change.Quest, ObjectiveIndex: change.After.Index));
            };
            _notes = world.PlayerNotes.CopyForInventoryEvents();
            _cards = new(records); _cards.Restore(world.PlayerCaravanCards.Capture());
        }

        internal void Bind(FalloutFormKey reference, FalloutPlayerInventory actual, FalloutPlayerInventory staged)
        {
            if (_inventories.ContainsKey(reference)) throw new InvalidOperationException("Inventory event receiver has two staging owners.");
            _inventories.Add(reference, (actual, staged, actual.Revision, true));
        }

        private FalloutPlayerInventory Inventory(FalloutFormKey reference)
        {
            if (_inventories.TryGetValue(reference, out var current)) return current.Staged;
            var actual = reference == Player ? _player : _world.Inventory(reference, _level(), _globals).Contents;
            var staged = actual.PrepareScriptStage();
            _inventories.Add(reference, (actual, staged, actual.Revision, false));
            return staged;
        }

        internal FalloutItemAddition Prepare(FalloutItemAddition addition, FalloutFormKey recipient)
        {
            var item = _records.GetEffective(addition.Form);
            if (item.Signature == "NOTE")
                throw new NotSupportedException("Collected-note transfer requires its placed/inventory retirement owner.");
            var state = FalloutInventoryScriptState.Prepare(_records, item, addition.Variant);
            return addition with { Variant = addition.Variant with { ScriptState = state } };
        }

        internal void Dispatch(FalloutPlayerInventory inventory, FalloutFormKey recipient, IReadOnlyList<FalloutItemAddition> incoming)
        {
            foreach (var addition in incoming)
            {
                if (addition.Variant.ScriptState is not { } state) continue;
                if (_world.InventoryScriptFrameActive)
                {
                    inventory.StoreInventoryScriptState(addition.Form, state.Admit(recipient, 1));
                    continue;
                }
                var item = _records.GetEffective(addition.Form);
                var script = FalloutScriptLocals.AttachedScript(_records, item) ?? throw new InvalidDataException("Inventory event script disappeared.");
                var locals = state.Resolve(script);
                var fields = script.ReadSubrecords().ToArray();
                var bindings = new FalloutScriptBindings(_records, item, script, fields);
                var execution = Execution(item, script, bindings, locals, recipient,
                    () => inventory.RemoveInventoryScriptInstance(item.FormKey, state.Identity));
                FalloutInventoryScriptEvents.Dispatch(_records, item, "OnAdd", recipient, execution);
                inventory.StoreInventoryScriptState(item.FormKey, state with { Locals = locals.Capture() });
            }
        }

        internal void OriginNotices(IReadOnlyList<FalloutHudEvent> notices) => _notices.AddRange(notices);

        internal void Add(FalloutFormKey recipient, FalloutFormKey form, int count, bool silent, FalloutItemVariant? extra = null)
        {
            var inventory = Inventory(recipient);
            var incoming = inventory.Add(_records, form, count, _level(), true, _globals, extra,
                leveledItems: _world.LeveledItems, prepareAddition: addition => Prepare(addition, recipient));
            if (recipient == Player && !silent)
                _notices.AddRange(incoming.GroupBy(value => value.Form).Select(group =>
                    new FalloutHudEvent(FalloutHudEventKind.ItemAdded, group.Key, group.Sum(value => value.Variant.Count))));
            Dispatch(inventory, recipient, incoming);
        }

        private FalloutInventoryScriptExecution Execution(FalloutPluginRecord owner, FalloutPluginRecord script,
            FalloutScriptBindings bindings, FalloutScriptEffectLocals? locals, FalloutFormKey recipient, Action? removeMe)
        {
            bool IsForm(string name) => FalloutScriptBindings.IsPlayer(name) ? bindings.HasPlayerReference : locals?.Contains(name) == true ? locals.Kind(name) == FalloutScriptLocalKind.Form :
                bindings.TryForm(name) is not null;
            FalloutScriptValue Read(string name)
            {
                if (locals?.Contains(name) == true) return locals.Read(name);
                if (FalloutScriptBindings.IsPlayer(name) && bindings.HasPlayerReference) return FalloutScriptValue.Form(_records.RuntimeFormId(bindings.Reference(name)));
                if (bindings.HasVariable(name))
                {
                    var variable = bindings.Variable(name);
                    if (_records.GetEffective(variable.Owner).Signature != "QUST")
                        throw new NotSupportedException("Inventory script placed-local access needs its staged reference owner.");
                    return _shadowQuests.Variable(variable.Owner, variable.Index);
                }
                if (bindings.TryForm(name) is { } form) return FalloutScriptValue.Form(_records.RuntimeFormId(form.FormKey));
                throw new NotSupportedException("Inventory event operand has no staged owner: " + name);
            }
            void Write(string name, FalloutScriptValue value)
            {
                if (locals?.Contains(name) == true) { locals.Write(name, value); return; }
                var variable = bindings.Variable(name);
                if (_records.GetEffective(variable.Owner).Signature != "QUST" || value.Kind != FalloutScriptValueKind.Number)
                    throw new NotSupportedException("Inventory event write has no staged quest numeric owner.");
                _shadowQuests.SetVariable(variable.Owner, variable.Index, value.Number);
            }
            var values = new FalloutScriptValueContext(Read, Write, IsForm: IsForm);
            FalloutScriptFunction? Function(string name) => name.Equals("GetContainer", StringComparison.OrdinalIgnoreCase)
                ? FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Form(_records.RuntimeFormId(recipient)), readOnly: true) : null;
            double Number(string token) => FalloutNvseNumericExpression.EvaluateValue([token], values, Function).Number;
            int Integer(string token)
            {
                var value = Number(token);
                if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                    throw new InvalidDataException("Inventory event integer is not representable.");
                return (int)value;
            }
            void Command(string name, IReadOnlyList<string> arguments)
            {
                var parts = name.Split('.');
                if (parts.Length > 2) throw new NotSupportedException("Inventory event receiver path is unbound.");
                var target = parts.Length == 2 ? bindings.Reference(parts[0]) : recipient;
                var operation = parts[^1].ToLowerInvariant();
                switch (operation)
                {
                    case "additem":
                        if (parts.Length != 2 || arguments.Count is < 2 or > 3) throw new NotSupportedException("AddItem receiver/argument ownership is unbound.");
                        Add(target, bindings.Form(arguments[0]).FormKey, Integer(arguments[1]), arguments.Count == 3 && Integer(arguments[2]) != 0);
                        return;
                    case "removeitem":
                        if (parts.Length != 2 || arguments.Count != 2) throw new NotSupportedException("RemoveItem receiver/destination ownership is unbound.");
                        var removed = bindings.Form(arguments[0]).FormKey; var count = Integer(arguments[1]);
                        if (!FalloutReferenceWorld.IsInventoryItem(_records.GetEffective(removed).Signature))
                            throw new NotSupportedException("RemoveItem non-item/list expansion has no staged removal owner.");
                        if (count < 0) throw new NotSupportedException("Negative RemoveItem count is unbound.");
                        if (count == 0) return;
                        var inventory = Inventory(target);
                        if (inventory.Item(removed) is { RecordType: "WEAP" } weapon && inventory.Equipped.Contains(weapon.RuntimeFormId) &&
                            target != Player && _world.Get(target).QueryNativeCondition is not null)
                            throw new NotSupportedException("Inventory script removal needs its live worn-weapon retirement lease.");
                        inventory.Remove(removed, count, true);
                        return;
                    case "removeme":
                        if (arguments.Count != 0 || parts.Length != 1 || removeMe is null)
                            throw new NotSupportedException("RemoveMe has no actual incoming item owner.");
                        removeMe(); return;
                    case "addcardtoplayer":
                    case "addcard":
                        if (parts.Length != 1 || arguments.Count != 0) throw new NotSupportedException("Card addition arguments are unbound.");
                        _cards.Add(owner.FormKey); return;
                    case "showmessage":
                        if (parts.Length != 1 || arguments.Count != 1) throw new NotSupportedException("Formatted ShowMessage is unbound.");
                        var messageForm = bindings.Form(arguments[0]).FormKey;
                        var message = FalloutSourceMessage.Read(_records.GetEffective(messageForm));
                        var messageScript = script.Signature == "QUST"
                            ? FalloutScriptLocals.AttachedScript(_records, script)?.FormKey ??
                                throw new NotSupportedException("Stage ShowMessage has no actual executing SCPT caller.")
                            : script.FormKey;
                        _messages.Add(new(messageForm, messageScript));
                        if (!message.Modal) _notices.Add(new(FalloutHudEventKind.Message, messageForm, 0, Script: messageScript));
                        return;
                    case "playsound":
                        if (parts.Length != 1 || arguments.Count != 1) throw new NotSupportedException("PlaySound argument ownership is unbound.");
                        _sounds.Add(new(script.FormKey, bindings.Form(arguments[0]).FormKey)); return;
                    case "addnote":
                        if (parts.Length != 1 || arguments.Count != 1) throw new NotSupportedException("AddNote receiver is unbound.");
                        var note = bindings.Form(arguments[0]).FormKey;
                        if (_notes.AddMembership(note)) _notices.Add(_world.PlayerNoteNotice(note));
                        return;
                    case "setstage":
                        if (parts.Length != 1 || arguments.Count != 2) throw new NotSupportedException("SetStage argument ownership is unbound.");
                        StageOwner().Enter(bindings.Form(arguments[0]).FormKey, checked((short)Integer(arguments[1]))); return;
                    case "setobjectivecompleted":
                    case "setobjectivedisplayed":
                        if (parts.Length != 1 || arguments.Count != 3) throw new NotSupportedException("Objective argument ownership is unbound.");
                        _shadowQuests.ApplyObjective(bindings.Form(arguments[0]).FormKey, checked((uint)Integer(arguments[1])),
                            operation == "setobjectivedisplayed", Integer(arguments[2]) != 0); return;
                    default: throw new NotSupportedException("Inventory event command has no staged owner: " + name);
                }
            }
            return new(values, Function, Command, Budget: _budget);
        }

        private FalloutQuestStages StageOwner()
        {
            if (_shadowStages is not null) return _shadowStages;
            _stageBaseline = _stages().CaptureResults();
            _shadowStages = new(_records, _shadowQuests, (quest, fields, source) =>
            {
                var bindings = new FalloutScriptBindings(_records, quest, quest, fields);
                var execution = Execution(quest, quest, bindings, null, Player, null);
                return FalloutGameModeProgram.Read("begin Result\n" + source + "\nend", "Result").Steps(name => execution.Values.Read(name).Number,
                    (name, value) => execution.Values.Write(name, value), execution.Command,
                    execution.Function, execution.UserFunction, _budget, execution.Values);
            }, _ => throw new NotSupportedException("Inventory stage conditions need a staged gameplay query owner."));
            _shadowStages.RestoreResults(_stageBaseline);
            return _shadowStages;
        }

        internal void PrepareEffects()
        {
            if (_prepared) throw new InvalidOperationException("Inventory event plan was prepared twice.");
            // All callbacks capable of changing other state run before the
            // pure quest/note/message/sound state publications below.
            foreach (var item in _inventories.Values.Where(item => !item.Outer))
                _leases.Add(item.Actual.PrepareScriptReplacement(item.Staged, item.Revision));
            _leases.Add(_quests.PrepareInventoryReplacement(_shadowQuests, _questRevision, _objectiveChanges));
            if (_shadowStages is not null) _leases.Add(_stages().PrepareInventoryReplacement(_shadowStages, _stageBaseline!));
            _leases.Add(_world.PlayerNotes.PrepareInventoryReplacement(_notes, _noteRevision));
            _leases.Add(_world.PlayerCaravanCards.PrepareReplacement(_cards.Capture()));
            if (_messages.Count != 0)
            {
                _messageBatch = _scripts.PrepareMessageBatch(_messages);
                if (!_messageBatch.HudEvents.SequenceEqual(_notices.Where(notice => notice.Kind == FalloutHudEventKind.Message)))
                    throw new InvalidOperationException("Prepared message HUD order differs from its source inventory commands.");
            }
            _player.Notifications.RequirePublish(_notices);
            if (_sounds.Count != 0) _leases.Add(_scripts.Sounds.PrepareInventorySounds(_sounds));
            _prepared = true;
        }

        internal void Commit()
        {
            if (!_prepared || _level() != _sourceLevel ||
                (_globals is null ? null : System.Text.Json.JsonSerializer.Serialize(_globals.Capture())) != _globalBaseline ||
                _quests.Revision != _questRevision || _world.PlayerNotes.Revision != _noteRevision ||
                _world.PlayerCaravanCards.Revision != _cardRevision ||
                _inventories.Values.Any(item => !item.Outer && item.Actual.Revision != item.Revision))
                throw new InvalidOperationException("Inventory event source state changed before publication.");
            _messageBatch?.RequireCurrent();
            while (_committed < _leases.Count) _leases[_committed++].Commit();
            if (_messageBatch is not null) { _messageBatch.Commit(); _messageCommitted = true; }
        }

        internal void Rollback()
        {
            var failures = new List<Exception>();
            if (_messageCommitted)
            {
                try { _messageBatch!.Rollback(); } catch (Exception error) { failures.Add(error); }
                _messageCommitted = false;
            }
            for (var index = _committed - 1; index >= 0; index--)
                try { _leases[index].Dispose(); } catch (Exception error) { failures.Add(error); }
            _committed = 0;
            if (failures.Count != 0) throw new AggregateException("Inventory event rollback failed.", failures);
        }

        internal void Complete()
        {
            // The outer inventory transaction is already irrevocably committed.
            // Never unwind its state/FIFO after a callback or sound was exposed.
            _completed = true;
            var failures = new List<Exception>();
            foreach (var lease in _leases)
                try { lease.Complete(); } catch (Exception error) { failures.Add(error); }
            foreach (var notice in _notices)
            {
                try
                {
                    if (notice.Kind == FalloutHudEventKind.NoteAdded) _player.Notifications.PublishNote(notice);
                    else _player.Notifications.Publish([notice]);
                }
                catch (Exception error) { failures.Add(error); }
            }
            foreach (var item in _inventories.Where(item => item.Key != Player && item.Value.Actual.Revision != item.Value.Revision))
                try { _world.InventoryChanged(item.Key); } catch (Exception error) { failures.Add(error); }
            if (failures.Count == 0) return;
            var failure = new AggregateException("Committed inventory event finalization failed.", failures);
            _failFinalization(failure.ToString());
            throw new InvalidOperationException(failure.Message, failure);
        }

        public void Dispose()
        {
            if (_completed) return;
            if (Accepted)
            {
                _failFinalization("Committed inventory event did not reach its required finalization boundary.");
                Complete();
                return;
            }
            var failures = new List<Exception>();
            try { if (!_completed && _committed != 0) Rollback(); }
            catch (Exception error) { failures.Add(error); }
            foreach (var lease in _leases)
                try { lease.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Inventory event preparation cleanup failed.", failures);
        }
    }
}
