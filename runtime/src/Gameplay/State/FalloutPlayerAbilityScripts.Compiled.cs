using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerAbilityScripts
{
    private Entry BuildCompiled(FalloutAbilityScript definition, long generation, FalloutPlayerAbilityScriptState? saved)
    {
        var spell = _records.GetEffective(definition.Spell);
        var effect = _records.GetEffective(definition.Effect);
        var script = _records.GetEffective(definition.Script);
        if (spell.Signature != "SPEL")
            throw new NotSupportedException("Scripted constant enchantments require their equipped-item instance lifecycle.");
        if (effect.Signature != "MGEF" || script.Signature != "SCPT")
            throw new InvalidDataException("Active-effect source links are not MGEF/SCPT.");
        var initial = new FalloutPlayerAbilityScriptState(definition.Spell, definition.EffectOrdinal,
            definition.Effect, definition.Script, spell.Plugin.Name, Hash(spell), effect.Plugin.Name, Hash(effect),
            script.Plugin.Name, Hash(script), generation, false, false, null, [], null);
        if (saved is not null)
        {
            var savedIdentity = saved with { Active = false, Started = false, Error = null, Locals = initial.Locals, Compiled = null };
            if (savedIdentity != initial)
                throw new InvalidDataException("Saved active-effect source/order differs from winning owned bytes.");
            if (saved.Compiled is null)
                throw new InvalidDataException("Current active effect lost its compiled event-list/cursor authority.");
        }
        var locals = new FalloutScriptEffectLocals(script, saved?.Locals);
        var compiled = new FalloutCompiledActiveEffectExecution(_records,
            _records.RuntimeFormKey(FalloutPlayerActorValues.PlayerReference), definition, generation, script, locals, saved?.Compiled);
        compiled.RequireLifecycle(saved?.Started ?? false, saved?.Error);
        var state = saved ?? initial with { Compiled = compiled.Capture(), Locals = locals.Capture() };
        return new(definition, state, script, locals, compiled);
    }

    private static void RequireCompiledState(FalloutPlayerAbilityScriptState effect)
    {
        var compiled = effect.Compiled ?? throw new InvalidDataException("Current active effect has no compiled lifetime state.");
        FalloutCompiledActiveEffectExecution.Validate(compiled);
        if (compiled.Target.ObjectId != FalloutPlayerActorValues.PlayerReference || compiled.Spell != effect.Spell ||
            compiled.EffectOrdinal != effect.EffectOrdinal || compiled.Effect != effect.Effect ||
            compiled.Script != effect.Script || compiled.Generation != effect.Generation ||
            compiled.Winner != effect.ScriptWinner || compiled.RecordSha256 != effect.ScriptSha256 ||
            effect.Started && compiled.Events.Any(row => !row.Attempted || !row.Cursor.Completed ||
                row.Receipt?.Disposition != "completed" || row.Failure is not null) ||
            !effect.Started && effect.Error is null && compiled.Events.Any(row => row.Attempted || row.Failure is not null) ||
            effect.Error is not null && !compiled.Events.Any(row => row.Attempted && row.Failure is not null))
            throw new InvalidDataException("Saved active-effect lifetime differs from its compiled instance/event authority.");
    }
}
