using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutInterfaceFadeClock(ulong Frame, float SourceTimerDelta, float GlobalTimeMultiplier, string Owner)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner) || !float.IsFinite(SourceTimerDelta) || SourceTimerDelta < 0 ||
            !float.IsFinite(GlobalTimeMultiplier) || GlobalTimeMultiplier <= 0)
            throw new NotSupportedException("Interface fade has no admitted source UI timer/global multiplier observation.");
    }
}
internal sealed record FalloutInterfaceFadeForceRetirement(bool Forced, string Owner)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner)) throw new NotSupportedException("Interface fade global forced-retirement predicate is unowned.");
    }
}
internal sealed record FalloutInterfaceFadeNativeHost(Action<FalloutInterfaceFadeChannel> Publish,
    Action<FalloutInterfaceFadeChannel> WriteOpacity, Action<FalloutInterfaceFadeChannel> Retire);

// Source-owned fade channels are independent from IMADs, calendar TimeScale,
// save-writer completion and the console-open predicate. All three channels
// share the original release hold and are advanced in physical catalog order.
internal sealed class FalloutInterfaceFade
{
    internal const string Schema = "opennv-source-interface-fade/v1";
    internal FalloutInterfaceFadeSource Source { get; }
    private readonly FalloutInterfaceFadeChannel[] _channels;
    private readonly List<FalloutInterfaceFadeFailure> _nativeFailures = [];
    private FalloutInterfaceFadeNativeHost? _native;
    private readonly bool[] _published = new bool[3];
    private readonly long[] _drawn = new long[3];
    private bool _busy, _retired;
    private ulong? _frame;
    internal long Attempt { get; private set; }
    internal byte ReleaseHold { get; private set; }
    internal FalloutInterfaceFadeFailure? Failure { get; private set; }
    internal IReadOnlyList<FalloutInterfaceFadeChannel> Channels => Array.AsReadOnly(_channels);
    internal string? SaveBlocker => _busy ? "interface-fade-operation-prefix" : null;
    internal object State => new
    {
        source = Source,
        Attempt,
        ReleaseHold,
        channels = Channels,
        Failure,
        nativePublished = _published.ToArray(),
        drawnRevisions = _drawn.ToArray(),
        retired = _retired,
        nativeFailures = _nativeFailures.ToArray(),
        saveBlocker = SaveBlocker
    };

    internal FalloutInterfaceFade(FalloutInterfaceFadeSource source, FalloutInterfaceFadeSnapshot? restore = null)
    {
        source.Validate(); Source = source;
        _channels = Enumerable.Range(0, 3).Select(channel =>
            new FalloutInterfaceFadeChannel(channel, 0, 0, FalloutInterfaceFadeDirection.Absent, 0, 0)).ToArray();
        if (restore is null) return;
        restore.Validate(); restore.Source.RequireCurrent(source);
        for (var channel = 0; channel < _channels.Length; ++channel) _channels[channel] = restore.Channels[channel];
        Attempt = restore.Attempt; ReleaseHold = restore.ReleaseHold; Failure = restore.Failure;
        _nativeFailures.AddRange(restore.NativeFailures);
        // No source start, replacement, hold, hour, callback or timer executes
        // on cold. A healthy live channel gets only a new presentation lease.
    }

    internal void BindNative(FalloutInterfaceFadeNativeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_native is not null || _retired) throw new InvalidOperationException("Interface fade native lifetime cannot bind twice.");
        _native = host;
        if (Failure is not null) return;
        foreach (var channel in _channels.Where(channel => channel.Direction != FalloutInterfaceFadeDirection.Absent))
            Execute(channel.Channel, FalloutInterfaceFadeOperation.ColdPublication, () => Publish(channel));
    }

    internal bool Start(int channel, float duration, bool decreasing)
    {
        RequireHealthy(); var old = Channel(channel);
        if (!float.IsFinite(duration) || duration < 0)
            throw new NotSupportedException("A negative or nonfinite interface fade duration has no admitted source consumer.");
        if (old.Direction == FalloutInterfaceFadeDirection.Increasing) return false;
        if (old.Direction != FalloutInterfaceFadeDirection.Absent)
            Execute(channel, FalloutInterfaceFadeOperation.ReplaceRetirement, () => RetireChannel(old));
        Execute(channel, FalloutInterfaceFadeOperation.Start, () =>
        {
            var opaque = decreasing || duration == 0;
            var next = old with
            {
                Generation = checked(old.Generation + 1),
                Revision = checked(_channels[channel].Revision + 1),
                Direction = decreasing ? FalloutInterfaceFadeDirection.Decreasing : FalloutInterfaceFadeDirection.Increasing,
                Duration = duration,
                Opacity = opaque ? 1 : 0
            };
            _channels[channel] = next;
            if (opaque) ReleaseHold = 3;
            Publish(next);
        });
        return true;
    }

    internal void End(int channel, bool force, Func<FalloutInterfaceFadeForceRetirement> observeGlobalForce)
    {
        RequireHealthy(); var current = Channel(channel);
        if (current.Direction == FalloutInterfaceFadeDirection.Absent) return;
        Execute(channel, FalloutInterfaceFadeOperation.End, () =>
        {
            var forced = force;
            if (!force)
            {
                var observation = observeGlobalForce(); observation.Validate(); forced = observation.Forced;
            }
            if (forced) { RetireChannel(current); return; }
            var next = current with
            {
                Direction = FalloutInterfaceFadeDirection.Decreasing,
                Revision = checked(current.Revision + 1),
                Opacity = current.Opacity == 1 ? BitConverter.Int32BitsToSingle(0x3f7ff972) : current.Opacity
            };
            _channels[channel] = next; Write(next);
        });
    }

    internal void Advance(FalloutInterfaceFadeClock clock)
    {
        RequireHealthy();
        Execute(null, FalloutInterfaceFadeOperation.Frame, () =>
        {
            clock.Validate();
            if (_frame is { } previous && clock.Frame <= previous)
                throw new InvalidOperationException("Interface fade cannot consume the same or an older actual UI frame.");
            _frame = clock.Frame;
            var delta = clock.SourceTimerDelta / clock.GlobalTimeMultiplier;
            for (var channel = 0; channel < _channels.Length; ++channel)
            {
                var current = _channels[channel]; var operation =
                    current.Direction == FalloutInterfaceFadeDirection.Decreasing && current.Opacity <= 0 ?
                        FalloutInterfaceFadeOperation.FrameRetirement : FalloutInterfaceFadeOperation.FrameOpacity;
                try
                {
                    AdvanceChannel(current, delta);
                }
                catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
                { Failure ??= new(Attempt, channel, operation, error.GetType().Name + ": " + error.Message); throw; }
            }
        });
    }

    private void AdvanceChannel(FalloutInterfaceFadeChannel current, float delta)
    {
        if (current.Direction == FalloutInterfaceFadeDirection.Absent) return;
        RequirePublished(current);
        if (current.Direction == FalloutInterfaceFadeDirection.Increasing && current.Opacity >= 1) return;
        if (current.Direction == FalloutInterfaceFadeDirection.Decreasing && current.Opacity <= 0)
        { RetireChannel(current); return; }
        var opacity = current.Opacity;
        if (current.Direction == FalloutInterfaceFadeDirection.Decreasing && ReleaseHold != 0) --ReleaseHold;
        else
        {
            var increase = current.Direction == FalloutInterfaceFadeDirection.Increasing;
            if (Source.Declaration.Arithmetic == FalloutInterfaceFadeArithmetic.WideQuotientThenFloat32)
                opacity = (float)(current.Opacity + (increase ? 1 : -1) * ((double)delta / current.Duration));
            else
            {
                var step = delta / current.Duration;
                opacity = increase ? current.Opacity + step : current.Opacity - step;
            }
            if (increase && opacity > 1) opacity = 1;
            else if (!increase && opacity < 0) opacity = 0;
        }
        if (!float.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new NotSupportedException("Source interface fade arithmetic produced an unowned nonfinite/negative opacity.");
        var next = current with { Revision = checked(current.Revision + 1), Opacity = opacity };
        _channels[current.Channel] = next; Write(next);
    }

    internal void ObserveNativeDraw(int channel, long generation, long revision)
    {
        var current = Channel(channel); RequirePublished(current);
        if (current.Generation != generation || revision < _drawn[channel] || revision > current.Revision)
            throw new InvalidOperationException("Interface fade draw belongs to a different source generation or opacity revision.");
        _drawn[channel] = revision;
    }
    private void Publish(FalloutInterfaceFadeChannel channel)
    {
        if (_published[channel.Channel]) throw new InvalidOperationException("Source interface fade already owns native geometry.");
        Native().Publish(channel); _published[channel.Channel] = true;
    }
    private void Write(FalloutInterfaceFadeChannel channel)
    {
        RequirePublished(channel); Native().WriteOpacity(channel);
    }
    private void RetireChannel(FalloutInterfaceFadeChannel channel)
    {
        RequirePublished(channel);
        Native().Retire(channel); _published[channel.Channel] = false; _drawn[channel.Channel] = 0;
        _channels[channel.Channel] = channel with
        {
            Direction = FalloutInterfaceFadeDirection.Absent,
            Revision = checked(channel.Revision + 1),
            Opacity = 0
        };
    }
    private FalloutInterfaceFadeNativeHost Native() => _native ??
        throw new NotSupportedException("Interface fade has no actual owned texture/native root lifetime.");
    private FalloutInterfaceFadeChannel Channel(int channel) => channel is >= 0 and < 3 ? _channels[channel] :
        throw new ArgumentOutOfRangeException(nameof(channel));
    private void RequirePublished(FalloutInterfaceFadeChannel channel)
    {
        if (!_published[channel.Channel] || _retired)
            throw new NotSupportedException("Living interface fade has no current source geometry publication.");
    }
    private void Execute(int? channel, FalloutInterfaceFadeOperation operation, Action action)
    {
        RequireHealthy();
        if (_busy) throw new InvalidOperationException("Interface fade reentry has no source order owner.");
        Attempt = checked(Attempt + 1); _busy = true;
        try { action(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { Failure ??= new(Attempt, channel, operation, error.GetType().Name + ": " + error.Message); throw; }
        finally { _busy = false; }
    }
    internal void ReportNativeFailure(int? channel, FalloutInterfaceFadeOperation operation, Exception error)
    {
        if (channel is < 0 or >= 3 || !Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(channel));
        Attempt = checked(Attempt + 1);
        var failure = new FalloutInterfaceFadeFailure(Attempt, channel, operation, error.GetType().Name + ": " + error.Message);
        _nativeFailures.Add(failure); Failure ??= failure;
    }
    internal void RetireNativeLifetime()
    {
        if (_retired) return;
        _retired = true; _native = null; Array.Clear(_published); Array.Clear(_drawn);
    }
    internal void RequireHealthy()
    {
        if (Failure is { } failure) throw new InvalidOperationException(
            $"Interface fade operation {failure.Attempt}:{failure.Operation} failed: {failure.Error}");
        if (_retired) throw new ObjectDisposedException(nameof(FalloutInterfaceFade));
    }
    internal FalloutInterfaceFadeSnapshot Capture()
    {
        if (SaveBlocker is { } blocker) throw new NotSupportedException("Capture requires " + blocker);
        var result = new FalloutInterfaceFadeSnapshot(Schema, Source, Attempt, ReleaseHold, _channels.ToArray(), Failure, _nativeFailures.ToArray());
        result.Validate(); return result;
    }
}
