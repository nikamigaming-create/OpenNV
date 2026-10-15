using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutLoadedAnimationGroupRead(FalloutPlayerLateFrameInvocation Frame, Guid Context,
    Guid Factory, long Sequence, ushort Word);
internal sealed record FalloutLoadedAnimationMovementRead(FalloutPlayerLateFrameInvocation Frame,
    Guid Context, Guid Factory, long Sequence, Guid? ModelFactory, FalloutActorMoverVector Delta,
    uint? MoverFlags, FalloutReferenceActorYawModeRead? Mode, FalloutReferenceScaleRead? Scale,
    FalloutReferenceFlagRead Flags, IReadOnlyList<uint> Rotation, bool Returned, FalloutActorMoverVector Before);
internal sealed partial class FalloutSourceAnimationContextState
{
    internal FalloutLoadedAnimationGroupRead ReadMovementGroup(FalloutPlayerLateFrameInvocation frame,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    { RequireMovementFrame(frame, requireActual); return new(frame, Identity, Factory, _sequence, _currentGroups[1]); }

    internal FalloutLoadedAnimationMovementRead ReadMovementVector(FalloutPlayerLateFrameInvocation frame,
        FalloutReferenceFlagRead flags, Func<FalloutReferenceActorYawModeRead> readMode,
        Func<FalloutReferenceScaleRead> readScale, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireMovementFrame(frame, requireActual);
        if (flags.Actor != Actor || flags.Process != CommonProcess || flags.Constructor != Construction.Invocation.ReferenceConstructor ||
            flags.Epoch != Construction.Invocation.Epoch)
            throw new InvalidOperationException("Animation vector borrowed another actual reference flag word.");
        if (_root is null) return new(frame, Identity, Factory, _sequence, null, FalloutActorMoverVector.Default,
            null, null, null, flags, [], false, FalloutActorMoverVector.Default);
        _root.RequireCurrent(); var vector = _movementDelta; var matrix = _root.LogicalScene.Root.Local.RotationRowMajor;
        var mover = (_loadedOwners ?? throw new NotSupportedException("source-animation-vector-current-mover-owner-unbound")).Mover.ReadPlayerFields();
        if ((mover.Flags & 0xf) != 0 && _selected[1] is not null)
            throw new NotSupportedException("source-AnimData-active-locomotion-speed-clamp-and-secondary-sequence-child-unowned");
        var mode = readMode(); RequireMovementFrame(frame, requireActual); FalloutReferenceScaleRead? scale = null;
        if (mode.Actor != Actor || mode.Process != Process || mode.Constructor != Construction.Invocation.ReferenceConstructor)
            throw new InvalidOperationException("Animation vector changed the same raw Actor19C field owner.");
        if (mode.Mode is 3 or 5 or 8 or 10)
        {
            scale = readScale(); RequireMovementFrame(frame, requireActual);
            if (scale.Actor != Actor || scale.Process != Process || scale.Constructor != Construction.Invocation.ReferenceConstructor)
                throw new InvalidOperationException("Animation vector changed the current reference scale owner.");
            var value = BitConverter.UInt32BitsToSingle(scale.Bits); vector.X /= value; vector.Y /= value;
        }
        var direct = (flags.Flags & FalloutPlayerMoverDeltaSource.DirectPositionFlag) != 0;
        if (!direct) vector.Z = 0;
        else vector = new(Row(0), Row(3), Row(6));
        RequireMovementFrame(frame, requireActual);
        return new(frame, Identity, Factory, _sequence, _root.Node.Factory, Vector(vector), mover.Flags,
            mode, scale, flags, matrix.Select(BitConverter.SingleToUInt32Bits).ToArray(), true, Vector(_movementDelta));
        float Row(int row) => ((matrix[row] * vector.X) + 0f) + (matrix[row + 1] * vector.Y) + (matrix[row + 2] * vector.Z);
    }
    private void RequireMovementFrame(FalloutPlayerLateFrameInvocation frame, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireIdle(); requireActual(frame);
        if (frame.PlayerProcess != Process || frame.PlayerConstructor != Construction.Invocation.ReferenceConstructor ||
            frame.Common.Process != CommonProcess || frame.Common.Actor != Actor || frame.Common.Epoch != Construction.Invocation.Epoch ||
            frame.ThreadId != _thread || _failure is not null || _writer is not null)
            throw new InvalidOperationException("Animation vector/group read lost its actual current source context and frame.");
    }
}
