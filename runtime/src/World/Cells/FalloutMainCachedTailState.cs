using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainCachedTailPhase
{
    BeforeOptionalPlayer, RequestManager, IndependentResets, ActorReset, Delay,
    FrameCounters, BeforeTimer, TimerReturned, HavokSettingCopied, RendererEntered,
    SourceChannels, GameplayDeltaStored, LocalReturned
}
internal sealed record FalloutMainCachedTailAttempt(Guid Main, long Ordinal, long Entered, long Changed,
    FalloutMainCachedTailPhase Phase, string? Failure);
internal sealed record FalloutMainCachedTailSnapshot(string Schema, FalloutMainCachedTailSource Source, string Stack,
    Guid CapturedProcess, long Sequence, byte OptionalPlayer, IReadOnlyList<byte> RequestCountdowns,
    byte IndependentByte, uint IndependentFloatBits, byte ActorResetPending, uint DelayRequest,
    uint FrameCounter, uint MainWord, uint GameplayFrameCounter, uint HavokTauBits,
    IReadOnlyList<uint> ChannelBits, uint GameplayDeltaBits, FalloutMainCachedTailAttempt? LastAttempt);

internal interface IFalloutMainCachedTailChildren
{
    string Source { get; }
    string Owner { get; }
    void OptionalPlayer(FalloutMainScriptInvocation invocation);
    bool FadeBlocksRequest(FalloutMainScriptInvocation invocation, int index);
    bool InterfaceBlocksRequest(FalloutMainScriptInvocation invocation);
    void DispatchRequest(FalloutMainScriptInvocation invocation, int request);
    void ResetPublishedActorsAndPlayer(FalloutMainScriptInvocation invocation);
    void PublishRendererClock(FalloutMainScriptInvocation invocation, float scaledSeconds);
    float ReadRegisteredClockChannel(FalloutMainScriptInvocation invocation, int index);
}

// This owns the actual preceding stores as well as the returned timer prefix.
// The request manager is a genuine source class constructor with three byte
// fields. Its empty constructor return does not complete pending saves.
internal sealed class FalloutMainCachedTailState : IDisposable
{
    internal const string Schema = "opennv-source-Main-cached-tail/v1";
    internal FalloutMainCachedTailSource Source { get; }
    private readonly string _stack;
    private readonly Guid _process;
    private readonly IFalloutSourceTickCounter _counter;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly byte[] _requests = new byte[3];
    private readonly uint[] _channels = new uint[4];
    private long _sequence;
    private byte _optionalPlayer, _independentByte, _actorResetPending;
    private uint _independentFloatBits, _delayRequest, _frameCounter, _mainWord, _gameFrameCounter, _havokBits, _gameplayDeltaBits;
    private FalloutMainCachedTailAttempt? _last;
    private Guid _active;
    private bool _disposed, _callback, _reentry;
    internal FalloutMainCachedTailState(FalloutMainCachedTailSource source, string stack, Guid process,
        IFalloutSourceTickCounter counter, FalloutMainCachedTailSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(counter);
        ArgumentException.ThrowIfNullOrWhiteSpace(counter.Owner);
        if (process == Guid.Empty) throw new InvalidDataException("Source Main tail has no real process constructor.");
        Source = source; _stack = stack; _process = process; _counter = counter;
        _frameCounter = source.InitialFrameCounter; _havokBits = source.InitialHavokTauBits;
        if (saved is null) return;
        Validate(saved);
        if (saved.Source != source || saved.Stack != stack || saved.CapturedProcess == process)
            throw new InvalidDataException("Cold Main tail changed source or restored another live process.");
        _sequence = saved.Sequence; _optionalPlayer = saved.OptionalPlayer;
        for (var index = 0; index < _requests.Length; ++index) _requests[index] = saved.RequestCountdowns[index];
        _independentByte = saved.IndependentByte; _independentFloatBits = saved.IndependentFloatBits;
        _actorResetPending = saved.ActorResetPending; _delayRequest = saved.DelayRequest; _frameCounter = saved.FrameCounter;
        _mainWord = saved.MainWord; _gameFrameCounter = saved.GameplayFrameCounter; _havokBits = saved.HavokTauBits;
        for (var index = 0; index < _channels.Length; ++index) _channels[index] = saved.ChannelBits[index];
        _gameplayDeltaBits = saved.GameplayDeltaBits; _last = saved.LastAttempt;
    }
    internal string? SaveBlocker => _active != Guid.Empty ? "source-Main-cached-tail-entered" :
        _last?.Failure is { } failure ? "source-Main-cached-tail-failed:" + failure :
        _last is { Phase: not FalloutMainCachedTailPhase.LocalReturned } ? "source-Main-cached-tail-unreturned" : null;
    internal object State => new
    {
        source = Source,
        process = _process,
        sequence = _sequence,
        last = _last,
        frameCounter = _frameCounter,
        gameplayFrameCounter = _gameFrameCounter,
        pendingActorReset = _actorResetPending,
        requestCountdowns = _requests.ToArray(),
        delayRequest = _delayRequest,
        sourceChannels = _channels.ToArray(),
        gameplayDeltaBits = _gameplayDeltaBits,
        saveBlocker = SaveBlocker
    };

    internal void BeforeTimer(FalloutMainScriptInvocation invocation, bool cachedMenu, IFalloutMainCachedTailChildren children)
    {
        Require(); invocation.Require(FalloutMainScriptCallerStep.CachedTailBefore);
        if (invocation.Process != _process || children.Source != Source.Identity || string.IsNullOrWhiteSpace(children.Owner) ||
            _last?.Main == invocation.Identity || _last is { Failure: not null } || _last is { Phase: not FalloutMainCachedTailPhase.LocalReturned })
            throw new InvalidOperationException("Main tail lost its actual once-only child/source lifetime or replayed a failed prefix.");
        _active = invocation.Identity; _last = new(invocation.Identity, invocation.Ordinal, Next(), _sequence,
            FalloutMainCachedTailPhase.BeforeOptionalPlayer, null);
        try
        {
            if (_optionalPlayer != 0) Call(() => children.OptionalPlayer(invocation));
            Set(FalloutMainCachedTailPhase.RequestManager);
            for (var request = 0; request < _requests.Length; ++request)
            {
                if (_requests[request] == 0) continue;
                if (_requests[request] > 1) { --_requests[request]; Next(); }
                else if (!Read(() => children.FadeBlocksRequest(invocation, 1)) && !Read(() => children.FadeBlocksRequest(invocation, 2)) &&
                    !Read(() => children.InterfaceBlocksRequest(invocation)))
                {
                    Call(() => children.DispatchRequest(invocation, request));
                    if (request == 1) { _requests[request] = 0; Next(); }
                }
                // The actual source uses priority short-circuiting. A higher
                // pending field stops this local pass even when fades hold it.
                break;
            }
            Set(FalloutMainCachedTailPhase.IndependentResets); _independentByte = 0; Next(); _independentFloatBits = 0; Next();
            Set(FalloutMainCachedTailPhase.ActorReset);
            if (!cachedMenu) { _actorResetPending = 1; Next(); }
            else
            {
                if (_actorResetPending != 0) Call(() => children.ResetPublishedActorsAndPlayer(invocation));
                _actorResetPending = 0; Next();
            }
            // The following selected source object child is an actual empty
            // return. It is represented here, not by a user supplied callback.
            Set(FalloutMainCachedTailPhase.Delay);
            if (_delayRequest != 0)
            {
                _delayRequest = Math.Min(_delayRequest, 20u); Next();
                Call(() => _counter.Wait(_delayRequest)); _delayRequest = 0; Next();
            }
            Set(FalloutMainCachedTailPhase.FrameCounters);
            _frameCounter = unchecked(_frameCounter + 1); Next(); _mainWord = 0; Next();
            if (!cachedMenu) { _gameFrameCounter = unchecked(_gameFrameCounter + 1); Next(); }
            Set(FalloutMainCachedTailPhase.BeforeTimer);
        }
        catch (Exception failure) { Fail(failure); throw; }
        finally { _active = Guid.Empty; }
    }
    internal void TimerReturned(FalloutMainScriptInvocation invocation)
    {
        Require(); invocation.Require(FalloutMainScriptCallerStep.CachedTimer);
        if (_last is not { Phase: FalloutMainCachedTailPhase.BeforeTimer, Failure: null } current || current.Main != invocation.Identity)
            throw new InvalidOperationException("Timer receipt escaped its actual immediately preceding source Main prefix.");
        Set(FalloutMainCachedTailPhase.TimerReturned);
    }
    internal void AfterTimer(FalloutMainScriptInvocation invocation, bool cachedMenu, float scaledSeconds,
        FalloutPluginStack records, IFalloutMainCachedTailChildren children)
    {
        Require(); invocation.Require(FalloutMainScriptCallerStep.CachedTailAfter);
        if (_last is not { Phase: FalloutMainCachedTailPhase.TimerReturned, Failure: null } current || current.Main != invocation.Identity ||
            children.Source != Source.Identity || string.IsNullOrWhiteSpace(children.Owner))
            throw new InvalidOperationException("Main clock suffix lacks its actual returned timer/source child.");
        _active = invocation.Identity;
        try
        {
            _havokBits = BitConverter.SingleToUInt32Bits(records.NumericSettings.Float("fHavokTauRatio"));
            Set(FalloutMainCachedTailPhase.HavokSettingCopied);
            Set(FalloutMainCachedTailPhase.RendererEntered); Call(() => children.PublishRendererClock(invocation, scaledSeconds));
            Set(FalloutMainCachedTailPhase.SourceChannels);
            for (var index = 0; index < 4; ++index)
            { var channel = index; _channels[index] = BitConverter.SingleToUInt32Bits(Read(() => children.ReadRegisteredClockChannel(invocation, channel))); Next(); }
            var delta = cachedMenu ? 0f : (float)((double)scaledSeconds * records.IniSettings.Float("fAnimationMult:General"));
            _gameplayDeltaBits = BitConverter.SingleToUInt32Bits(delta); Set(FalloutMainCachedTailPhase.GameplayDeltaStored);
            Set(FalloutMainCachedTailPhase.LocalReturned);
        }
        catch (Exception failure) { Fail(failure); throw; }
        finally { _active = Guid.Empty; }
    }
    internal void RetainTimerFailure(FalloutMainScriptInvocation invocation, Exception failure)
    {
        Require(); invocation.Require(FalloutMainScriptCallerStep.CachedTimer);
        if (_last?.Main != invocation.Identity) throw new InvalidOperationException("Failed timer has no actual entered Main prefix.");
        Fail(failure);
    }
    private T Read<T>(Func<T> body)
    {
        _callback = true; _reentry = false;
        try { var value = body(); if (_reentry) throw new InvalidOperationException("Tail child swallowed a forbidden source reentry."); return value; }
        finally { _callback = false; }
    }
    private void Call(Action body) => Read(() => { body(); return true; });
    private void Require()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != Environment.CurrentManagedThreadId || _callback || _active != Guid.Empty)
        { _reentry = true; throw new InvalidOperationException("Main source tail changed its actual thread or reentered."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void Set(FalloutMainCachedTailPhase phase) => _last = _last! with { Phase = phase, Changed = Next() };
    private void Fail(Exception failure) => _last = _last! with { Failure = _last.Failure ?? failure.ToString(), Changed = Next() };
    internal FalloutMainCachedTailSnapshot Capture()
    {
        Require();
        var value = new FalloutMainCachedTailSnapshot(Schema, Source, _stack, _process, _sequence, _optionalPlayer, _requests.ToArray(),
            _independentByte, _independentFloatBits, _actorResetPending, _delayRequest, _frameCounter, _mainWord, _gameFrameCounter,
            _havokBits, _channels.ToArray(), _gameplayDeltaBits, _last);
        Validate(value); return value;
    }
    internal static void Validate(FalloutMainCachedTailSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Schema != Schema || value.Source is null || string.IsNullOrWhiteSpace(value.Stack) || value.CapturedProcess == Guid.Empty || value.Sequence < 0 ||
            value.RequestCountdowns is not { Count: 3 } || value.ChannelBits is not { Count: 4 } || value.OptionalPlayer > 1 || value.ActorResetPending > 1 ||
            value.LastAttempt is { } last && (last.Main == Guid.Empty || last.Ordinal < 1 || last.Entered < 1 || last.Changed < last.Entered ||
                last.Changed > value.Sequence || !Enum.IsDefined(last.Phase) || last.Failure is not null && string.IsNullOrWhiteSpace(last.Failure)))
            throw new InvalidDataException("Main tail omitted its exact fields/source/entered failure prefix.");
        value.Source.Validate();
        // These external writers are not yet admitted. A cold blob cannot
        // manufacture their input merely by supplying source-shaped fields.
        if (value.OptionalPlayer != 0 || value.RequestCountdowns.Any(count => count != 0) || value.DelayRequest != 0)
            throw new NotSupportedException("Main optional Player/request-countdown/delay writers are independently unowned.");
        if (value.LastAttempt is null && (value.Sequence != 0 || value.ActorResetPending != 0 ||
            value.IndependentByte != 0 || value.IndependentFloatBits != 0 || value.MainWord != 0 ||
            value.FrameCounter != value.Source.InitialFrameCounter || value.GameplayFrameCounter != 0 ||
            value.HavokTauBits != value.Source.InitialHavokTauBits || value.ChannelBits.Any(bits => bits != 0) || value.GameplayDeltaBits != 0))
            throw new InvalidDataException("Fresh Main cached tail changed its genuine loader/file constructor fields.");
    }
    public void Dispose()
    {
        if (_disposed) return; Require(); _disposed = true;
    }
}
