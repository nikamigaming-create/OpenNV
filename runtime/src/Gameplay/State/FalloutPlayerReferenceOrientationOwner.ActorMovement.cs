using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerReferenceOrientationOwner
{
    internal FalloutPlayerFrameAnglesRead ReadActorMovementAngles(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireActorMovementFrame(invocation, requireActual); var read = new FalloutPlayerFrameAnglesRead(_source.Identity,
            Stack, Process, Factory, Binding, _sequence, new(_x, _y, _z));
        RequireActorMovementFrame(invocation, requireActual); return read;
    }
    internal FalloutPlayerFrameAnglesRead ResetExceptionalActorMovementAngles(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual, IFalloutReferenceOrientationChangeConsumer consumer)
    {
        RequireActorMovementFrame(invocation, requireActual); ArgumentNullException.ThrowIfNull(consumer);
        var before = new FalloutPhysicalVector3(_x, _y, _z); var entered = Next(); var fault = _reentry; _writing = true;
        // The actual vector setter pairs X/Y, stores Z, then normalizes the
        // Character Z field before the mandatory reference change child.
        _x = 0; _y = 0; _z = 0; _stores = checked(_stores + 3); Next();
        _z = FalloutPlayerControlOrientation.NormalizeBits(_z); _stores = checked(_stores + 1); Next();
        _call = new(invocation.Caller.Invocation, "actual-Actor-movement-exceptional-raw-vector-reset",
            "raw-reference-vector-reset", entered, Next(), null, 0, before.Z, _z,
            FalloutReferenceOrientationStep.ChangeCallback, 2, consumer.Factory, false, null);
        try
        {
            consumer.RequireCurrent(); Check();
            if (consumer.Source != _source || consumer.Stack != Stack || consumer.Process != Process ||
                consumer.Reference != Binding.Reference || consumer.Factory == Guid.Empty)
                throw new InvalidDataException("Exceptional rotation reset changed its actual mask2 child owner.");
            consumer.Changed(invocation.Caller.Invocation, 2); Check(); consumer.RequireCurrent(); Check();
            _call = _call with { Step = FalloutReferenceOrientationStep.Returned, Returned = true, Changed = Next() };
        }
        catch (Exception error) { _failure ??= error.Message; _call = _call with { Failure = error.Message, Changed = Next() }; throw; }
        finally { _writing = false; }
        return ReadActorMovementAngles(invocation, requireActual);
        void Check()
        {
            requireActual(invocation);
            if (_reentry != fault || !_living() || _thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Raw rotation vector reset changed its actual writer/thread/Player scope.");
        }
    }
    private void RequireActorMovementFrame(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireOwner(); requireActual(invocation);
        if (invocation.PlayerProcess != Process || invocation.Common.Stack != Stack ||
            invocation.Common.Actor.Reference != Binding.Reference || invocation.Common.Actor.Base != Binding.Base ||
            invocation.ThreadId != _thread)
            throw new InvalidDataException("Actor movement borrowed another actual raw reference orientation owner.");
    }
}
