using System.Security.Cryptography;
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
    private bool PackageEndIdlePending => _aiState?.PackageIdle?.Kind == "POEA" && _eventIdleClock?.Complete == false;

    private void DispatchPackageEvent(FalloutScriptPackage package, string kind)
    {
        var actor = Appearance.Reference!.Value;
        var world = _aiWorld!;
        var startedRevision = BeginScriptPackageEvent(package, kind);
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
                var idle = FalloutActorIdleSource.Resolve(_aiRecords, record);
                var timing = FalloutIdleAnimationData.Read(record);
                var source = ReadIdle(idle);
                var clock = new FalloutIdleAnimationPlayback(source.Animation.Sequence.StartTime, source.Animation.Sequence.StopTime,
                    source.Animation.Sequence.Frequency, source.Animation.Sequence.CycleType,
                    source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), timing.SelectAdditionalLoops(_aiState!.SoundRandom.NextBounded));
                var saved = new FalloutPackageEventIdle(package.Form, Hash(_aiRecords.GetEffective(package.Form).ReadData()), kind,
                    form, Hash(record.ReadData()), idle.AnimationPath, source.Hash, clock.Capture());
                BindEventIdle(saved, source.Animation, clock);
            }
            CompleteScriptPackageEvent(package, kind, startedRevision);
            GD.Print($"OPENNV_CREATURE_PACKAGE_EVENT reference={actor} package={package.Form} event={kind} owner=shared-reference-results");
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FileNotFoundException)
        {
            _aiState!.ScriptError ??= $"Package {kind} {package.Form}: {error.Message}";
            throw;
        }
    }

    private (RuntimeNativeNifAnimation Animation, string Hash) ReadIdle(FalloutActorIdleSource idle)
    {
        if (idle.Objects.Count != 0) throw new NotSupportedException("Creature event idle requires its ANIO attachment owner.");
        if (!_content.TryRead(idle.AnimationPath, null, out var bytes, out _)) throw new FileNotFoundException(idle.AnimationPath);
        var nif = FalloutNifFile.Read(bytes);
        var sequences = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        if (sequences.Length != 1) throw new NotSupportedException("Creature event idle requires one source sequence.");
        var targets = FindChildren("*", "", true, false).OfType<Node3D>()
            .Select(node => node.GetMeta("opennv_nif_source_name", "").AsString()).ToHashSet(StringComparer.Ordinal);
        targets.UnionWith(sequences[0].ControlledBlocks.Select(link => link.NodeName).Where(name =>
            Skeleton.HasSourceTarget(name) || Skeleton.MaterialChannels.HasSourceTarget(name)));
        var animation = new RuntimeNativeNifAnimation(nif, sequences[0], Skeleton, link => BindAttachment(nif, link),
            externalObjectTargets: targets);
        if (animation.UnboundChannels.Count != 0)
            throw new NotSupportedException("Creature event idle has unbound source channels: " +
                string.Join("; ", animation.UnboundChannels.Select(channel => channel.Source.NodeName + "/" + channel.Reason)));
        return (animation, Hash(bytes));
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
            source.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), saved.Clock.SelectedAdditionalLoops);
        clock.Restore(saved.Clock);
        BindEventIdle(saved, source.Animation, clock);
    }

    private void BindEventIdle(FalloutPackageEventIdle saved, RuntimeNativeNifAnimation animation, FalloutIdleAnimationPlayback clock)
    {
        saved.Validate();
        animation.ApplySourceTime(clock.SourceSeconds);
        _eventIdleAnimation = animation; _eventIdleClock = clock; _aiState!.PackageIdle = saved;
    }

    private bool AdvanceEventIdle(double delta)
    {
        if (_eventIdleAnimation is not { } animation || _eventIdleClock is not { } clock || _aiState!.PackageIdle is not { } saved) return false;
        try
        {
            clock.Advance(delta, interval =>
            {
                animation.ApplySourceTime(interval.To);
                foreach (var key in animation.TextKeys.Where(key => key.Time <= interval.To &&
                    (key.Time > interval.From || interval.IncludeFrom && key.Time == interval.From)))
                    foreach (var value in key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
                    {
                        var structural = value.Equals("start", StringComparison.OrdinalIgnoreCase) || value.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("StartLoop", StringComparison.OrdinalIgnoreCase) || value.Equals("EndLoop", StringComparison.OrdinalIgnoreCase);
                        var disposition = structural ? "source-phase-owner" : _sounds.Dispatch(value);
                        if (disposition.Contains("unbound", StringComparison.Ordinal))
                            throw new NotSupportedException($"Creature event idle key {value} is unbound.");
                        _lastEvent = new { ordinal = ++_eventCount, idle = saved.Idle.ToString(), key.Time, key = value, disposition };
                    }
            });
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

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
