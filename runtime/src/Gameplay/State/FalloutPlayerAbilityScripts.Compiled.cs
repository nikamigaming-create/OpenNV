using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerAbilityScripts
{
    private Entry BuildCompiled(FalloutAbilityScript definition, long generation, FalloutPlayerAbilityScriptState? saved)
    {
        var source = FalloutScriptedEffectSource.Read(_records, definition);
        if (source.Origin == FalloutScriptedEffectOrigin.DeliveredSpell)
            throw new NotSupportedException("Timed spell still requires its genuine casting/delivery application owner.");
        var script = _records.GetEffective(definition.Script);
        var initial = new FalloutPlayerAbilityScriptState(definition.Spell, definition.EffectOrdinal,
            definition.Effect, definition.Script, source.ItemWinner, source.ItemSha256, source.EffectWinner, source.EffectSha256,
            script.Plugin.Name, Hash(script), generation, false, false, null, [], null, null, null);
        if (saved is not null)
        {
            var savedIdentity = saved with
            {
                Active = false,
                Started = false,
                Error = null,
                Locals = initial.Locals,
                Compiled = null,
                Timeline = null,
                Consumption = null
            };
            if (savedIdentity != initial || saved.Compiled is null || saved.Timeline is null)
                throw new InvalidDataException("Saved effect differs from its complete winning source/timeline/cell owner.");
            RequireConsumptionSource(saved.Consumption);
        }
        var lifetime = new FalloutScriptedActiveEffect(_records,
            _records.RuntimeFormKey(FalloutPlayerActorValues.PlayerReference), definition, generation,
            saved?.Timeline?.LastClockMutation ?? Clock.LastConsumedMutation, NextGeneration, Execute,
            saved?.Timeline, saved?.Locals, saved?.Compiled);
        var entry = new Entry(definition, saved ?? initial, lifetime);
        entry.State = CaptureEntry(entry);
        return entry;
    }

    private static void RequireCompiledState(FalloutPlayerAbilityScriptState effect)
    {
        var compiled = effect.Compiled ?? throw new InvalidDataException("Current active effect has no compiled event list.");
        var timeline = effect.Timeline ?? throw new InvalidDataException("Current active effect has no real elapsed/lifecycle owner.");
        var lifecycle = compiled.Lifetime ?? throw new InvalidDataException("Current active effect has no event-list lifecycle.");
        FalloutCompiledActiveEffectExecution.Validate(compiled);
        FalloutScriptedActiveEffect.Validate(timeline); ValidateConsumption(effect.Consumption);
        if (compiled.Target.ObjectId != FalloutPlayerActorValues.PlayerReference || compiled.Spell != effect.Spell ||
            compiled.EffectOrdinal != effect.EffectOrdinal || compiled.Effect != effect.Effect ||
            compiled.Script != effect.Script || compiled.Generation != effect.Generation ||
            timeline.EventListGeneration != effect.Generation || timeline.InstanceGeneration > effect.Generation ||
            compiled.Winner != effect.ScriptWinner || compiled.RecordSha256 != effect.ScriptSha256 ||
            effect.Started != timeline.Started || effect.Error != timeline.Failure ||
            effect.Active != (timeline.Applied && !timeline.Expired && !timeline.Removed) ||
            lifecycle.Started != timeline.Started || lifecycle.Finished != timeline.FinishApplied ||
            effect.Error is null && lifecycle.Failure is not null ||
            effect.Consumption is { } use && (use.Item != effect.Spell || use.ItemSha256 != effect.SpellSha256 ||
                !ConsumptionOwns(use, timeline.InstanceGeneration, effect.EffectOrdinal)))
            throw new InvalidDataException("Saved effect differs from its actual source instance/event-list/application prefix.");
    }

}
