using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutScriptedEffectConsumption(long Ordinal, FalloutFormKey Item, string ItemSha256,
    int CountBefore, int CountAfter, long InventoryRevisionBefore, long InventoryRevisionAfter,
    ulong ClockMutation, bool InventoryCommitted, bool PoolsCommitted, bool EffectsPublished,
    IReadOnlyList<long> Instances, IReadOnlyList<int> EffectOrdinals, string? Failure);

internal sealed partial class FalloutPlayerAbilityScripts
{
    private readonly Dictionary<long, Entry> _transients = [];
    private FalloutScriptedEffectClock? _clock;
    private FalloutScriptedEffectClockSnapshot? _restoreClock;
    private FalloutPlayerInventory? _inventory;
    private FalloutPlayerVitals? _targetVitals;
    private FalloutScriptedEffectConsumption? _lastConsumption;
    private long _consumptions;
    private string? _sourceFailure;
    private bool _applying;

    internal void BindClock(FalloutScriptedEffectClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (_clock is not null) throw new InvalidOperationException("Active effects already have their gameplay-clock owner.");
        if (_restoreClock is { } saved && clock.Capture() != saved)
            throw new InvalidDataException("Restored active effects lost their shared gameplay-clock prefix.");
        _clock = clock;
    }

    internal void BindInventory(FalloutPlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (_inventory is not null) throw new InvalidOperationException("Scripted Aid already has its actual player inventory.");
        _inventory = inventory;
    }

    internal void BindTargetVitals(FalloutPlayerVitals vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);
        if (_targetVitals is not null) throw new InvalidOperationException("Effects already have their actual engine-player vitals.");
        _targetVitals = vitals;
    }

    private FalloutScriptedEffectClock Clock => _clock ??
        throw new NotSupportedException("Scripted active effects have no actual shared gameplay-clock owner.");
    private long NextGeneration() => _generation = checked(_generation + 1);
    private IEnumerable<Entry> EveryEntry => _entries.Values.Concat(_transients.Values);

    private FalloutCompiledActiveEffectReceipt Execute(FalloutCompiledActiveEffectInvocation invocation) =>
        (_execute ?? throw new NotSupportedException("Active effects have no genuine compiled executor."))(invocation);

    private FalloutPlayerAbilityScriptState CaptureEntry(Entry entry)
    {
        var timeline = entry.Lifetime.Capture();
        return entry.State with
        {
            Generation = entry.Lifetime.EventListGeneration,
            Active = entry.Lifetime.Applied,
            Started = entry.Lifetime.Started,
            Error = entry.Lifetime.Failure,
            Locals = entry.Lifetime.Locals.Capture(),
            Compiled = entry.Lifetime.Compiled.Capture(),
            Timeline = timeline
        };
    }

    private FalloutAbilityScript SavedDefinition(FalloutPlayerAbilityScriptState saved)
    {
        var item = _records.GetEffective(saved.Spell);
        if (item.Signature == "SPEL")
            return _declarations.Scripts(saved.Spell).SingleOrDefault(effect => effect.EffectOrdinal == saved.EffectOrdinal) ??
                throw new InvalidDataException("Saved constant effect has no winning source ordinal.");
        if (item.Signature == "ALCH")
        {
            var source = FalloutIngestible.Read(_records, saved.Spell);
            if ((uint)saved.EffectOrdinal >= source.Effects.Count)
                throw new InvalidDataException("Saved Aid effect ordinal is outside its winning source.");
            return FalloutScriptedEffectSource.FromIngestible(_records, source, source.Effects[saved.EffectOrdinal]);
        }
        throw new NotSupportedException("Saved timed/equipped magic application has no genuine delivery owner.");
    }

    private void SynchronizeLifecycle()
    {
        var selection = _selection().Distinct().ToArray();
        if (_synchronizing)
        {
            if (!selection.SequenceEqual(_synchronizingSelection))
                throw new NotSupportedException("Active-effect source selection changed during its actual script invocation.");
            return;
        }
        if (_sourceFailure is { } failure) throw new NotSupportedException(failure);
        _ = Clock;
        _synchronizing = true; _synchronizingSelection = selection;
        try
        {
            var candidates = selection.SelectMany(form => _declarations.Scripts(form)).ToArray();
            var selected = candidates.Select(effect => (effect.Spell, effect.EffectOrdinal)).ToHashSet();
            // Start and Finish can query another effect in the same source
            // selection. Bind every declared instance and its independent
            // event list before entering any lifecycle script. Construction
            // does not apply conditions or mark a Start as completed.
            foreach (var definition in candidates)
            {
                var key = (definition.Spell, definition.EffectOrdinal);
                if (!_entries.TryGetValue(key, out var entry) || entry.Lifetime.Retired)
                {
                    var next = checked(_generation + 1);
                    entry = BuildCompiled(definition, next, null);
                    _entries[key] = entry; _generation = next;
                }
            }
            foreach (var entry in _entries.Values.OrderBy(entry => entry.Lifetime.InstanceGeneration))
                if (!selected.Contains((entry.Definition.Spell, entry.Definition.EffectOrdinal)))
                {
                    var alreadyRetired = entry.Lifetime.Retired;
                    try
                    {
                        entry.Lifetime.RemoveFromSourceSelection();
                        if (!alreadyRetired && entry.Lifetime.Retired)
                        { _lastRetired = CaptureEntry(entry); _retired = checked(_retired + 1); }
                    }
                    finally { entry.State = CaptureEntry(entry); }
                }
            foreach (var definition in candidates)
            {
                var entry = _entries[(definition.Spell, definition.EffectOrdinal)];
                try { entry.Lifetime.InitializeConstant(_condition); }
                finally { entry.State = CaptureEntry(entry); }
            }
        }
        finally { _synchronizingSelection = []; _synchronizing = false; }
    }

    internal void AdvanceFromCurrentSourceFrame()
    {
        if (_sourceFailure is { } retained) throw new NotSupportedException(retained);
        if (_synchronizing) throw new InvalidOperationException("Gameplay effect clock cannot enter a script invocation recursively.");
        SynchronizeLifecycle();
        var pulse = Clock.EnterSourceFrame();
        Exception? failure = null;
        _synchronizing = true; _synchronizingSelection = _selection().Distinct().ToArray();
        try
        {
            var vitals = _targetVitals ?? throw new NotSupportedException("Effect update has no actual engine-player life-state owner.");
            foreach (var entry in EveryEntry.OrderBy(entry => entry.Lifetime.InstanceGeneration))
            {
                if (vitals.State.HitPoints == 0)
                    throw new NotSupportedException("Dead-target scripted effects require their original persist/termination producer.");
                try { entry.Lifetime.Advance(pulse, _condition); }
                finally { entry.State = CaptureEntry(entry); }
                if (vitals.State.HitPoints == 0)
                    throw new NotSupportedException("Effect command changed the actual target to dead; its termination consumer remains unowned.");
            }
            // Successful expired consumed instances cease to be live effects.
            // Keep the last actual closure and a monotonic total, not an
            // ever-growing tape of completed per-frame script cursors.
            foreach (var pair in _transients.Where(pair => pair.Value.Lifetime.Retired).ToArray())
            {
                _lastRetired = CaptureEntry(pair.Value); _retired = checked(_retired + 1);
                _transients.Remove(pair.Key);
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            _synchronizingSelection = []; _synchronizing = false;
            Clock.Retire(pulse, failure);
        }
    }

    private long _retired;
    private FalloutPlayerAbilityScriptState? _lastRetired;

    internal FalloutScriptedIngestibleUse PrepareIngestible(FalloutIngestible item,
        IReadOnlyList<FalloutIngestibleEffect> selected)
    {
        if (_sourceFailure is { } retained) throw new NotSupportedException(retained);
        if (_synchronizing || _applying) throw new NotSupportedException("Consuming another scripted Aid during an effect invocation requires its nested application owner.");
        var inventory = _inventory ?? throw new NotSupportedException("Scripted Aid has no actual inventory binding.");
        if (inventory.Item(item.Form) is not { Count: > 0 } entry)
            throw new InvalidOperationException("Prepared scripted Aid is absent from the actual player inventory.");
        var definitions = selected.Where(effect => effect.Archetype == 1)
            .Select(effect => FalloutScriptedEffectSource.FromIngestible(_records, item, effect)).ToArray();
        if (definitions.Length == 0) throw new InvalidOperationException("Scripted Aid preparation has no selected source script effect.");
        foreach (var definition in definitions)
        {
            var source = FalloutScriptedEffectSource.Read(_records, definition);
            if (source.Origin != FalloutScriptedEffectOrigin.Ingestible)
                throw new InvalidDataException("Aid application source is not an actual consumed item.");
            if (_transients.Values.Any(value => value.Definition.Spell == definition.Spell &&
                value.Definition.EffectOrdinal == definition.EffectOrdinal && !value.Lifetime.Retired))
                throw new NotSupportedException("Repeated live scripted Aid requires its original stacking/replacement consumer.");
            // Decode its actual complete compiled source before inventory
            // mutation. No source text or successful callback is substituted.
            var script = _records.GetEffective(definition.Script);
            var locals = new FalloutScriptEffectLocals(script);
            _ = new FalloutCompiledActiveEffectExecution(_records,
                _records.RuntimeFormKey(FalloutPlayerActorValues.PlayerReference), definition,
                checked(_generation + 1), script, locals);
        }
        return new(this, inventory, item, definitions, entry.Count, inventory.Revision, Clock.LastConsumedMutation);
    }

    internal void CommitIngestible(FalloutScriptedIngestibleUse use, Action publishExistingPools)
    {
        use.Require(this, _inventory, Clock.LastConsumedMutation);
        if (_sourceFailure is { } retained) throw new NotSupportedException(retained);
        if (_applying || _synchronizing) throw new NotSupportedException("Nested scripted Aid application has no current source owner.");
        if (FalloutIngestible.Read(_records, use.Item.Form).Hash != use.Item.Hash)
            throw new InvalidDataException("Prepared scripted Aid changed its winning original source bytes.");
        var consumption = new FalloutScriptedEffectConsumption(checked(_consumptions + 1), use.Item.Form,
            use.Item.Hash.ToLowerInvariant(), use.CountBefore, use.CountBefore, use.RevisionBefore, use.RevisionBefore,
            use.ClockMutation, false, false, false, [], use.Definitions.Select(value => value.EffectOrdinal).ToArray(), null);
        _consumptions = consumption.Ordinal; _lastConsumption = consumption;
        _applying = true;
        try
        {
            use.Consume(); // The genuine inventory Remove, exactly once.
            consumption = consumption with
            {
                InventoryCommitted = true,
                CountAfter = use.Inventory.Item(use.Item.Form)?.Count ?? 0,
                InventoryRevisionAfter = use.Inventory.Revision
            };
            _lastConsumption = consumption;
            publishExistingPools();
            consumption = consumption with { PoolsCommitted = true }; _lastConsumption = consumption;
            var instances = new List<long>();
            foreach (var definition in use.Definitions)
            {
                var generation = checked(_generation + 1);
                var entry = BuildCompiled(definition, generation, null);
                _generation = generation; _transients.Add(generation, entry); instances.Add(generation);
                consumption = consumption with { Instances = instances.ToArray() }; _lastConsumption = consumption;
                entry.State = entry.State with { Consumption = consumption };
            }
            consumption = consumption with { EffectsPublished = true }; _lastConsumption = consumption;
            foreach (var instance in consumption.Instances)
                _transients[instance].State = _transients[instance].State with { Consumption = consumption };
            // Source Start is produced by the next actual positive effect tick.
            // Applying Aid never advances time or fakes a script retirement.
        }
        catch (Exception failure)
        {
            _sourceFailure = "Scripted Aid application retained its consumed/published prefix: " + failure.Message;
            consumption = consumption with
            {
                Failure = _sourceFailure,
                CountAfter = use.Inventory.Item(use.Item.Form)?.Count ?? 0,
                InventoryRevisionAfter = use.Inventory.Revision,
                InventoryCommitted = consumption.InventoryCommitted ||
                    (use.Inventory.Item(use.Item.Form)?.Count ?? 0) == use.CountBefore - 1 && use.Inventory.Revision > use.RevisionBefore
            };
            _lastConsumption = consumption;
            foreach (var instance in consumption.Instances)
                _transients[instance].State = _transients[instance].State with { Consumption = consumption };
            throw;
        }
        finally { _applying = false; }
    }

    internal static void ValidateConsumption(FalloutScriptedEffectConsumption? state)
    {
        if (state is null) return;
        if (state.Ordinal <= 0 || state.Item.ObjectId == 0 || !ValidHash(state.ItemSha256) || state.CountBefore <= 0 || state.CountAfter < 0 ||
            state.InventoryRevisionBefore < 0 || state.InventoryRevisionAfter < state.InventoryRevisionBefore ||
            state.Instances is null || state.EffectOrdinals is null || state.EffectOrdinals.Count == 0 ||
            state.EffectOrdinals.Any(value => value < 0) || state.EffectOrdinals.Distinct().Count() != state.EffectOrdinals.Count ||
            state.Instances.Count > state.EffectOrdinals.Count || state.Instances.Any(value => value <= 0) ||
            state.Instances.Distinct().Count() != state.Instances.Count ||
            state.PoolsCommitted && !state.InventoryCommitted || state.EffectsPublished && (!state.PoolsCommitted || state.Instances.Count == 0) ||
            state.InventoryCommitted && (state.CountAfter != state.CountBefore - 1 || state.InventoryRevisionAfter <= state.InventoryRevisionBefore) ||
            state.EffectsPublished && state.Instances.Count != state.EffectOrdinals.Count ||
            !state.EffectsPublished && state.Failure is null || state.Failure is { Length: 0 })
            throw new InvalidDataException("Current scripted Aid lost its actual inventory/pool/effect publication prefix.");
    }

    private void RequireConsumptionSource(FalloutScriptedEffectConsumption? state)
    {
        if (state is null) return;
        ValidateConsumption(state);
        var item = FalloutIngestible.Read(_records, state.Item);
        if (item.Hash.ToLowerInvariant() != state.ItemSha256)
            throw new InvalidDataException("Saved Aid application differs from its exact winning item bytes.");
        foreach (var ordinal in state.EffectOrdinals)
        {
            if ((uint)ordinal >= item.Effects.Count)
                throw new InvalidDataException("Saved Aid application has no winning source effect ordinal.");
            _ = FalloutScriptedEffectSource.FromIngestible(_records, item, item.Effects[ordinal]);
        }
    }

    private static bool ConsumptionOwns(FalloutScriptedEffectConsumption state, long instance, int ordinal)
    {
        for (var index = 0; index < state.Instances.Count; ++index)
            if (state.Instances[index] == instance) return state.EffectOrdinals[index] == ordinal;
        return false;
    }
}

// The constructor is not an item-consumed success receipt. It retains the
// actual prepared inventory owner and can perform its genuine removal once.
internal sealed class FalloutScriptedIngestibleUse
{
    private readonly FalloutPlayerAbilityScripts _owner;
    private bool _consumed;
    internal FalloutPlayerInventory Inventory { get; }
    internal FalloutIngestible Item { get; }
    internal IReadOnlyList<FalloutAbilityScript> Definitions { get; }
    internal int CountBefore { get; }
    internal long RevisionBefore { get; }
    internal ulong ClockMutation { get; }
    internal FalloutScriptedIngestibleUse(FalloutPlayerAbilityScripts owner, FalloutPlayerInventory inventory,
        FalloutIngestible item, IReadOnlyList<FalloutAbilityScript> definitions, int count, long revision, ulong mutation)
    { _owner = owner; Inventory = inventory; Item = item; Definitions = definitions; CountBefore = count; RevisionBefore = revision; ClockMutation = mutation; }

    internal void Require(FalloutPlayerAbilityScripts owner, FalloutPlayerInventory? inventory, ulong mutation)
    {
        if (!ReferenceEquals(_owner, owner) || !ReferenceEquals(Inventory, inventory) || _consumed ||
            Inventory.Revision != RevisionBefore || Inventory.Item(Item.Form)?.Count != CountBefore || mutation != ClockMutation)
            throw new InvalidOperationException("Prepared scripted Aid no longer owns its actual inventory/time prefix.");
    }

    internal void Consume()
    {
        if (_consumed) throw new InvalidOperationException("Scripted Aid cannot replay its genuine inventory removal.");
        _consumed = true; Inventory.Remove(Item.Form, 1, true);
        if ((Inventory.Item(Item.Form)?.Count ?? 0) != CountBefore - 1 || Inventory.Revision <= RevisionBefore)
            throw new InvalidDataException("Scripted Aid inventory did not publish its exact single-item removal.");
    }

    internal void Commit(Action publishExistingPools) => _owner.CommitIngestible(this, publishExistingPools);
}
