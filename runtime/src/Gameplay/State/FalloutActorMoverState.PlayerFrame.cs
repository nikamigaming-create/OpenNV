using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutActorMoverState
{
    internal const string FrameScopeBoundary = "source-Actor-mover-actual-Player-frame-caller-unbound";
    internal const string FrameWordBoundary = "source-Player-mover-nonzero-frame-word-setting-or-animation-effects-unbound";
    private Action<FalloutActorMoverFrameInvocation>? _requirePlayerFrame;
    private long _frameSequence, _frameFlagCalls, _frameVectorCalls;
    private long _frameFlagStores, _frameVectorXYStores, _frameVectorZStores;
    private FalloutActorMoverFrameStoreOperation? _frameFlags, _frameVector;
    private string? _frameFailure;
    private bool _currentFrameEntered;

    internal FalloutActorMoverFrameStoreOperation? LastPlayerFrameFlags => _frameFlags;
    internal FalloutActorMoverFrameStoreOperation? LastPlayerFrameDefaultVector => _frameVector;

    internal void BindPlayerFrame(FalloutCombatActorIdentity actor,
        Action<FalloutActorMoverFrameInvocation> requireActualFrame)
    {
        RequireOwner(); actor.Validate(); ArgumentNullException.ThrowIfNull(requireActualFrame);
        if (!actor.EnginePlayer || _requirePlayerFrame is not null || _currentFrameEntered || _actor is not null && _actor != actor)
            throw new InvalidOperationException("PlayerMover changed its actual Actor or current frame publication.");
        _actor = actor; _requirePlayerFrame = requireActualFrame;
    }

    internal FalloutActorMoverFrameStoreOperation StorePlayerFrameFlags(FalloutActorMoverFrameInvocation invocation, ushort word)
        => ExecutePlayerFrameStore(invocation, FalloutActorMoverFrameStoreKind.Flags, word);

    internal FalloutActorMoverFrameStoreOperation StorePlayerFrameDefaultVector(FalloutActorMoverFrameInvocation invocation)
        => ExecutePlayerFrameStore(invocation, FalloutActorMoverFrameStoreKind.DefaultVector, null);

    private FalloutActorMoverFrameStoreOperation ExecutePlayerFrameStore(FalloutActorMoverFrameInvocation invocation,
        FalloutActorMoverFrameStoreKind kind, ushort? word)
    {
        RequireOwner(); RequirePlayerFrameIdentity(invocation);
        if (_failure is { } packageFailure) throw new NotSupportedException(packageFailure);
        if (_frameFailure is { } frameFailure) throw new NotSupportedException(frameFailure);
        if (_requirePlayerFrame is null) throw new NotSupportedException(FrameScopeBoundary);
        if (kind == FalloutActorMoverFrameStoreKind.Flags)
        {
            if (_frameFlags is { } previous &&
                (!previous.Returned || invocation.Caller.Main == previous.Invocation.Caller.Main ||
                    invocation.Caller.Ordinal <= previous.Invocation.Caller.Ordinal))
                throw new InvalidOperationException("PlayerMover replayed its actual frame or crossed an unreturned flag setter.");
            _frameFlagCalls = checked(_frameFlagCalls + 1);
        }
        else
        {
            if (_frameFlags is not { Returned: true } flags || !SamePlayerFrameScope(flags.Invocation, invocation) ||
                invocation.Common.Changed < flags.Invocation.Common.Changed ||
                _frameVector is { } vector && SamePlayerFrameScope(vector.Invocation, invocation))
                throw new InvalidOperationException("PlayerMover default vector lost its same returned flag setter or replayed the frame child.");
            _frameVectorCalls = checked(_frameVectorCalls + 1);
        }
        _currentFrameEntered = true; _busy = true; var fault = _fault;
        var operation = new FalloutActorMoverFrameStoreOperation(invocation, Factory, kind,
            kind == FalloutActorMoverFrameStoreKind.Flags ? _frameFlagCalls : _frameVectorCalls,
            NextFrame(), _frameSequence, word, null, [], false, null);
        Retain(operation);
        try
        {
            Guard();
            if (kind == FalloutActorMoverFrameStoreKind.Flags)
            {
                RequireAvailable(FalloutActorMoverLane.PlayerFields);
                operation = operation with { PreviousFlags = _values.Player.Flags };
                Retain(operation);
                if (word != 0 || (_values.Player.Flags & 0x400) != 0)
                    throw new NotSupportedException(FrameWordBoundary);
                Store(FalloutActorMoverField.PlayerFlags, 0);
            }
            else
            {
                RequireAvailable(FalloutActorMoverLane.DefaultVector);
                RequireAvailable(FalloutActorMoverLane.PlayerFields);
                Store(FalloutActorMoverField.PlayerVectorXY, 0, 0);
                Store(FalloutActorMoverField.PlayerVectorZ, 0);
            }
            Guard();
            operation = Current() with { Returned = true, Changed = NextFrame() };
            Retain(operation); return operation;
        }
        catch (Exception error)
        {
            _frameFailure ??= error.Message;
            operation = Current() with { Failure = _frameFailure, Changed = NextFrame() };
            Retain(operation); throw;
        }
        finally { _busy = false; }

        FalloutActorMoverFrameStoreOperation Current() =>
            (kind == FalloutActorMoverFrameStoreKind.Flags ? _frameFlags : _frameVector) ??
            throw new InvalidOperationException("Entered PlayerMover lost its actual current frame store operation.");
        void Guard()
        {
            RequirePlayerFrameIdentity(invocation);
            (_requirePlayerFrame ?? throw new NotSupportedException(FrameScopeBoundary))(invocation); _requireLifetime();
            if (_retired || _thread != Environment.CurrentManagedThreadId || _fault != fault)
                throw new InvalidOperationException("PlayerMover frame guard swallowed reentry or changed its actual thread/lifetime.");
        }
        void Store(FalloutActorMoverField field, uint bits, uint? secondBits = null)
        {
            Guard(); var store = new FalloutActorMoverStore(field, NextFrame(), bits, secondBits);
            _values = Apply(_values, store);
            if (field == FalloutActorMoverField.PlayerFlags) _frameFlagStores = checked(_frameFlagStores + 1);
            else if (field == FalloutActorMoverField.PlayerVectorXY) _frameVectorXYStores = checked(_frameVectorXYStores + 1);
            else _frameVectorZStores = checked(_frameVectorZStores + 1);
            operation = Current() with { Stores = Array.AsReadOnly(Current().Stores.Append(store).ToArray()), Changed = _frameSequence };
            Retain(operation); Guard();
        }
    }

    private void RequirePlayerFrameIdentity(FalloutActorMoverFrameInvocation invocation)
    {
        ValidatePlayerFrameIdentity(invocation, Source, Stack, _actor);
        if (invocation.PlayerProcess != Process || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("PlayerMover frame borrowed another actual Player process/thread.");
    }
    private void Retain(FalloutActorMoverFrameStoreOperation operation)
    {
        if (operation.Kind == FalloutActorMoverFrameStoreKind.Flags) _frameFlags = operation;
        else _frameVector = operation;
    }
    private long NextFrame() => _frameSequence = checked(_frameSequence + 1);
    private FalloutActorMoverPlayerFrameSnapshot CapturePlayerFrame() => new(_frameSequence, _frameFlagCalls, _frameVectorCalls,
        _frameFlagStores, _frameVectorXYStores, _frameVectorZStores, CloneFrame(_frameFlags), CloneFrame(_frameVector), _frameFailure);
    private void RestorePlayerFrame(FalloutActorMoverPlayerFrameSnapshot? saved)
    {
        if (saved is null) return;
        _frameSequence = saved.Sequence; _frameFlagCalls = saved.FlagCalls; _frameVectorCalls = saved.VectorCalls;
        _frameFlagStores = saved.FlagStores; _frameVectorXYStores = saved.VectorXYStores; _frameVectorZStores = saved.VectorZStores;
        _frameFlags = CloneFrame(saved.Flags); _frameVector = CloneFrame(saved.Vector); _frameFailure = saved.Failure;
    }
    private static FalloutActorMoverFrameStoreOperation? CloneFrame(FalloutActorMoverFrameStoreOperation? call) =>
        call is null ? null : call with { Stores = Array.AsReadOnly(call.Stores.ToArray()) };
}
