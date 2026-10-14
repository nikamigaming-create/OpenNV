using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutControllerMoverDeltaInvocation(Guid Call, FalloutPlayerLateFrameInvocation Frame,
    FalloutProcessSceneContextQuery Context, Guid RuntimeProcess, FalloutPhysicalVector3 Angles,
    FalloutActorMoverVector Movement, uint Flags);
internal sealed record FalloutControllerMoverDeltaFields(Guid? QueueHead, Guid? QueueTail,
    uint? ScalarBits, uint? IndependentWord, string? QueueBoundary = null);
internal sealed record FalloutControllerMoverDeltaCall(FalloutControllerMoverDeltaInvocation Invocation,
    Guid Controller, long Entered, long Changed, uint FlagsBefore, uint FlagsAfter,
    IReadOnlyList<FalloutEarlyControllerStore> Stores, FalloutPlayerMoverSpeedCacheRead? Speed,
    bool WorldRead, string? World, bool Returned, string? Failure);
internal sealed record FalloutControllerMoverDeltaSnapshot(FalloutControllerMoverDeltaFields Fields,
    long Sequence, long Calls, FalloutControllerMoverDeltaCall? LastCall);

internal sealed partial class FalloutEarlyController
{
    private FalloutControllerMoverDeltaFields _moverDeltaFields = new(null, null, null, null);
    private long _moverDeltaSequence, _moverDeltaCalls;
    private FalloutControllerMoverDeltaCall? _moverDeltaCall;

    // This is the real selected controller delta virtual's reached pre-world
    // body. It mutates the same fields as constructor/reset/post-action owners.
    // Its current-null world return does not invoke a state or contact solver.
    internal FalloutControllerMoverDeltaCall UpdateMoverDelta(FalloutPlayerMoverDeltaSource source,
        FalloutControllerMoverDeltaInvocation invocation, FalloutPlayerMoverSpeedCacheState speed,
        Action<FalloutControllerMoverDeltaInvocation> requireActual)
    {
        source.Validate(); RequireCurrent(); ArgumentNullException.ThrowIfNull(speed); ArgumentNullException.ThrowIfNull(requireActual);
        if (source.Controller.Fields.Controls.Main.EngineSha256 != _factory.Inputs.EngineSha256 ||
            source.Identity != speed.Source.Identity || speed.Actor != Lineage.Actor || speed.Stack != Lineage.Stack)
            throw new InvalidDataException("Controller delta changed its actual source/cache owner.");
        if (_moverDeltaCall is { } previous && (previous.Returned == false || previous.Invocation.Frame.Caller.Main == invocation.Frame.Caller.Main ||
            previous.Invocation.Frame.PlayerProcess == invocation.Frame.PlayerProcess &&
                previous.Invocation.Frame.Caller.Ordinal >= invocation.Frame.Caller.Ordinal))
            throw new InvalidOperationException("Controller delta replayed a returned or failed current source frame.");
        var fault = _fault; var before = _flags; var entered = _moverDeltaSequence; _busy = true;
        _moverDeltaCalls = checked(_moverDeltaCalls + 1);
        _moverDeltaCall = new(invocation, Identity, entered, entered, before, before, [], null, false, null, false, null);
        var writes = new List<FalloutEarlyControllerStore>();
        try
        {
            Guard(); _flags &= ~FalloutPlayerMoverDeltaSource.ControllerTransientMask; Write("mover-delta-flags", _flags); Guard();
            if (_moverDeltaFields.QueueBoundary is { } boundary) throw new NotSupportedException(boundary);
            if (_moverDeltaFields.QueueHead is not null || _moverDeltaFields.QueueTail is not null)
                throw new NotSupportedException("source-controller-delta-nonempty-retained-release-queue-payload-unbound");
            if (_postProcessFields.SecondBodyBoundary is { } second) throw new NotSupportedException(second);
            if (_postProcessFields.SecondBody is not null)
                throw new NotSupportedException("source-controller-delta-current-second-body-release-owner-unbound");
            _moverDeltaFields = _moverDeltaFields with { IndependentWord = 0 }; Write("mover-delta-independent-word", 0); Guard();
            if (_postProcessFields.ObjectBoundary is { } other) throw new NotSupportedException(other);
            if (_postProcessFields.Object is not null)
                throw new NotSupportedException("source-controller-delta-current-object-release-owner-unbound");
            // The enclosing Actor computes requested speed only when one of
            // its same current mover's low six bits is set. On the selected
            // zero-word arm, this numerator is source positive zero. The cache
            // denominator is still freshly read; zero cannot replace it.
            if ((invocation.Flags & FalloutPlayerMoverDeltaSource.MotionMask) != 0)
                throw new NotSupportedException("source-controller-delta-current-requested-speed-producer-unbound");
            var cached = speed.ReadCurrent(invocation.Frame, _ => Guard());
            _moverDeltaCall = _moverDeltaCall with { Speed = cached };
            var scalar = BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Divide(0, BitConverter.UInt32BitsToSingle(cached.Bits)));
            _moverDeltaFields = _moverDeltaFields with { ScalarBits = scalar }; Write("mover-delta-speed-scalar", scalar); Guard();
            var world = Phantom.CurrentWorld;
            _moverDeltaCall = _moverDeltaCall with { WorldRead = true, World = world }; Guard();
            if (world is not null)
                throw new NotSupportedException("source-controller-delta-nonnull-world-current-state-contact-integrator-unbound");
            _moverDeltaCall = _moverDeltaCall with
            {
                Changed = _moverDeltaSequence,
                FlagsAfter = _flags,
                Stores = writes.ToArray(),
                Returned = true
            };
            return _moverDeltaCall;
        }
        catch (Exception error)
        {
            _failure ??= error.Message; _factory.RetainFailure(_failure);
            _moverDeltaCall = _moverDeltaCall! with
            {
                Changed = _moverDeltaSequence,
                FlagsAfter = _flags,
                Stores = writes.ToArray(),
                Failure = _failure
            }; throw;
        }
        finally { _busy = false; }
        void Guard()
        {
            requireActual(invocation); _factory.RequireLifetime();
            var frame = invocation.Frame; var process = invocation.Context.Process;
            if (_fault != fault || invocation.Call == Guid.Empty || process.Actor != Lineage.Actor ||
                process.Process != Lineage.CommonProcess || process.Epoch != Lineage.Epoch ||
                process.Stack != Lineage.Stack || process.GameplayOwnership != frame.Common.GameplayOwnership ||
                invocation.Context.Context != Identity || invocation.RuntimeProcess != Lineage.RuntimeProcess ||
                frame.PlayerConstructor != Lineage.Constructor || frame.Common.Actor != Lineage.Actor ||
                frame.Common.Process != Lineage.CommonProcess || frame.Common.Epoch != Lineage.Epoch ||
                frame.ThreadId != Lineage.Thread || frame.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Controller delta lost its actual current Common pointer/Runtime/Scene epoch/lease/thread.");
        }
        void Write(string field, uint bits)
        {
            // Frame stores use a bounded latest receipt and exact counters,
            // as the same PlayerMover frame fields do. Constructor/reset
            // history remains separate and is never copied on a frame read.
            writes.Add(new(checked(++_moverDeltaSequence), field, bits, null, null, null));
            _moverDeltaCall = _moverDeltaCall! with { FlagsAfter = _flags, Stores = writes.ToArray(), Changed = _moverDeltaSequence };
        }
    }
    internal void ObserveUnownedMoverDeltaQueueWriter(string boundary)
    { RequireCurrent(); ArgumentException.ThrowIfNullOrWhiteSpace(boundary); _moverDeltaFields = _moverDeltaFields with { QueueBoundary = boundary }; }
    internal FalloutControllerMoverDeltaSnapshot CaptureMoverDelta() => new(_moverDeltaFields, _moverDeltaSequence, _moverDeltaCalls, _moverDeltaCall);
    internal void RestoreMoverDelta(FalloutControllerMoverDeltaSnapshot? saved)
    {
        if (saved is null) return;
        ValidateMoverDelta(saved); _moverDeltaFields = saved.Fields; _moverDeltaSequence = saved.Sequence;
        _moverDeltaCalls = saved.Calls; _moverDeltaCall = saved.LastCall;
    }
    internal static void ValidateMoverDelta(FalloutControllerMoverDeltaSnapshot saved)
    {
        if (saved.Fields is null || saved.Calls < 0 || saved.Sequence < 0 || (saved.Calls == 0) != (saved.LastCall is null) ||
            saved.Fields.QueueHead is not null || saved.Fields.QueueTail is not null || saved.Fields.IndependentWord is not (null or 0) ||
            saved.Fields.QueueBoundary is { } boundary && string.IsNullOrWhiteSpace(boundary))
            throw new InvalidDataException("Controller delta lost its distinct source queue/scalar fields or bounded call count.");
        if (saved.LastCall is not { } call)
        {
            if (saved.Sequence != 0 || saved.Fields.IndependentWord is not null || saved.Fields.ScalarBits is not null)
                throw new InvalidDataException("Controller delta fabricated unexecuted word/scalar stores.");
            return;
        }
        if (call.Controller == Guid.Empty || call.Invocation.Call == Guid.Empty || call.Entered < 0 || call.Changed < call.Entered ||
            call.Stores is null || call.Stores.Count > 3 || call.Entered != checked((saved.Calls - 1) * 3) ||
            call.Changed != call.Entered + call.Stores.Count || saved.Sequence != call.Changed || call.Returned != (call.Failure is null) ||
            call.FlagsAfter != (call.Stores.Count == 0 ? call.FlagsBefore : call.FlagsBefore & ~FalloutPlayerMoverDeltaSource.ControllerTransientMask) ||
            call.Returned && (!call.WorldRead || call.World is not null || call.Speed is null || call.Stores.Count != 3) ||
            call.Speed is not null && call.Speed.Actor != call.Invocation.Frame.Common.Actor)
            throw new InvalidDataException("Controller delta changed its actual pre-world stores, current cache or returned-null-world branch.");
        string[] order = ["mover-delta-flags", "mover-delta-independent-word", "mover-delta-speed-scalar"];
        for (var index = 0; index < call.Stores.Count; ++index)
            if (call.Stores[index].Field != order[index] || call.Stores[index].Sequence != call.Entered + index + 1 ||
                call.Stores[index].Bits != (index == 0 ? call.FlagsAfter : index == 1 ? 0u : saved.Fields.ScalarBits))
                throw new InvalidDataException("Controller delta reordered its real committed scalar/flag stores.");
        if (call.Stores.Count >= 2 && saved.Fields.IndependentWord != 0 ||
            call.Speed is not null && (call.Speed.Factory == Guid.Empty || call.Speed.Process != call.Invocation.RuntimeProcess ||
                call.Speed.Constructor != call.Invocation.Frame.PlayerConstructor || call.Speed.Stack != call.Invocation.Frame.Common.Stack) ||
            call.Stores.Count == 3 && (call.Speed is null || saved.Fields.ScalarBits != BitConverter.SingleToUInt32Bits(
                FalloutPhysicalFloat32.Divide(0, BitConverter.UInt32BitsToSingle(call.Speed.Bits)))))
            throw new InvalidDataException("Controller delta changed its real current cache division or committed word/scalar fields.");
    }
}
