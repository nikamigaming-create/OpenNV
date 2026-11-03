using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutMineState RequireMine(FalloutFormKey reference, FalloutMineSource source)
    {
        var instance = Get(reference);
        if (records.GetEffective(reference).Signature != "PGRE" || instance.Base != source.Projectile.Form)
            throw new InvalidDataException("Placed mine has no actual source projectile reference.");
        instance.Mine ??= FalloutMineState.Initial(source);
        instance.Mine.Validate(source);
        return instance.Mine;
    }

    internal bool MineActorEligible(FalloutFormKey reference, FalloutFormKey actor)
    {
        if (!MineCandidateEligible(actor)) return false;

        // An authored mine has no deploying actor. Native ownership excludes
        // members of its faction; this is independent of faction hostility.
        var ownership = Ownership(reference);
        if (ownership.Owner is not { } owner) return true;
        if (records.GetEffective(owner).Signature != "FACT")
            throw new NotSupportedException("Placed mine non-faction ownership requires its native trigger eligibility owner.");
        return !ActorFactions(actor).TryGetValue(owner, out var rank) || rank < 0;
    }

    private bool MineCandidateEligible(FalloutFormKey actor)
    {
        var basis = records.GetEffective(ActorBase(actor));
        if (basis.Signature == "CREA")
        {
            var source = FalloutActorTemplateOwner.Resolve(records, basis, 64, Actor(actor).Templates);
            var acbs = source.ReadSubrecords().Single(field => field.Signature == "ACBS").Data;
            if (acbs.Length != 24) throw new InvalidDataException("Mine candidate creature flags have an invalid extent.");
            if ((BinaryPrimitives.ReadUInt32LittleEndian(acbs.Span) & 0x20) != 0) return false;
        }
        else if (basis.Signature != "NPC_") throw new InvalidDataException("Mine candidate is not an actor.");
        else if (FalloutRaceProperties.IsChild(records.GetEffective(ActorRace(actor)))) return false;

        return true;
    }

    internal void TakeMine(FalloutFormKey reference, FalloutMineSource source, FalloutPlayerInventory player,
        int level, FalloutGlobalState globals)
    {
        var instance = Get(reference);
        var mine = RequireMine(reference, source);
        if (!CanActivate(reference) || !source.Projectile.CanBePickedUp ||
            source.Projectile.CanBeDisabled && mine.Phase != FalloutMinePhase.Disabled ||
            mine.Phase == FalloutMinePhase.Detonated || mine.Error is not null)
            throw new InvalidOperationException("Mine is not available for pickup.");
        var weapon = source.PickupWeapon(records);
        RequirePlayerItemAcquisition(records, records.GetEffective(weapon), records.RuntimeFormKey(0x14));
        var ownership = Ownership(reference);
        var variant = new FalloutItemVariant(1, Owner: ownership.Owner,
            Global: ownership.Global, FactionRank: ownership.FactionRank);
        var changed = false;
        using var lease = new FalloutInventoryChangeLease(() =>
        {
            if (!CanActivate(reference) || instance.Mine != mine || Ownership(reference) != ownership)
                throw new InvalidOperationException("Mine changed during pickup.");
            instance.Taken = true;
            changed = true;
        }, () => { if (changed) instance.Taken = false; });
        player.AddWithPreparedOwner(records, weapon, 1, level, false, lease, globals, variant);
    }
}
