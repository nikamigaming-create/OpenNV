using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorFoodAcquisition(FalloutFormKey Actor, FalloutFormKey Reference,
    FalloutFormKey Item, int Count, Action Commit);

internal sealed partial class FalloutReferenceWorld
{
    // Preparation owns the entire placed stack. Partial stack selection and
    // OnAdd scripts need separate instance/event owners and are refused.
    internal FalloutActorFoodAcquisition PrepareActorFoodAcquisition(FalloutFormKey actorReference,
        FalloutFormKey foodReference, int maximumCount, int level, FalloutGlobalState? globals)
    {
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (records.RuntimeFormId(actorReference) == 0x14)
            throw new InvalidOperationException("Player pickup has a separate input and event owner.");
        var actor = Actor(actorReference);
        if (!IsEnabled(actorReference) || !ActorMayEatWorldFood(actorReference, foodReference))
            throw new InvalidOperationException("Actor food source is no longer available or accessible.");
        var food = Get(foodReference);
        var item = records.GetEffective(food.Base);
        if (FalloutScriptLocals.AttachedScript(records, item) is not null)
            throw new NotSupportedException("Actor food pickup requires its item OnAdd event owner.");
        var fields = records.GetEffective(foodReference).ReadSubrecords().ToArray();
        var counts = fields.Where(field => field.Signature == "XCNT").ToArray();
        if (counts.Length > 1 || counts.Length == 1 && counts[0].Data.Length != 4)
            throw new InvalidDataException("Actor food pickup has an invalid placed count.");
        var count = counts.Length == 0 ? 1 : BinaryPrimitives.ReadInt32LittleEndian(counts[0].Data.Span);
        if (count <= 0) throw new InvalidDataException("Actor food pickup has a nonpositive placed count.");
        if (count > maximumCount) throw new NotSupportedException("Actor food pickup needs its partial placed-stack owner.");
        var health = fields.Where(field => field.Signature == "XHLP").ToArray();
        if (health.Length > 1 || health.Length == 1 && health[0].Data.Length != 4)
            throw new InvalidDataException("Actor food pickup has invalid placed condition.");
        var condition = health.Length == 0 ? (float?)null : BinaryPrimitives.ReadSingleLittleEndian(health[0].Data.Span) / 100;
        if (condition is { } value && (!float.IsFinite(value) || value is < 0 or > 1))
            throw new InvalidDataException("Actor food pickup has an invalid condition value.");
        var ownership = Ownership(foodReference);
        var variant = new FalloutItemVariant(count, condition, ownership.Owner, ownership.Global, ownership.FactionRank);
        if (actor.Inventory is null) throw new NotSupportedException("Actor food pickup needs its initialized inventory owner.");
        var inventory = Inventory(actorReference, level, globals).Contents;
        var revision = inventory.Revision;
        var used = false;
        return new(actorReference, foodReference, item.FormKey, count, () =>
        {
            if (used || actor.Inventory?.Contents != inventory || inventory.Revision != revision || food.Base != item.FormKey ||
                !IsEnabled(actorReference) || !ActorMayEatWorldFood(actorReference, foodReference) || Ownership(foodReference) != ownership ||
                FalloutScriptLocals.AttachedScript(records, item) is not null)
                throw new InvalidOperationException("Prepared actor food pickup is stale or already consumed.");
            used = true;
            var marked = false;
            using var lease = new FalloutInventoryChangeLease(() =>
            {
                if (!IsEnabled(actorReference) || !ActorMayEatWorldFood(actorReference, foodReference) || Ownership(foodReference) != ownership)
                    throw new InvalidOperationException("Actor food source changed during pickup publication.");
                food.Taken = true; marked = true;
            }, () => { if (marked) food.Taken = false; });
            inventory.AddWithPreparedOwner(records, item.FormKey, count, level, true, lease, globals, variant);
        });
    }

    // The holder must already own its inventory and be an accessible
    // container or a dead actor. Living-actor negotiation and combat remain
    // separate owners. The supplied variant is an actual current stack.
    internal FalloutActorFoodAcquisition PrepareActorFoodTransfer(FalloutFormKey actorReference,
        FalloutFormKey holderReference, FalloutFormKey itemForm, int variantIndex, int count,
        int level, FalloutGlobalState? globals)
    {
        if (actorReference == holderReference || records.RuntimeFormId(actorReference) == 0x14 ||
            records.RuntimeFormId(holderReference) == 0x14)
            throw new InvalidOperationException("Actor food transfer needs distinct non-player inventory owners.");
        if (count <= 0 || variantIndex < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var actor = Actor(actorReference);
        var holder = Get(holderReference);
        var holderType = records.GetEffective(holder.Base).Signature;
        var item = records.GetEffective(itemForm);
        if (!IsActorFood(itemForm)) throw new InvalidDataException("Transferred item is not classified as actor food.");
        if (FalloutScriptLocals.AttachedScript(records, item) is not null)
            throw new NotSupportedException("Actor food transfer requires its item OnAdd and OnDrop event owners.");
        if (actor.Inventory is null || holder.Inventory is null)
            throw new NotSupportedException("Actor food transfer needs both initialized inventory owners.");
        var ownership = Ownership(holderReference);
        bool Accessible()
        {
            if (!IsEnabled(actorReference) || IsDead(actorReference) || !CanActivate(holderReference)) return false;
            if (holderType is "NPC_" or "CREA") return IsDead(holderReference) &&
                holder.ScriptError is null && holder.ScriptStoppedFrame is null &&
                holder.Injury is { DeathInventoryGranted: true, DeathEventPending: false };
            if (holderType != "CONT") throw new InvalidDataException("Food holder has no actor or container inventory.");
            if (GetLocked(holderReference) != 0 || ownership.Global is not null) return false;
            if (ownership.Owner is not { } owner || owner == actorReference || owner == actor.Base) return true;
            if (records.GetEffective(owner).Signature == "FACT")
                return ActorFactions(actorReference).GetValueOrDefault(owner, (sbyte)-1) >= Math.Max(0, ownership.FactionRank ?? 0);
            return false;
        }
        if (!Accessible()) throw new NotSupportedException("Food holder needs its access, living actor or combat owner.");
        var source = Inventory(holderReference, level, globals).Contents;
        var destination = Inventory(actorReference, level, globals).Contents;
        var entry = source.Item(itemForm) ?? throw new InvalidOperationException("Food is absent from its holder.");
        var variants = entry.Variants ?? [new FalloutItemVariant(entry.Count)];
        if (variantIndex >= variants.Count || count > variants[variantIndex].Count)
            throw new InvalidOperationException("Selected food variant is absent or too small.");
        var selected = variants[variantIndex];
        if (selected.Owner is not null || selected.Global is not null || selected.FactionRank is not null)
            throw new NotSupportedException("Owned food variants need their transfer access owner.");
        var sourceRevision = source.Revision; var destinationRevision = destination.Revision;
        var used = false;
        return new(actorReference, holderReference, itemForm, count, () =>
        {
            if (used || actor.Inventory?.Contents != destination || holder.Inventory?.Contents != source ||
                source.Revision != sourceRevision || destination.Revision != destinationRevision ||
                Ownership(holderReference) != ownership || !Accessible())
                throw new InvalidOperationException("Prepared actor food transfer is stale or already consumed.");
            used = true;
            using var lease = new FalloutInventoryChangeLease(() =>
            {
                if (actor.Inventory?.Contents != destination || holder.Inventory?.Contents != source ||
                    Ownership(holderReference) != ownership || !Accessible())
                    throw new InvalidOperationException("Food holder changed during transfer publication.");
            }, () => { });
            source.TransferToWithPreparedOwner(destination, itemForm, count, variantIndex, lease);
        });
    }
}
