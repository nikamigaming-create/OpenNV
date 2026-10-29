using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // A placed reference provides the real calling identity. The currently
    // admitted effects are staged; unsupported commands never publish pickup.
    private bool TakeWithInventoryEvent(FalloutFormKey reference, FalloutReferenceInstance instance,
        FalloutPlayerInventory player, int level, FalloutGlobalState? globals, FalloutItemVariant variant)
    {
        var item = records.GetEffective(instance.Base);
        if (item.Signature == "NOTE") return false;
        var script = FalloutScriptLocals.AttachedScript(records, item);
        if (script is null) return false;
        var fields = script.ReadSubrecords().ToArray();
        var source = fields.Where(field => field.Signature == "SCTX").ToArray();
        if (source.Length != 1) throw new NotSupportedException("Item event requires an unambiguous source program.");
        var recipient = records.RuntimeFormKey(0x14);
        var bindings = new FalloutScriptBindings(records, item, script, fields);
        var events = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(source[0].Data.Span));
        if (!events.Any(block => block.Event.Equals("OnAdd", StringComparison.OrdinalIgnoreCase) &&
            (block.Filter is null || bindings.Reference(block.Filter) == recipient))) return false;
        if (variant.Count != 1 || FalloutScriptLocals.ReadDeclarations(script).Count != 0)
            throw new NotSupportedException("Item event needs a persistent stack or script-local instance owner.");
        var oldItem = player.Item(instance.Base);
        var oldVariants = oldItem?.Variants ?? (oldItem is null ? [] : [new FalloutItemVariant(oldItem.Count)]);
        if (oldVariants.Any(value => value with { Count = 1 } == variant with { Count = 1 }))
            throw new NotSupportedException("Item event cannot distinguish the incoming instance from an existing variant.");

        var stagedCards = new FalloutPlayerCaravanCards(records);
        stagedCards.Restore(PlayerCaravanCards.Capture());
        var cardsRevision = PlayerCaravanCards.Revision;
        FalloutInventoryChangeLease? cardsLease = null;
        var taken = instance.Taken; var changedTaken = false;
        var lease = new FalloutInventoryChangeLease(() =>
        {
            var ownership = Ownership(reference);
            if (instance.Taken != taken || !CanActivate(reference) ||
                ownership.Owner != variant.Owner || ownership.Global != variant.Global || ownership.FactionRank != variant.FactionRank ||
                PlayerCaravanCards.Revision != cardsRevision)
                throw new InvalidOperationException("Item state changed during pickup.");
            cardsLease = PlayerCaravanCards.PrepareReplacement(stagedCards.Capture());
            cardsLease.Commit();
            changedTaken = true;
            instance.Taken = true;
        }, () =>
        {
            try { if (changedTaken) instance.Taken = taken; }
            finally { cardsLease?.Dispose(); }
        }, () => cardsLease?.Complete());
        player.AddWithPreparedOwner(records, instance.Base, 1, level, false, lease, globals, variant,
            stageAddition: staged =>
            {
                bool IsForm(string name) => FalloutScriptBindings.IsPlayer(name) && bindings.HasPlayerReference;
                var values = new FalloutScriptValueContext(name => IsForm(name)
                        ? FalloutScriptValue.Form(records.RuntimeFormId(bindings.Reference(name)))
                        : throw new NotSupportedException($"Item event value {name} has no staged owner."),
                    (_, _) => throw new NotSupportedException("Item event mutation has no staged owner."), IsForm: IsForm);
                var execution = new FalloutInventoryScriptExecution(values,
                    name => name.Equals("GetContainer", StringComparison.OrdinalIgnoreCase)
                        ? FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Form(records.RuntimeFormId(recipient)), readOnly: true) : null,
                    (name, arguments) =>
                    {
                        if (name.Equals("AddCardToPlayer", StringComparison.OrdinalIgnoreCase) || name.Equals("addcard", StringComparison.OrdinalIgnoreCase))
                        {
                            if (arguments.Count != 0) throw new NotSupportedException("Card addition arguments are unowned.");
                            stagedCards.Add(instance.Base); return;
                        }
                        if (name.Equals("RemoveMe", StringComparison.OrdinalIgnoreCase) && arguments.Count == 0)
                        {
                            var current = staged.Item(instance.Base) ?? throw new InvalidOperationException("Incoming item is absent.");
                            var variants = current.Variants ?? [new FalloutItemVariant(current.Count)];
                            var indices = Enumerable.Range(0, variants.Count).Where(index =>
                                variants[index] with { Count = 1 } == variant with { Count = 1 }).ToArray();
                            if (indices.Length != 1) throw new NotSupportedException("Incoming item variant is ambiguous.");
                            staged.RemoveVariant(instance.Base, indices[0], 1, true, staged.Revision); return;
                        }
                        throw new NotSupportedException($"Item event command {name} has no staged owner.");
                    });
                FalloutInventoryScriptEvents.Dispatch(records, item, "OnAdd", recipient, execution);
            });
        return true;
    }
}
