using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutActorBaseFlagState? _actorBaseFlags;
    internal FalloutActorBaseFlagState ActorBaseFlags => _actorBaseFlags ??= new(records);
    private readonly Dictionary<FalloutFormKey, bool> _sourceEssential = [];

    private bool SourceEssential(FalloutReferenceInstance actor)
    {
        var source = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base),
            (ushort)FalloutActorTemplateGroup.BaseData, actor.Templates);
        if (_sourceEssential.TryGetValue(source.FormKey, out var essential)) return essential;
        var fields = source.ReadSubrecords().Where(field => field.Signature == "ACBS").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 24)
            throw new InvalidDataException("Actor base flags have invalid ACBS data.");
        essential = ((FalloutActorBaseFlags)BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span) &
            FalloutActorBaseFlags.Essential) != 0;
        _sourceEssential.Add(source.FormKey, essential);
        return essential;
    }

    internal bool IsEssential(FalloutFormKey reference)
    {
        var actor = Actor(reference);
        return actor.PlayerTeammate && !PlayerHardcore || actor.ReferenceEssential ||
            ActorBaseFlags.Essential(actor.Base, SourceEssential(actor));
    }

    internal bool IsActorRefEssential(FalloutFormKey reference) => Actor(reference).ReferenceEssential;

    internal void SetActorRefEssential(FalloutFormKey reference, bool essential) =>
        Actor(reference).ReferenceEssential = essential;

    private bool EssentialProtection(FalloutFormKey reference) => IsEssential(reference) &&
        records.IniSettings.Boolean("bEssentialTakeNoDamage:GamePlay");

    private FalloutActorValue BeginEssentialKnockout(FalloutReferenceInstance actor, FalloutActorValue health,
        ref FalloutActorInjury injury)
    {
        var duration = records.NumericSettings.Float("fEssentialDeathTime");
        if (!float.IsFinite(duration) || duration < 0)
            throw new InvalidDataException("Essential knockout duration must be finite and nonnegative.");
        var restored = RestoreEssentialDamage(actor, health, ref injury);
        actor.EssentialKnockout = new(duration);
        return restored;
    }

    private FalloutActorValue RestoreEssentialDamage(FalloutReferenceInstance actor, FalloutActorValue health,
        ref FalloutActorInjury injury)
    {
        var restored = FalloutActorValueCommands.Apply(health, FalloutActorValueOperation.Restore,
            Math.Max(0, -health.Damage + 1), health.Current);
        if (restored.Current <= 0)
            throw new NotSupportedException("Essential recovery requires a positive living health baseline.");
        var limbs = new Dictionary<byte, float>(injury.LimbDamage);
        foreach (var group in BodyParts(actor.Reference).Parts.Where(part => part.HealthPercent > 0 &&
            FalloutActorValueNames.IsLimb((FalloutActorValueId)part.ActorValue)).GroupBy(part => part.ActorValue))
        {
            if (!group.Any(part => limbs.GetValueOrDefault(part.Type) > 0)) continue;
            var parts = group.ToArray();
            if (parts.Length != 1)
                throw new NotSupportedException("Essential limb recovery has ambiguous source anatomy.");
            var part = parts[0];
            limbs[part.Type] = Math.Max(0, limbs.GetValueOrDefault(part.Type) - health.Base * part.HealthPercent / 100);
        }
        if (actor.ActorValues.TryGetValue("fatigue", out var fatigue))
            actor.ActorValues["fatigue"] = FalloutActorValueCommands.Apply(fatigue, FalloutActorValueOperation.Restore,
                Math.Max(0, -fatigue.Damage + 1), fatigue.Current);
        injury = injury with { LimbDamage = limbs };
        return restored;
    }

    private FalloutActorValue EssentialSuppressedDeath(FalloutReferenceInstance actor, FalloutActorValue health,
        ref FalloutActorInjury injury)
    {
        if (!actor.Unconscious && !actor.Restrained) return BeginEssentialKnockout(actor, health, ref injury);
        var fraction = records.NumericSettings.Float("fEssentialHealthPercentReGain");
        if (!float.IsFinite(fraction) || fraction <= 0)
            throw new NotSupportedException("Nonpositive essential health regain requires its living zero-health lifecycle.");
        var target = Math.Truncate(health.Base) * fraction;
        var changed = health with { Damage = Math.Min(0, (float)(health.Damage + target - health.Current)) };
        return changed.IsFinite ? changed : throw new InvalidDataException("Essential health regain exceeds finite storage.");
    }

    internal void AdvanceEssentialKnockout(FalloutFormKey reference, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var actor = Actor(reference);
        if (actor.EssentialKnockout is not { RecoveryRequested: false } state || !actor.KnockedDown || actor.Unconscious || actor.Restrained) return;
        if (actor.PlayerTeammate && PlayerInCombat()) return;
        var remaining = state.RemainingSeconds - seconds;
        if (!float.IsFinite(remaining)) throw new InvalidDataException("Essential knockout clock exceeds finite storage.");
        if (remaining <= 0)
        {
            var injury = actor.Injury!;
            actor.ActorValues["health"] = RestoreEssentialDamage(actor, Health(reference), ref injury);
            actor.Injury = injury;
            actor.EssentialKnockout = state with { RemainingSeconds = remaining, RecoveryRequested = true };
        }
        else actor.EssentialKnockout = state with { RemainingSeconds = remaining };
    }

    // The public query is the process unconscious predicate, not the raw actor
    // life state or the ordinary explosion-ragdoll flag. Get-up is separate.
    internal int GetKnockedState(FalloutFormKey reference) =>
        Actor(reference).EssentialKnockout is { Unconscious: true } ? 1 : 0;
}
