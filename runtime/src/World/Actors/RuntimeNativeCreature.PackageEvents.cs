using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    internal Action<FalloutPackageEvent, FalloutFormKey>? ExecutePackageEvent { get; set; }
    private RuntimeLiveContentSource _content = null!;
    private RuntimeNativeNifAnimation? _eventIdleAnimation;
    private FalloutIdleAnimationPlayback? _eventIdleClock;
    private readonly ConditionalWeakTable<RuntimeNativeNifAnimation, FalloutIdleGroupTextKeys> _idleGroupKeys = new();
    private bool PackageEndIdlePending => _aiState?.PackageIdle?.Kind == "POEA" && _eventIdleClock?.Complete == false;

    private void DispatchPackageEvent(FalloutScriptPackage package, string kind)
    {
        var actor = Appearance.Reference!.Value;
        var world = _aiWorld!;
        if (kind == "POBA") world.MarkPackageStart(actor, _aiRecords!.GetEffective(package.Form), _aiClock);
        world.PackageEvents.Mark(actor, package.Form, kind switch
        {
            "POBA" => FalloutReferencePackageEventKind.Start,
            "POEA" => FalloutReferencePackageEventKind.Done,
            "POCA" => FalloutReferencePackageEventKind.Change,
            _ => throw new InvalidDataException("Creature package event kind is unknown.")
        });
        try
        {
            if (package.EventPrograms.GetValueOrDefault(kind) is { } program)
            {
                if (ExecutePackageEvent is { } execute) execute(program, actor);
                else program.RequireEmptyScript();
            }
            if (package.Events.GetValueOrDefault(kind) is { } form)
            {
                var record = _aiRecords!.GetEffective(form);
                _aiState!.RecordIdleRequest(_aiRecords, form);
                var idle = FalloutActorIdleSource.Resolve(_aiRecords, record);
                var timing = FalloutIdleAnimationData.Read(record);
                var source = ReadIdle(idle);
                var clock = new FalloutIdleAnimationPlayback(source.Animation.Sequence.StartTime, source.Animation.Sequence.StopTime,
                    source.Animation.Sequence.Frequency, source.Animation.Sequence.CycleType,
                    source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), timing.SelectAdditionalLoops(_aiState!.SoundRandom.NextBounded), source.Animation.Sequence.Name);
                var saved = new FalloutPackageEventIdle(package.Form, Hash(_aiRecords.GetEffective(package.Form).ReadData()), kind,
                    form, Hash(record.ReadData()), idle.AnimationPath, source.Hash, clock.Capture());
                BindEventIdle(saved, source.Animation, clock);
            }
            GD.Print($"OPENNV_CREATURE_PACKAGE_EVENT reference={actor} package={package.Form} event={kind} owner=shared-reference-results");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FileNotFoundException)
        {
            _aiState!.ScriptError ??= $"Package {kind} {package.Form}: {error.Message}";
            throw;
        }
    }

    private (RuntimeNativeNifAnimation Animation, string Hash) ReadIdle(FalloutActorIdleSource idle, bool externalAccumulationRoot = false)
    {
        if (idle.Objects.Count != 0) throw new NotSupportedException("Creature event idle requires its ANIO attachment owner.");
        var nif = NativeAnimationResources.For(_content).Read(idle.AnimationPath);
        var sequence = FalloutKfSequenceSource.Primary(nif);
        var attachments = IndexAttachmentTargets();
        var targets = attachments.Keys.ToHashSet(StringComparer.Ordinal);
        targets.UnionWith(sequence.ControlledBlocks.Select(link => link.NodeName).Where(name =>
            Skeleton.HasSourceTarget(name) || Skeleton.MaterialChannels.HasSourceTarget(name)));
        var animation = new RuntimeNativeNifAnimation(nif, sequence, Skeleton, link => BindAttachment(nif, link, attachments),
            externalObjectTargets: targets, externalAccumulationRoot: externalAccumulationRoot);
        if (animation.UnboundChannels.Count != 0)
            throw new NotSupportedException("Creature event idle has unbound source channels: " +
                string.Join("; ", animation.UnboundChannels.Select(channel => channel.Source.NodeName + "/" + channel.Reason)));
        _ = GroupKeys(animation);
        return (animation, nif.Sha256.ToUpperInvariant());
    }

    private void RestoreEventIdle()
    {
        if (_aiState?.PackageIdle is not { } saved) return;
        saved.Validate();
        var idle = FalloutActorIdleSource.Resolve(_aiRecords!, _aiRecords!.GetEffective(saved.Idle));
        var source = ReadIdle(idle);
        if (!saved.Animation.Equals(idle.AnimationPath, StringComparison.OrdinalIgnoreCase) ||
            !saved.AnimationSha256.Equals(source.Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved creature idle differs from its owned animation.");
        var sequence = source.Animation.Sequence;
        var clock = new FalloutIdleAnimationPlayback(sequence.StartTime, sequence.StopTime, sequence.Frequency, sequence.CycleType,
            source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), saved.Clock.SelectedAdditionalLoops, sequence.Name);
        clock.Restore(saved.Clock);
        BindEventIdle(saved, source.Animation, clock);
    }

    private void BindEventIdle(FalloutPackageEventIdle saved, RuntimeNativeNifAnimation animation, FalloutIdleAnimationPlayback clock)
    {
        saved.Validate();
        if (ScriptIdleActive) throw new NotSupportedException("Creature package event idle cannot compose an active script idle.");
        animation.ApplySourceTime(clock.SourceSeconds);
        _eventIdleAnimation = animation; _eventIdleClock = clock; _aiState!.PackageIdle = saved;
    }

    private bool AdvanceEventIdle(double delta)
    {
        if (_eventIdleAnimation is not { } animation || _eventIdleClock is not { } clock || _aiState!.PackageIdle is not { } saved) return false;
        try
        {
            clock.Advance(delta, interval => DispatchIdleInterval(animation, interval, saved.Idle));
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            _aiState.ScriptError ??= $"Package idle {saved.Idle}: {error.Message}";
            throw;
        }
        finally { _aiState.PackageIdle = saved with { Clock = clock.Capture() }; }
        if (Combat?.PackageOwnsPose == true) animation.ApplySourceTime(clock.SourceSeconds);
        else RuntimeNativeNifAnimation.ApplyLayers((_animation, SourceSeconds), (animation, clock.SourceSeconds));
        if (clock.Complete)
        {
            _eventIdleAnimation = null; _eventIdleClock = null; _aiState.PackageIdle = null; _evaluateRequested = true;
            GD.Print($"OPENNV_CREATURE_PACKAGE_IDLE_END reference={Appearance.Reference} idle={saved.Idle} sourceSeconds={clock.SourceSeconds:R}");
        }
        return true;
    }

    private void DispatchIdleInterval(RuntimeNativeNifAnimation animation, FalloutIdleAnimationInterval interval, FalloutFormKey idle)
    {
        animation.ApplySourceTime(interval.To);
        var declarations = GroupKeys(animation);
        for (var ordinal = 0; ordinal < animation.TextKeys.Count; ++ordinal)
        {
            var key = animation.TextKeys[ordinal];
            if (key.Time > interval.To || !(key.Time > interval.From || interval.IncludeFrom && key.Time == interval.From)) continue;
            var values = key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (var segment = 0; segment < values.Length; ++segment)
            {
                var value = values[segment].Trim();
                var structural = value.Equals("start", StringComparison.OrdinalIgnoreCase) || value.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("StartLoop", StringComparison.OrdinalIgnoreCase) || value.Equals("EndLoop", StringComparison.OrdinalIgnoreCase);
                var kind = declarations.Kind(ordinal, segment);
                structural |= kind == FalloutIdleGroupKeyKind.GroupMarker;
                if (ReferenceEquals(animation, _scriptIdleAnimation) && !_scriptIdleClock!.HasGroupTiming && kind == FalloutIdleGroupKeyKind.GroupMarker &&
                    value.Equals("End", StringComparison.OrdinalIgnoreCase) && _scriptIdleClock!.AdditionalLoops == 0 &&
                    _scriptIdleDeactivation is null)
                    _scriptIdleEndPending = true;
                var disposition = kind == FalloutIdleGroupKeyKind.NonmatchingGroupMarker
                    ? "source-nonmatching-group-marker" : kind == FalloutIdleGroupKeyKind.TransitionMetadata &&
                        ReferenceEquals(animation, _scriptIdleAnimation) && _scriptIdleTransition is not null
                        ? "source-transition-metadata" : structural ? "source-phase-owner" : DispatchCreatureTextKey(value);
                if (disposition.Contains("unbound", StringComparison.Ordinal))
                    throw new NotSupportedException($"Creature event idle key {value} is unbound.");
                _lastEvent = new { ordinal = ++_eventCount, idle = idle.ToString(), key.Time, key = value, disposition };
            }
        }
    }

    private FalloutIdleGroupTextKeys GroupKeys(RuntimeNativeNifAnimation animation) =>
        _idleGroupKeys.GetValue(animation, static source => FalloutIdleGroupTextKeys.Read(source.Sequence.Name, source.TextKeys));

    private string DispatchCreatureTextKey(string value) => _sounds.Dispatch(value);

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
