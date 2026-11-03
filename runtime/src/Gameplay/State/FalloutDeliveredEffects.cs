using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutDeliveredEffectState(long Generation, FalloutFormKey Target, FalloutFormKey Caster,
    FalloutFormKey Source, string SourceHash, int Ordinal, FalloutFormKey Effect, string EffectHash,
    FalloutFormKey Script, string ScriptHash, double Duration, double Elapsed, IReadOnlyList<FalloutScriptEffectLocalCell> Locals);

internal sealed record FalloutDeliveredEffectsSnapshot(long LastGeneration, IReadOnlyList<FalloutDeliveredEffectState> Active);
internal sealed record FalloutDeliveredEffectDelivery(FalloutFormKey Target, FalloutFormKey Caster, FalloutIngestible Source);
internal enum FalloutDeliveredTargetStatus { Resident, Unloaded, Retired }
internal enum FalloutDeliveredEffectPhase { Start, Finish }
internal sealed record FalloutDeliveredEffectInvocation(FalloutDeliveredEffectState State, FalloutPluginRecord Script,
    FalloutGameModeProgram Program, FalloutScriptEffectLocals Locals, FalloutDeliveredEffectPhase Phase);

// The campaign owns elapsed time and invocation order independently of actor
// scene residency. A saved activation has completed Start and has not run Finish.
internal sealed class FalloutDeliveredEffects
{
    private sealed record Definition(FalloutPluginRecord Script, string ScriptHash, FalloutGameModeProgram? Start, FalloutGameModeProgram? Finish);

    private sealed class Activation(FalloutDeliveredEffectState state, Definition definition, FalloutScriptEffectLocals locals)
    {
        internal FalloutDeliveredEffectState State { get; set; } = state;
        internal Definition Definition { get; } = definition;
        internal FalloutScriptEffectLocals Locals { get; } = locals;
    }

    private readonly FalloutPluginStack _records;
    private readonly Action<FalloutFormKey, FalloutPluginRecord, FalloutGameModeProgram, FalloutScriptEffectLocals> _execute;
    private readonly Dictionary<FalloutFormKey, Definition> _scripts = [];
    private readonly Dictionary<FalloutFormKey, FalloutIngestible> _sources = [];
    private readonly List<Activation> _active = [];
    private Action<IReadOnlyList<FalloutDeliveredEffectInvocation>>? _preflight;
    private Func<FalloutFormKey, FalloutDeliveredTargetStatus>? _targetStatus;
    private long _generation;
    private bool _invoking;
    private string? _failure;

    internal FalloutDeliveredEffects(FalloutPluginStack records,
        Action<FalloutFormKey, FalloutPluginRecord, FalloutGameModeProgram, FalloutScriptEffectLocals> execute,
        FalloutDeliveredEffectsSnapshot? restore)
    {
        _records = records;
        _execute = execute;
        if (restore is null) return;
        Validate(restore);
        _generation = restore.LastGeneration;
        foreach (var saved in restore.Active)
        {
            var source = Source(saved.Source);
            var effect = source.Effects.SingleOrDefault(effect => effect.Index == saved.Ordinal) ??
                throw new InvalidDataException("Saved delivered effect ordinal is absent from its source.");
            var activation = Build(saved.Target, saved.Caster, source, effect);
            var expected = activation.State with { Generation = saved.Generation, Elapsed = saved.Elapsed, Locals = saved.Locals };
            if (saved != expected) throw new InvalidDataException("Saved delivered effect differs from its winning source.");
            _active.Add(new(saved, activation.Definition, new(activation.Definition.Script, saved.Locals)));
        }
    }

    // Preflight must validate the commands/resources of all lifecycle blocks
    // without executing them. Restored presentations rebind separately.
    internal void BindPreflight(Action<IReadOnlyList<FalloutDeliveredEffectInvocation>> prepare,
        Func<FalloutFormKey, FalloutDeliveredTargetStatus> targetStatus,
        Action<IReadOnlyList<FalloutDeliveredEffectState>> validateRestored)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(targetStatus);
        ArgumentNullException.ThrowIfNull(validateRestored);
        if (_preflight is not null) throw new InvalidOperationException("Delivered script preflight is already bound.");
        foreach (var active in _active) RequireTarget(active.State.Target, targetStatus);
        if (_active.Count != 0)
        {
            var finishes = new List<FalloutDeliveredEffectInvocation>();
            foreach (var active in _active) AddInvocation(finishes, active, active.Definition.Finish, FalloutDeliveredEffectPhase.Finish);
            prepare(finishes);
            validateRestored(_active.Select(active => active.State).ToArray());
        }
        _targetStatus = targetStatus;
        _preflight = prepare;
    }

    internal Action Prepare(FalloutFormKey target, FalloutFormKey caster, FalloutIngestible source) =>
        Prepare([new(target, caster, source)]);

    internal Action Prepare(IReadOnlyList<FalloutDeliveredEffectDelivery> deliveries)
    {
        RequireAvailable();
        var targetStatus = _targetStatus ?? throw new NotSupportedException("Delivered scripts have no target lifetime owner.");
        if (deliveries.Count == 0 || deliveries.Any(delivery => delivery.Source.Effects.Count == 0))
            throw new InvalidDataException("Delivered script selection is empty.");
        var generation = _generation;
        var effects = deliveries.SelectMany(delivery =>
        {
            var source = delivery.Source;
            RequireTarget(delivery.Target, targetStatus);
            var winner = Source(source.Form);
            if (source.Hash != winner.Hash) throw new InvalidDataException("Delivered enchantment differs from the winning source.");
            return source.Effects.OrderBy(effect => effect.Index).Select(selected =>
            {
                var actual = winner.Effects.SingleOrDefault(effect => effect.Index == selected.Index) ??
                    throw new InvalidDataException("Delivered effect ordinal is absent from its source.");
                if (actual.Form != selected.Form || actual.Hash != selected.Hash)
                    throw new InvalidDataException("Delivered effect differs from the winning source.");
                var activation = Build(delivery.Target, delivery.Caster, winner, actual);
                activation.State = activation.State with { Generation = checked(++generation) };
                return activation;
            });
        }).ToArray();
        RequireNoOverlap(effects);
        var starts = new List<FalloutDeliveredEffectInvocation>();
        var finishes = new List<FalloutDeliveredEffectInvocation>();
        foreach (var effect in effects)
        {
            AddInvocation(starts, effect, effect.Definition.Start, FalloutDeliveredEffectPhase.Start);
            AddInvocation(finishes, effect, effect.Definition.Finish, FalloutDeliveredEffectPhase.Finish);
        }
        var lifecycle = starts.Concat(finishes).ToArray();
        Preflight(lifecycle);
        var baseline = _generation;
        var consumed = false;
        return () =>
        {
            RequireAvailable();
            if (consumed || _generation != baseline) throw new InvalidOperationException("Prepared delivered effect is stale or already consumed.");
            RequireNoOverlap(effects);
            foreach (var effect in effects) RequireTarget(effect.State.Target, targetStatus);
            consumed = true;
            Preflight(lifecycle);
            Invoke(starts);
            _generation = generation;
            _active.AddRange(effects);
        };
    }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        RequireAvailable();
        if (seconds == 0 || _active.Count == 0) return;
        var targetStatus = _targetStatus ?? throw new NotSupportedException("Delivered scripts have no restored target lifetime owner.");
        foreach (var active in _active) RequireTarget(active.State.Target, targetStatus);
        var invocations = new List<FalloutDeliveredEffectInvocation>();
        var expired = _active.Where(active => active.State.Elapsed + seconds >= active.State.Duration)
            .OrderBy(active => active.State.Duration - active.State.Elapsed).ThenBy(active => active.State.Generation).ToArray();
        foreach (var active in expired)
        {
            AddInvocation(invocations, active, active.Definition.Finish, FalloutDeliveredEffectPhase.Finish);
        }
        Preflight(invocations);
        Invoke(invocations);
        foreach (var active in expired) _active.Remove(active);
        foreach (var active in _active) active.State = active.State with { Elapsed = active.State.Elapsed + seconds };
    }

    internal FalloutDeliveredEffectsSnapshot? Capture()
    {
        RequireAvailable();
        if (_active.Count != 0 && _preflight is null)
            throw new NotSupportedException("Active delivered effects have not rebound their restored command and presentation owners.");
        return _generation == 0 ? null : new(_generation,
            _active.Select(effect => effect.State with { Locals = effect.Locals.Capture() }).ToArray());
    }

    internal static void Validate(FalloutDeliveredEffectsSnapshot snapshot)
    {
        if (snapshot.LastGeneration < 1 || snapshot.Active is null || snapshot.Active.Any(effect => effect is null ||
            effect.Generation < 1 || effect.Generation > snapshot.LastGeneration || effect.Ordinal < 0 ||
            effect.Target.ObjectId == 0 || effect.Caster.ObjectId == 0 || effect.Source.ObjectId == 0 ||
            effect.Effect.ObjectId == 0 || effect.Script.ObjectId == 0 || !ValidHash(effect.SourceHash) ||
            !ValidHash(effect.EffectHash) || !ValidHash(effect.ScriptHash) || effect.Locals is null ||
            !double.IsFinite(effect.Duration) || effect.Duration <= 0 || !double.IsFinite(effect.Elapsed) ||
            effect.Elapsed < 0 || effect.Elapsed >= effect.Duration) ||
            !snapshot.Active.Select(effect => effect.Generation).SequenceEqual(snapshot.Active.Select(effect => effect.Generation).Distinct().Order()) ||
            snapshot.Active.Select(effect => (effect.Target, effect.Effect)).Distinct().Count() != snapshot.Active.Count)
            throw new InvalidDataException("Saved delivered effects have invalid lifecycle or identity.");
    }

    private Activation Build(FalloutFormKey target, FalloutFormKey caster, FalloutIngestible source, FalloutIngestibleEffect effect)
    {
        if (_records.RuntimeFormId(target) != 0x14 && _records.GetEffective(target).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Delivered effect target is not a placed actor.");
        _ = _records.RuntimeFormId(caster);
        if (effect.Archetype != 1 || effect.Script is not { } script || effect.Range != 1 || effect.EffectiveDuration == 0 ||
            effect.Magnitude != 0 || effect.Resistance != -1 || effect.AdditionalPresentation ||
            (effect.Flags & 0x20) == 0 || (effect.Flags & ~0x100001f5u) != 0)
            throw new NotSupportedException($"Delivered script effect {effect.Form} needs its flags, duration, resistance or presentation owner.");
        var definition = Script(script);
        return new(new(0, target, caster, source.Form, source.Hash, effect.Index, effect.Form, effect.Hash,
            script, definition.ScriptHash, effect.EffectiveDuration, 0, []), definition, new(definition.Script));
    }

    private FalloutIngestible Source(FalloutFormKey source)
    {
        if (!_sources.TryGetValue(source, out var definition))
            _sources.Add(source, definition = FalloutIngestible.ReadWeaponEnchantment(_records, source));
        return definition;
    }

    private Definition Script(FalloutFormKey script)
    {
        if (_scripts.TryGetValue(script, out var known)) return known;
        var record = _records.GetEffective(script);
        if (record.Signature != "SCPT") throw new InvalidDataException("Delivered effect script is not SCPT.");
        var fields = record.ReadSubrecords().ToArray();
        var header = fields.Single(field => field.Signature == "SCHR").Data;
        var compiled = fields.Single(field => field.Signature == "SCDA").Data;
        var source = fields.Single(field => field.Signature == "SCTX").Data;
        if (header.Length != 20 || FalloutScriptHeader.Read(header.Span).CompiledBytes != compiled.Length)
            throw new InvalidDataException("Delivered effect script source lacks complete compiled identity.");
        var events = FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(source.Span));
        var blocks = new Dictionary<string, FalloutGameModeProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in events)
        {
            if (block.Event.ToLowerInvariant() is not ("scripteffectstart" or "scripteffectfinish") ||
                block.Filter is not null || block.Parameters is not null || !blocks.TryAdd(block.Event, block.Program))
                throw new NotSupportedException($"Delivered effect script {script} needs its source event ordering or parameter owner.");
        }
        if (blocks.Count == 0) throw new InvalidDataException("Delivered effect script has no lifecycle blocks.");
        var definition = new Definition(record, Convert.ToHexString(SHA256.HashData(record.ReadData())),
            blocks.GetValueOrDefault("ScriptEffectStart"), blocks.GetValueOrDefault("ScriptEffectFinish"));
        _scripts.Add(script, definition);
        return definition;
    }

    private void RequireNoOverlap(IReadOnlyList<Activation> effects)
    {
        var keys = new HashSet<(FalloutFormKey Target, FalloutFormKey Effect)>();
        foreach (var effect in _active.Concat(effects))
            if (!keys.Add((effect.State.Target, effect.State.Effect)))
                throw new NotSupportedException($"Delivered effect {effect.State.Effect} on {effect.State.Target} needs its source recast or stacking owner.");
    }

    private static void AddInvocation(List<FalloutDeliveredEffectInvocation> invocations, Activation activation,
        FalloutGameModeProgram? program, FalloutDeliveredEffectPhase phase)
    {
        if (program is not null) invocations.Add(new(activation.State, activation.Definition.Script, program, activation.Locals, phase));
    }

    private void Preflight(IReadOnlyList<FalloutDeliveredEffectInvocation> invocations)
    {
        var prepare = _preflight ?? throw new NotSupportedException("Delivered scripts have no command preflight owner.");
        if (invocations.Count == 0) return;
        _invoking = true;
        try { prepare(invocations); }
        finally { _invoking = false; }
    }

    private void Invoke(IReadOnlyList<FalloutDeliveredEffectInvocation> invocations)
    {
        _invoking = true;
        try
        {
            foreach (var invocation in invocations)
                _execute(invocation.State.Target, invocation.Script, invocation.Program, invocation.Locals);
        }
        catch (Exception error)
        {
            _failure = $"Delivered script invocation failed: {error.Message}";
            throw new InvalidOperationException(_failure, error);
        }
        finally { _invoking = false; }
    }

    private static void RequireTarget(FalloutFormKey target, Func<FalloutFormKey, FalloutDeliveredTargetStatus> targetStatus)
    {
        if (targetStatus(target) is not (FalloutDeliveredTargetStatus.Resident or FalloutDeliveredTargetStatus.Unloaded))
            throw new NotSupportedException($"Delivered effect target {target} needs its source retirement or cancellation owner.");
    }

    private void RequireAvailable()
    {
        if (_failure is not null) throw new InvalidOperationException(_failure);
        if (_invoking) throw new InvalidOperationException("Delivered effect lifecycle is already invoking a script.");
    }

    private static bool ValidHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
