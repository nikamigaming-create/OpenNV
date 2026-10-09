using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerAbilityScriptState(FalloutFormKey Spell, int EffectOrdinal,
    FalloutFormKey Effect, FalloutFormKey Script, string SpellWinner, string SpellSha256,
    string EffectWinner, string EffectSha256, string ScriptWinner, string ScriptSha256,
    long Generation, bool Active, bool Started, string? Error, IReadOnlyList<FalloutScriptEffectLocalCell> Locals, FalloutCompiledActiveEffectSnapshot? Compiled = null, FalloutScriptedEffectTimeline? Timeline = null,
    FalloutScriptedEffectConsumption? Consumption = null);
internal sealed record FalloutPlayerAbilityScriptsSnapshot(string Schema, uint Reference,
    FalloutFormKey Player, string PlayerWinner, string PlayerSha256, long LastGeneration,
    IReadOnlyList<FalloutPlayerAbilityScriptState> Effects, FalloutScriptedEffectClockSnapshot? Clock = null,
    long Retired = 0, FalloutPlayerAbilityScriptState? LastRetired = null,
    long Consumptions = 0, FalloutScriptedEffectConsumption? LastConsumption = null, string? Failure = null);
// The shared player source-script lifetime for constant selection and actual
// consumed applications. Script cells, time and cold prefixes have one owner.
internal sealed partial class FalloutPlayerAbilityScripts : IFalloutAbilityScriptLifetime
{
    internal const string Schema = "opennv-player-ability-scripts/v3";
    private sealed class Entry(FalloutAbilityScript definition, FalloutPlayerAbilityScriptState state,
        FalloutScriptedActiveEffect lifetime)
    {
        internal FalloutAbilityScript Definition { get; } = definition;
        internal FalloutPlayerAbilityScriptState State { get; set; } = state;
        internal FalloutScriptedActiveEffect Lifetime { get; } = lifetime;
        internal FalloutScriptEffectLocals Locals => Lifetime.Locals;
        internal FalloutCompiledActiveEffectExecution Compiled => Lifetime.Compiled;
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
        _generation = restore.LastGeneration; _restoreClock = restore.Clock;
        _retired = restore.Retired; _lastRetired = restore.LastRetired;
        _consumptions = restore.Consumptions; _lastConsumption = restore.LastConsumption; _sourceFailure = restore.Failure;
        foreach (var saved in restore.Effects)
        {
            var definition = SavedDefinition(saved);
            var entry = Build(definition, saved.Generation, saved);
            var source = FalloutScriptedEffectSource.Read(records, definition);
            if (!source.Constant && _transients.Values.Any(value => value.Definition.Spell == definition.Spell &&
                value.Definition.EffectOrdinal == definition.EffectOrdinal && !value.Lifetime.Retired))
                throw new NotSupportedException("Saved concurrent scripted Aid still requires its actual stacking/replacement owner.");
            var added = source.Constant ? _entries.TryAdd((saved.Spell, saved.EffectOrdinal), entry) :
                _transients.TryAdd(entry.Lifetime.InstanceGeneration, entry);
            if (!added) throw new InvalidDataException("Saved active effects duplicate an actual source instance.");
        }
        RequireConsumptionSource(_lastConsumption);
        if (_lastRetired is { } retired) _ = Build(SavedDefinition(retired), retired.Generation, retired);
    }

    internal void BindExecutor(Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (_execute is not null) throw new InvalidOperationException("Player active-effect executor is already bound.");
        _execute = execute;
    }

    public void Synchronize() => SynchronizeLifecycle();
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
        if (_synchronizing || _applying) throw new NotSupportedException("Saving an active-effect invocation/application requires its continuation owner.");
        return new(Schema, FalloutPlayerActorValues.PlayerReference, _player.Player, _player.PlayerWinner, _player.PlayerSha256,
            _generation, EveryEntry.OrderBy(entry => entry.Lifetime.InstanceGeneration).Select(CaptureEntry).ToArray(),
            Clock.Capture(), _retired, _lastRetired, _consumptions, _lastConsumption, _sourceFailure);
    }
    internal static void ValidateSource(FalloutPluginStack records, FalloutPlayerAbilityScriptsSnapshot state)
    {
        // Constructor source/cell admission does not execute Start or evaluate
        // live conditions. The real restored driver owns selected-effect parity.
        _ = new FalloutPlayerAbilityScripts(records,
            () => state.Effects.Where(effect => records.GetEffective(effect.Spell).Signature == "SPEL" && effect.Active).Select(effect => effect.Spell).Distinct().ToArray(),
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
                effect.Error is { Length: 0 } || effect.Timeline is null ||
                effect.Active && !effect.Started && effect.Error is null && effect.Consumption is null ||
                effect.Locals.Where((cell, index) => cell is null || cell.Index == 0 || cell.Ordinal != index).Any()) ||
            state.Clock is null || state.Retired < 0 || (state.Retired == 0) != (state.LastRetired is null) ||
            state.Consumptions < 0 || (state.Consumptions == 0) != (state.LastConsumption is null) ||
            state.Failure is { Length: 0 } || state.LastConsumption is { } consumption && consumption.Ordinal != state.Consumptions ||
            state.Effects.Select(effect => effect.Timeline!.InstanceGeneration).Distinct().Count() != state.Effects.Count ||
            state.Effects.Select(effect => effect.Generation).Distinct().Count() != state.Effects.Count)
            throw new InvalidDataException("Saved player active-effect identity/lifecycle is invalid.");
        FalloutScriptedEffectClock.Validate(state.Clock);
        ValidateConsumption(state.LastConsumption);
        if (state.LastConsumption?.Failure != state.Failure)
            throw new InvalidDataException("Saved player effect owner lost its actual application failure.");
        foreach (var effect in state.Effects) RequireCompiledState(effect);
        if (state.LastRetired is { } retired)
        {
            RequireCompiledState(retired);
            if (retired.Generation > state.LastGeneration || retired.Timeline is not { } timeline ||
                !(timeline.Expired || timeline.Removed) || timeline.Started && !timeline.FinishApplied || retired.Error is not null)
                throw new InvalidDataException("Saved effect retirement lost its genuine final source closure.");
        }
        if (state.Effects.Any(effect => effect.Timeline!.LastClockMutation > state.Clock.LastConsumedMutation))
            throw new InvalidDataException("Saved effect instance is ahead of its actual consumed gameplay clock.");
    }
    private static string Hash(FalloutPluginRecord source) => Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant();
    private static bool ValidHash(string value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
