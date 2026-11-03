using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutInventoryScriptContext(FalloutFormKey? Container, Action Remove, bool ElapsedTimeOwned,
    FalloutScriptInventoryCaller Caller);
internal sealed record FalloutInventoryScriptResult(FalloutFormKey Item, Guid Identity, int Blocks, string? Error);

internal sealed partial class FalloutReferenceScripts
{
    private sealed record InventoryCatalog(FalloutPlayerInventory Inventory, long Revision,
        IReadOnlyList<(FalloutFormKey Item, Guid Identity)> Instances);
    private readonly Dictionary<FalloutFormKey, InventoryCatalog> _inventoryCatalogs = [];
    private readonly Dictionary<(FalloutFormKey Item, FalloutFormKey Script, string Hash),
        (FalloutScriptBindings Bindings, IReadOnlyList<FalloutScriptEventProgram> Blocks)> _inventoryPrograms = [];
    private bool _advancingInventory;

    internal IReadOnlyList<FalloutInventoryScriptResult> AdvanceInventory(FalloutFormKey container, double seconds, bool gameMode = true, Func<bool>? canContinue = null)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_advancingInventory) throw new InvalidOperationException("Inventory scripts cannot recursively advance their frame owner.");
        var inventory = world.Inventory(container, (host.PlayerLevel ??
            throw new NotSupportedException("Inventory script frame has no actual level owner."))(), host.Globals).Contents;
        var traversalOwned = inventory.HasNativeScriptOrder;
        const string missingTraversal = "Full inventory GameMode/traversal has no retained source order; only one pending event-list can execute without structural mutation.";
        IReadOnlyList<(FalloutFormKey Item, Guid Identity)>? isolatedEvents = null;
        if (!traversalOwned)
        {
            var retained = inventory.RetainedScriptInstances();
            isolatedEvents = retained.Where(value => inventory.Item(value.Item)!.Variants!
                .Single(variant => variant.ScriptState?.Identity == value.Identity).ScriptState!.Events is { Count: > 0 }).ToArray();
            // A single real event list has no relative event-list order to
            // invent. Unknown GameMode and initialization ordering stay refused.
            if (isolatedEvents.Count != 1 || inventory.ScriptInitializations.Count != 0)
                return retained.Select(value => new FalloutInventoryScriptResult(value.Item, value.Identity, 0, missingTraversal)).ToArray();
        }
        if (!_inventoryCatalogs.TryGetValue(container, out var catalog) ||
            !ReferenceEquals(catalog.Inventory, inventory) || catalog.Revision != inventory.Revision || !traversalOwned)
        {
            catalog = new(inventory, inventory.Revision, isolatedEvents ?? inventory.ScriptInstances());
        }
        if (catalog.Instances.Count == 0)
        {
            _inventoryCatalogs[container] = catalog;
            return [];
        }
        var results = new List<FalloutInventoryScriptResult>();
        var budget = new FalloutScriptExecutionBudget();
        var mutationAtEntry = inventory.ScriptMutationRevision;
        using var iteration = inventory.EnterScriptIteration(traversalOwned);
        using var frame = world.EnterInventoryScriptFrame();
        _advancingInventory = true;
        try
        {
            if (!traversalOwned)
            {
                if (canContinue?.Invoke() != false)
                {
                    var pending = catalog.Instances.Single();
                    Invoke(pending.Item, pending.Identity, null, eventOnly: true);
                    results.Add(new(pending.Item, pending.Identity, 0, missingTraversal));
                }
                return results;
            }
            // Initialization calls have their own ordered repeated-GUID receipt;
            // they do not inherit the regular containing-object context.
            while (inventory.ScriptInitializations.Count != 0)
            {
                if (canContinue?.Invoke() == false) return results;
                var call = inventory.ScriptInitializations[0];
                var entry = inventory.NativeScriptEntries.Single(value => value.Instances.Contains(call.Identity));
                Invoke(entry.Item, call.Identity, call);
                inventory.CompleteScriptInitialization(call);
            }
            var seenMutation = inventory.ScriptMutationRevision;
            var outer = 0;
            while (outer < inventory.NativeScriptEntries.Count)
            {
                if (canContinue?.Invoke() == false) break;
                var form = inventory.NativeScriptEntries[outer].Item;
                var inner = 0;
                var restarted = false;
                while (true)
                {
                    if (canContinue?.Invoke() == false) return results;
                    var current = inventory.NativeScriptEntries.SingleOrDefault(entry => entry.Item == form);
                    if (current is null || inner >= current.Instances.Count) break;
                    Invoke(form, current.Instances[inner], null);
                    if (inventory.ScriptMutationRevision != seenMutation)
                    {
                        // The native mutation flag breaks the inner walk,
                        // reloads the actual outer head, clears the flag, then
                        // advances to head.next. The head may be nonscripted.
                        seenMutation = inventory.ScriptMutationRevision;
                        outer = 1; restarted = true; break;
                    }
                    ++inner;
                }
                if (!restarted) ++outer;
            }
        }
        finally
        {
            _advancingInventory = false;
            if (!traversalOwned || inventory.ScriptMutationRevision != mutationAtEntry ||
                inventory.NativeScriptEntries.Sum(entry => entry.Instances.Count) != catalog.Instances.Count)
                _inventoryCatalogs.Remove(container);
            else _inventoryCatalogs[container] = catalog with { Revision = inventory.Revision };
        }
        return results;

        void Invoke(FalloutFormKey form, Guid identity, FalloutInventoryScriptInitialization? initialization, bool eventOnly = false)
        {
            var state = inventory.Item(form)?.Variants?
                .SingleOrDefault(variant => variant.ScriptState?.Identity == identity)?.ScriptState;
            if (state is null) throw new InvalidDataException("Native script cursor lost its actual retained event list.");
            if (state.Error is { } previous) { results.Add(new(form, identity, 0, previous)); return; }
            if (initialization is null && !gameMode && state.Events is not { Count: > 0 }) return;
            using var invocation = inventory.EnterScriptInvocation(identity);
            var script = records.GetEffective(state.Script);
            var locals = state.Resolve(script);
            var key = (form, state.Script, state.Sha256);
            if (!_inventoryPrograms.TryGetValue(key, out var program))
            {
                var fields = script.ReadSubrecords().ToArray();
                var source = fields.Single(field => field.Signature == "SCTX");
                program = (new(records, records.GetEffective(form), script, fields),
                    FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(source.Data.Span)));
                _inventoryPrograms.Add(key, program);
            }
            var executed = 0;
            string? failure = null;
            try
            {
                if (eventOnly && program.Blocks.Any(block => block.Event.Equals("GameMode", StringComparison.OrdinalIgnoreCase)))
                    throw new NotSupportedException("Isolated inventory event also declares GameMode; its full ordered frame is unqualified.");
                foreach (var block in program.Blocks)
                {
                    var pending = inventory.Item(form)?.Variants?
                        .SingleOrDefault(variant => variant.ScriptState?.Identity == identity)?.ScriptState?.Events ?? [];
                    var mask = block.Event.ToLowerInvariant() switch { "onadd" => 1u, "onequip" => 2u, "onunequip" => 8u, "onhit" => 128u, "onfire" => 256u, _ => 0u };
                    var admitted = !eventOnly && (initialization?.GameMode ?? gameMode) && block.Event.Equals("GameMode", StringComparison.OrdinalIgnoreCase) ||
                        initialization is null && mask != 0 && pending.Any(value => (value.Mask & mask) != 0 &&
                            (block.Filter is null || program.Bindings.Reference(block.Filter) == value.Actor));
                    if (!admitted) continue;
                    var context = new FalloutInventoryScriptContext(initialization is null ? container : null,
                        () =>
                        {
                            if (initialization is not null) throw new NotSupportedException("Initial inventory RemoveMe has no native containing-object owner.");
                            inventory.RemoveInventoryScriptInstance(form, identity);
                        }, initialization is null && gameMode,
                        new(form, identity, initialization is null ? container : null,
                            () => inventory.Item(form)?.Variants?.Any(variant => variant.ScriptState is { } actual &&
                                actual.Identity == identity && actual.Script == state.Script && actual.Sha256 == state.Sha256) == true));
                    foreach (var _ in Steps(form, program.Bindings, block.Program, null, initialization is null ? seconds : 0,
                        budget: budget, effectLocals: locals, inventoryContext: context)) { }
                    ++executed;
                }
            }
            catch (Exception error) { failure = error.Message; }
            var retained = inventory.Item(form)?.Variants?
                .SingleOrDefault(variant => variant.ScriptState?.Identity == identity)?.ScriptState;
            if (retained is not null)
                inventory.StoreInventoryScriptState(form, retained with
                { Locals = locals.Capture(), Events = failure is null && initialization is null ? null : retained.Events, Error = failure });
            results.Add(new(form, identity, executed, failure));
        }
    }
}
