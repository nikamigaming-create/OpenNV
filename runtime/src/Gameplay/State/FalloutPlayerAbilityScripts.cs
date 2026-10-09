using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerAbilityScriptState(FalloutFormKey Spell, int EffectOrdinal,
    FalloutFormKey Effect, FalloutFormKey Script, string SpellWinner, string SpellSha256,
    string EffectWinner, string EffectSha256, string ScriptWinner, string ScriptSha256,
    long Generation, bool Active, bool Started, string? Error, IReadOnlyList<FalloutScriptEffectLocalCell> Locals, FalloutCompiledActiveEffectSnapshot? Compiled = null);
internal sealed record FalloutPlayerAbilityScriptsSnapshot(string Schema, uint Reference,
    FalloutFormKey Player, string PlayerWinner, string PlayerSha256, long LastGeneration,
    IReadOnlyList<FalloutPlayerAbilityScriptState> Effects);

// The bounded constant-ability Start lifecycle. Update/timing and Finish event
// sources remain refused, rather than being silently treated as Start-only.
internal sealed partial class FalloutPlayerAbilityScripts : IFalloutAbilityScriptLifetime
{
    internal const string Schema = "opennv-player-ability-scripts/v2";
    private sealed class Entry(FalloutAbilityScript definition, FalloutPlayerAbilityScriptState state,
        FalloutPluginRecord script, FalloutScriptEffectLocals locals, FalloutCompiledActiveEffectExecution compiled)
    {
        internal FalloutAbilityScript Definition { get; } = definition;
        internal FalloutPlayerAbilityScriptState State { get; set; } = state;
        internal FalloutPluginRecord Script { get; } = script;
        internal FalloutScriptEffectLocals Locals { get; } = locals;
        internal FalloutCompiledActiveEffectExecution Compiled { get; } = compiled;
    }
    private readonly FalloutPluginStack _records;
    private readonly Func<IReadOnlyList<FalloutFormKey>> _selection;
    private readonly Func<FalloutCondition, float> _condition;
    private readonly FalloutAbilityModifiers _declarations;
    private readonly FalloutPlayerActorValueSource _player;
    private readonly Dictionary<(FalloutFormKey Spell, int Ordinal), Entry> _entries = [];
    private Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt>? _execute;
    private IReadOnlyList<FalloutFormKey> _synchronizingSelection = [];
    private bool _synchronizing;
    private long _generation;

    internal FalloutPlayerAbilityScripts(FalloutPluginStack records, Func<IReadOnlyList<FalloutFormKey>> selection,
        Func<FalloutCondition, float> condition, FalloutPlayerAbilityScriptsSnapshot? restore = null)
    {
        _records = records; _selection = selection; _condition = condition; _declarations = new(records);
        _player = FalloutPlayerActorValueSource.Read(records);
        if (restore is null) return;
        Validate(restore);
        if (restore.Player != _player.Player || restore.PlayerWinner != _player.PlayerWinner || restore.PlayerSha256 != _player.PlayerSha256)
            throw new InvalidDataException("Saved active effects differ from the winning engine-player source.");
        var savedKeys = restore.Effects.Select(effect => (effect.Spell, effect.EffectOrdinal)).ToHashSet();
        if (_selection().Distinct().SelectMany(form => _declarations.Scripts(form))
            .Any(effect => !savedKeys.Contains((effect.Spell, effect.EffectOrdinal))))
            throw new InvalidDataException("Current player ability state is missing a selected scripted-effect instance.");
        _generation = restore.LastGeneration;
        foreach (var saved in restore.Effects)
        {
            var definition = _declarations.Scripts(saved.Spell).SingleOrDefault(effect => effect.EffectOrdinal == saved.EffectOrdinal) ??
                throw new InvalidDataException("Saved active effect has no source effect ordinal.");
            var entry = Build(definition, saved.Generation, saved);
            if (!_entries.TryAdd((saved.Spell, saved.EffectOrdinal), entry))
                throw new InvalidDataException("Saved active effects duplicate an instance identity.");
        }
    }

    internal void BindExecutor(Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (_execute is not null) throw new InvalidOperationException("Player active-effect executor is already bound.");
        _execute = execute;
    }

    public void Synchronize()
    {
        var selection = _selection().Distinct().ToArray();
        if (_synchronizing)
        {
            if (!selection.SequenceEqual(_synchronizingSelection))
                throw new NotSupportedException("Active-effect source selection changed during its script invocation.");
            return;
        }
        var execute = _execute ?? throw new NotSupportedException("Player active effects have no genuine script executor.");
        _synchronizing = true; _synchronizingSelection = selection;
        try
        {
            var candidates = selection.SelectMany(form => _declarations.Scripts(form)).ToArray();
            // Resolve every winning definition before publishing any new effect.
            foreach (var definition in candidates)
                if (!_entries.ContainsKey((definition.Spell, definition.EffectOrdinal)))
                {
                    var generation = checked(_generation + 1);
                    var entry = Build(definition, generation, null);
                    _entries.Add((definition.Spell, definition.EffectOrdinal), entry); _generation = generation;
                }
            var active = candidates.Where(effect => FalloutCondition.AllPass(effect.Conditions, _condition))
                .Select(effect => (effect.Spell, effect.EffectOrdinal)).ToHashSet();
            foreach (var entry in _entries.Values)
                if (entry.State.Active && !active.Contains((entry.State.Spell, entry.State.EffectOrdinal)))
                    entry.State = entry.State with { Active = false };
            foreach (var definition in candidates)
            {
                var key = (definition.Spell, definition.EffectOrdinal);
                if (!active.Contains(key)) continue;
                var entry = _entries[key];
                if (entry.State.Error is { } retained) throw new NotSupportedException(retained);
                if (!entry.State.Active && entry.State.Started)
                {
                    var generation = checked(_generation + 1);
                    entry = Build(definition, generation, null);
                    _entries[key] = entry; _generation = generation;
                }
                entry.State = entry.State with { Active = true };
                if (entry.State.Started) continue;
                try
                {
                    entry.Compiled.ExecuteStart(execute);
                    entry.State = entry.State with { Started = true, Locals = entry.Locals.Capture(), Compiled = entry.Compiled.Capture() };
                }
                catch (Exception error)
                {
                    entry.State = entry.State with
                    {
                        Error = $"Active effect {definition.Spell}/{definition.EffectOrdinal} Start failed: {error.Message}",
                        Locals = entry.Locals.Capture(), Compiled = entry.Compiled.Capture()
                    };
                    throw;
                }
            }
        }
        finally { _synchronizingSelection = []; _synchronizing = false; }
    }

    public void RequireStarted(FalloutFormKey spell, IReadOnlyList<FalloutAbilityScript> scripts)
    {
        Synchronize();
        if (!_selection().Contains(spell)) throw new InvalidDataException("Ability query does not belong to the live constant-effect selection.");
        foreach (var definition in scripts)
        {
            if (!_entries.TryGetValue((spell, definition.EffectOrdinal), out var entry) || entry.Definition != definition &&
                (entry.Definition.Effect != definition.Effect || entry.Definition.Script != definition.Script))
                throw new InvalidDataException("Ability query differs from its owned effect/script identity.");
            if (entry.State.Error is { } retained) throw new NotSupportedException(retained);
            if (!_synchronizing && entry.State.Active && !entry.State.Started)
                throw new NotSupportedException("Ability query reached an incomplete ScriptEffectStart.");
        }
    }

    private Entry Build(FalloutAbilityScript definition, long generation, FalloutPlayerAbilityScriptState? saved) =>
        BuildCompiled(definition, generation, saved);

    internal FalloutPlayerAbilityScriptsSnapshot Capture()
    {
        if (_synchronizing) throw new NotSupportedException("Saving an active-effect invocation requires its continuation owner.");
        return new(Schema, FalloutPlayerActorValues.PlayerReference, _player.Player, _player.PlayerWinner, _player.PlayerSha256,
            _generation, _entries.Values.OrderBy(entry => entry.State.Generation)
                .Select(entry => entry.State with { Locals = entry.Locals.Capture(), Compiled = entry.Compiled.Capture() }).ToArray());
    }
    internal static void ValidateSource(FalloutPluginStack records, FalloutPlayerAbilityScriptsSnapshot state)
    {
        // Constructor source/cell admission does not execute Start or evaluate
        // live conditions. The real restored driver owns selected-effect parity.
        _ = new FalloutPlayerAbilityScripts(records,
            () => state.Effects.Where(effect => effect.Active).Select(effect => effect.Spell).Distinct().ToArray(),
            _ => throw new InvalidOperationException("Source validation cannot execute live ability conditions."), state);
    }

    internal static void Validate(FalloutPlayerAbilityScriptsSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Schema != Schema || state.Reference != FalloutPlayerActorValues.PlayerReference || state.Player.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(state.PlayerWinner) || !ValidHash(state.PlayerSha256) || state.LastGeneration < 0 || state.Effects is null ||
            state.Effects.Any(effect => effect is null || effect.Spell.ObjectId == 0 || effect.Effect.ObjectId == 0 || effect.Script.ObjectId == 0 ||
                effect.EffectOrdinal < 0 || effect.Generation <= 0 || effect.Generation > state.LastGeneration ||
                string.IsNullOrWhiteSpace(effect.SpellWinner) || string.IsNullOrWhiteSpace(effect.EffectWinner) || string.IsNullOrWhiteSpace(effect.ScriptWinner) ||
                !ValidHash(effect.SpellSha256) || !ValidHash(effect.EffectSha256) || !ValidHash(effect.ScriptSha256) || effect.Locals is null ||
                effect.Error is { Length: 0 } || effect.Started && effect.Error is not null ||
                effect.Active && !effect.Started && effect.Error is null ||
                effect.Locals.Where((cell, index) => cell is null || cell.Index == 0 || cell.Ordinal != index).Any()) ||
            state.Effects.Select(effect => (effect.Spell, effect.EffectOrdinal)).Distinct().Count() != state.Effects.Count ||
            state.Effects.Select(effect => effect.Generation).Distinct().Count() != state.Effects.Count)
            throw new InvalidDataException("Saved player active-effect identity/lifecycle is invalid.");
        foreach (var effect in state.Effects) RequireCompiledState(effect);
    }
    private static string Hash(FalloutPluginRecord source) => Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant();
    private static bool ValidHash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
