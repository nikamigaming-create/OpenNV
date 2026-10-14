using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutActorMoverState
{
    private static void ValidatePlayerFrame(FalloutActorMoverSnapshot saved, FalloutActorMoverSource source, string stack,
        IReadOnlyDictionary<(Guid, Guid), FalloutActorMoverPublication> publications)
    {
        var frame = saved.PlayerFrame ?? throw new InvalidDataException("PlayerMover omitted its independent bounded frame store history.");
        if (frame.Sequence < 0 || frame.FlagCalls < 0 || frame.VectorCalls < 0 || frame.FlagStores < 0 ||
            frame.VectorXYStores < 0 || frame.VectorZStores < 0 || frame.VectorCalls > frame.FlagCalls ||
            (frame.FlagCalls != 0) != (frame.Flags is not null) ||
            (frame.VectorCalls != 0) != (frame.Vector is not null) ||
            frame.Failure is { } error && string.IsNullOrWhiteSpace(error))
            throw new InvalidDataException("PlayerMover frame lost its bounded source call/store counters or failed prefix.");
        ValidateOperation(frame.Flags, FalloutActorMoverFrameStoreKind.Flags, frame.FlagCalls);
        ValidateOperation(frame.Vector, FalloutActorMoverFrameStoreKind.DefaultVector, frame.VectorCalls);
        var flagStores = frame.Flags?.Stores.Count ?? 0;
        var vectorStores = frame.Vector?.Stores.Count ?? 0;
        if (frame.FlagStores != (frame.FlagCalls == 0 ? 0 : frame.FlagCalls - 1 + flagStores) ||
            frame.VectorXYStores != (frame.VectorCalls == 0 ? 0 : frame.VectorCalls - 1 + (vectorStores > 0 ? 1 : 0)) ||
            frame.VectorZStores != (frame.VectorCalls == 0 ? 0 : frame.VectorCalls - 1 + (vectorStores > 1 ? 1 : 0)) ||
            frame.Sequence != checked(frame.FlagCalls * 2 + frame.FlagStores + frame.VectorCalls * 2 + frame.VectorXYStores + frame.VectorZStores) ||
            frame.Sequence != Math.Max(frame.Flags?.Changed ?? 0, frame.Vector?.Changed ?? 0) ||
            frame.Failure != (frame.Flags?.Failure ?? frame.Vector?.Failure))
            throw new InvalidDataException("PlayerMover frame current counters disagree with actual entered stores and return publications.");
        if (frame.Flags is { } flags && frame.Vector is { } vector)
        {
            if (SamePlayerFrameScope(flags.Invocation, vector.Invocation))
            {
                if (!flags.Returned ||
                    vector.Invocation.Common.Changed < flags.Invocation.Common.Changed || vector.Entered != flags.Changed + 1)
                    throw new InvalidDataException("PlayerMover vector lost its same returned flag setter, current Common lineage or source order.");
            }
            else if (!vector.Returned || flags.Invocation.Caller.Ordinal <= vector.Invocation.Caller.Ordinal ||
                flags.Invocation.Caller.Main == vector.Invocation.Caller.Main || flags.Entered <= vector.Changed)
                throw new InvalidDataException("PlayerMover next flag setter crossed an unreturned or replayed previous frame child.");
        }
        if (frame.Flags is { } lastFlags && (frame.Vector is { } lastVector && SamePlayerFrameScope(lastFlags.Invocation, lastVector.Invocation)
                ? lastFlags.Entered != frame.Sequence - (2 + vectorStores) - (1 + flagStores)
                : lastFlags.Entered != frame.Sequence - (1 + flagStores)))
            throw new InvalidDataException("PlayerMover current frame store sequence differs from its complete cumulative prefixes.");

        void ValidateOperation(FalloutActorMoverFrameStoreOperation? operation, FalloutActorMoverFrameStoreKind kind, long ordinal)
        {
            if (operation is null) return;
            ValidatePlayerFrameIdentity(operation.Invocation, source, stack, saved.Actor);
            var maximum = kind == FalloutActorMoverFrameStoreKind.Flags ? 1 : 2;
            if (operation.Factory == Guid.Empty || !publications.ContainsKey((operation.Invocation.PlayerProcess, operation.Factory)) ||
                operation.Kind != kind || operation.CallOrdinal != ordinal || ordinal < 1 || operation.Stores is null ||
                operation.Stores.Count > maximum || operation.Returned != (operation.Failure is null) ||
                operation.Returned && operation.Stores.Count != maximum ||
                operation.Failure is { } failure && string.IsNullOrWhiteSpace(failure) ||
                operation.Entered < 1 || operation.Changed > frame.Sequence ||
                operation.Changed != operation.Entered + operation.Stores.Count + 1)
                throw new InvalidDataException("PlayerMover frame operation lost its actual publication, store extent or entered/returned boundary.");
            if (kind == FalloutActorMoverFrameStoreKind.Flags ?
                operation.RequestedWord is null || operation.PreviousFlags is not (null or 0) ||
                    operation.Stores.Count != 0 && (operation.RequestedWord != 0 || operation.PreviousFlags != 0) ||
                    operation.RequestedWord != 0 && operation.PreviousFlags is not null && operation.Failure != FrameWordBoundary :
                operation.RequestedWord is not null || operation.PreviousFlags is not null)
                throw new InvalidDataException("PlayerMover frame changed its actual zero-word/vector branch or invented a skipped field read.");
            FalloutActorMoverField[] order = kind == FalloutActorMoverFrameStoreKind.Flags ?
                [FalloutActorMoverField.PlayerFlags] : [FalloutActorMoverField.PlayerVectorXY, FalloutActorMoverField.PlayerVectorZ];
            for (var index = 0; index < operation.Stores.Count; ++index)
            {
                var store = operation.Stores[index];
                if (store is null || store.Field != order[index] || store.Bits != 0 ||
                    store.SecondBits != (store.Field == FalloutActorMoverField.PlayerVectorXY ? 0u : null) ||
                    store.Changed != operation.Entered + index + 1)
                    throw new InvalidDataException("PlayerMover frame reordered its full flags or paired-XY/then-Z source stores.");
            }
        }
    }

    private static void ValidatePlayerFrameIdentity(FalloutActorMoverFrameInvocation invocation,
        FalloutActorMoverSource source, string stack, FalloutCombatActorIdentity? actor)
    {
        if (invocation is null || invocation.Caller is not { } caller || caller.Main == Guid.Empty || caller.Ordinal < 1 ||
            caller.Invocation == Guid.Empty || invocation.PlayerProcess == Guid.Empty || invocation.ThreadId < 1 ||
            invocation.Common is not { } common || actor is null || common.Actor != actor || !actor.EnginePlayer ||
            common.ExecutableSha256 != source.Controls.Main.EngineSha256 || common.Stack != stack || common.Process == Guid.Empty ||
            common.Epoch < 1 || common.Level != FalloutDetectionProcessLevel.High || common.Changed < 1 || common.GameplayOwnership == Guid.Empty)
            throw new InvalidDataException("PlayerMover frame omitted actual Main/Prelude/Actor/Common/epoch/thread or source lifetime.");
        actor.Validate();
    }
    private static bool SamePlayerFrameScope(FalloutActorMoverFrameInvocation first, FalloutActorMoverFrameInvocation second) =>
        first.Caller == second.Caller && first.PlayerProcess == second.PlayerProcess && first.ThreadId == second.ThreadId &&
        first.Common.Actor == second.Common.Actor && first.Common.Process == second.Common.Process && first.Common.Epoch == second.Common.Epoch &&
        first.Common.Level == second.Common.Level && first.Common.GameplayOwnership == second.Common.GameplayOwnership &&
        first.Common.Stack == second.Common.Stack && first.Common.ExecutableSha256 == second.Common.ExecutableSha256;
}
