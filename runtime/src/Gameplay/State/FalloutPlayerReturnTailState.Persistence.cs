using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerReturnTailState
{
    internal static void Validate(FalloutPlayerReturnTailSnapshot saved)
    {
        saved.Source.Validate();
        if (string.IsNullOrWhiteSpace(saved.Stack) || saved.Process == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Constructor == Guid.Empty || saved.CommonProcess == Guid.Empty || saved.Sequence < 0 || saved.Calls < 0 ||
            saved.Publications is not { Count: > 0 } || (saved.Calls == 0) != (saved.LastCall is null) ||
            saved.DeathTimerWriter is { Length: 0 } || saved.Failure is { Length: 0 })
            throw new InvalidDataException("Player animation tail lost its actual bounded owner/history.");
        var processes = new HashSet<Guid>(); var factories = new Dictionary<Guid, FalloutPlayerReturnTailPublication>();
        var constructors = new HashSet<Guid>(); var commons = new HashSet<Guid>();
        for (var index = 0; index < saved.Publications.Count; index++)
        {
            var row = saved.Publications[index];
            if (row.Process == Guid.Empty || row.Factory == Guid.Empty || row.Constructor == Guid.Empty || row.CommonProcess == Guid.Empty ||
                !processes.Add(row.Process) || !constructors.Add(row.Constructor) || !commons.Add(row.CommonProcess) ||
                !factories.TryAdd(row.Factory, row) || row.Published < 0 || row.Published > saved.Sequence ||
                (index == 0 ? row.Published != 0 || row.PreviousProcess is not null || row.PreviousFactory is not null :
                    row.PreviousProcess != saved.Publications[index - 1].Process || row.PreviousFactory != saved.Publications[index - 1].Factory ||
                    row.Published < saved.Publications[index - 1].Published))
                throw new InvalidDataException("Player tail reused or disconnected independent cold source owners.");
        }
        var current = saved.Publications[^1];
        if (current.Process != saved.Process || current.Factory != saved.Factory || current.Constructor != saved.Constructor ||
            current.CommonProcess != saved.CommonProcess)
            throw new InvalidDataException("Player tail lost its actual current publication.");
        if (saved.LastCall is not { } call) return;
        ValidateAnimationCall(call, saved.Source, saved.Stack);
        if (!factories.TryGetValue(call.Factory, out var publication) || call.Invocation.PlayerProcess != publication.Process ||
            call.Invocation.PlayerConstructor != publication.Constructor || call.Invocation.Common.Process != publication.CommonProcess ||
            call.Ordinal != saved.Calls || call.Entered <= publication.Published || call.Changed > saved.Sequence ||
            call.Failure != saved.Failure || call.Stores.LastOrDefault(row => row.Field == FalloutPlayerReturnTailField.DeathTimer) is { } timer &&
                timer.After != saved.DeathTimerBits)
            throw new InvalidDataException("Player tail latest call replaced its original factory, current timer or failure.");
    }

    internal static void ValidateAnimationCall(FalloutPlayerReturnTailCall call, FalloutPlayerReturnTailSource source, string stack)
    {
        var frame = call.Invocation;
        if (call.Factory == Guid.Empty || call.Ordinal < 1 || call.Entered < 1 || call.Changed <= call.Entered ||
            frame.Caller.Main == Guid.Empty || frame.Caller.Ordinal < 1 || frame.Caller.Invocation == Guid.Empty ||
            frame.PlayerProcess == Guid.Empty || frame.PlayerConstructor == Guid.Empty || frame.Prelude == Guid.Empty || frame.ThreadId < 1 ||
            frame.Common.Process == Guid.Empty || frame.Common.Stack != stack || frame.Common.Epoch < 1 || !frame.Common.Actor.EnginePlayer ||
            frame.Common.ExecutableSha256 != source.Scene.Main.EngineSha256 || call.Stores is null || call.Stores.Count > 4 ||
            call.Animations is null || call.Animations.Count > 2 || call.ContextUpdates is null || call.ContextUpdates.Count > 2 ||
            call.Returned != (call.Failure is null) || call.Failure is { Length: 0 } ||
            (call.Returned ? call.Step != FalloutPlayerReturnTailStep.Returned || call.FailedAt is not null :
                call.Step != FalloutPlayerReturnTailStep.Failed || call.FailedAt is null || !Enum.IsDefined(call.FailedAt.Value) ||
                call.FailedAt is FalloutPlayerReturnTailStep.Returned or FalloutPlayerReturnTailStep.Failed))
            throw new InvalidDataException("Player tail replaced its actual entered source frame or returned failure boundary.");
        var at = call.FailedAt ?? FalloutPlayerReturnTailStep.Returned;
        if (at > FalloutPlayerReturnTailStep.DeathDelay && call.Delay is null ||
            at > FalloutPlayerReturnTailStep.RawLife && call.RawLife is null ||
            at > FalloutPlayerReturnTailStep.AnimationClock && call.Clock is null ||
            at > FalloutPlayerReturnTailStep.CommonAnimation && call.Animations.Count < 1 ||
            at > FalloutPlayerReturnTailStep.FirstAnimation && call.Animations.Count != 2 ||
            at > FalloutPlayerReturnTailStep.ProcessAnimationQueue && call.Queue is not { Returned: true } ||
            at > FalloutPlayerReturnTailStep.ReferenceAnimationExtra && call.Extra is null)
            throw new InvalidDataException("Player tail omitted a reached earlier scalar or mandatory animation child.");
        if (call.Delay is { } delay && (delay.Name != FalloutPlayerReturnTailSource.DeathDelayName ||
            delay.RecordsFactory == Guid.Empty || !FalloutPlayerPhysicalSource.Digest(delay.RecordsSource) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(delay.Bits))))
            throw new InvalidDataException("Player death-delay read lost its real winning records owner.");
        long sequence = call.Entered;
        FalloutPlayerAnimationBracketStore? enteredBracket = null;
        foreach (var store in call.Stores)
        {
            if (store.Changed <= sequence || store.Changed >= call.Changed || !Enum.IsDefined(store.Field))
                throw new InvalidDataException("Player tail omitted/reordered an actual committed scalar store.");
            sequence = store.Changed;
            if (store.Field == FalloutPlayerReturnTailField.AnimationBracket)
            {
                var bracket = store.SharedBracket ?? throw new InvalidDataException("Animation bracket store lost the SAME Scene owner.");
                if (bracket.Source != source.Scene.Identity || bracket.Frame != frame || bracket.SceneProcess != frame.PlayerProcess ||
                    bracket.SceneConstructor != frame.PlayerConstructor || bracket.ConsumerFactory != call.Factory || bracket.Changed < 1 ||
                    bracket.Child != FalloutPlayerAnimationBracketChild.Updater || store.Before != bracket.Before || store.After != bracket.After ||
                    bracket.After != unchecked((byte)(bracket.Before + (bracket.Enter ? 1 : -1))))
                    throw new InvalidDataException("Player animation changed its actual shared Scene bracket writer.");
                if (enteredBracket is null)
                {
                    if (!bracket.Enter) throw new InvalidDataException("Animation bracket returned before entering its actual child.");
                    enteredBracket = bracket;
                }
                else if (bracket.Enter || bracket.Before != enteredBracket.After || bracket.After != enteredBracket.Before)
                    throw new InvalidDataException("Animation bracket failed to restore the exact nested outer value.");
            }
            else if (store.SharedBracket is not null)
                throw new InvalidDataException("Death timer borrowed a Scene bracket writer.");
        }
        var bracketCount = call.Stores.Count(row => row.Field == FalloutPlayerReturnTailField.AnimationBracket);
        if (at > FalloutPlayerReturnTailStep.AnimationBracketEnter && bracketCount < 1 || call.Returned && bracketCount != 2 ||
            !call.Returned && at < FalloutPlayerReturnTailStep.AnimationBracketLeave && bracketCount > 1)
            throw new InvalidDataException("Player animation omitted an entered bracket or restored across a failed child.");
        for (var index = 0; index < call.Animations.Count; index++)
        {
            var read = call.Animations[index];
            if (read.Frame != frame || index == 0 && (read.Common is null || read.First is not null) ||
                index == 1 && (read.Common is not null || read.First is null) ||
                read.Common is { } common && (common.Actor != frame.Common.Actor || common.Epoch != frame.Common.Epoch ||
                    common.Process != frame.Common.Process || common.GameplayOwnership != frame.Common.GameplayOwnership) ||
                read.First is { } first && (first.Actor != frame.Common.Actor || first.Epoch != frame.Common.Epoch ||
                    first.Process != frame.Common.Process || first.Constructor != frame.PlayerConstructor))
                throw new InvalidDataException("Player animation reordered or aliased its independent Common/first pointer borrows.");
        }
        if (call.Clock is { } clock) RequireClock(clock, frame);
        if (call.Queue is { } queue && (queue.Frame != frame || queue.Common.Process != frame.Common.Process ||
            queue.Common.Actor != frame.Common.Actor || queue.Common.Epoch != frame.Common.Epoch || queue.Head is not null || queue.Next is not null))
            throw new InvalidDataException("Player animation substituted its actual current pair queue.");
        if (call.Extra is { } extra && (extra.Frame != frame || extra.Source != source.Identity || extra.Stack != stack ||
            extra.Process != frame.Common.Process || extra.Actor != frame.Common.Actor || extra.Epoch != frame.Common.Epoch ||
            extra.Constructor != frame.PlayerConstructor || extra.FieldsFactory == Guid.Empty || extra.InitializerFactory == Guid.Empty ||
            extra.Sequence < 0 || extra.Extra is not null || extra.Payload is not null))
            throw new InvalidDataException("Player animation Extra read changed its actual list/membership source.");
        if (call.Returned && call.ContextUpdates.Count != call.Animations.Count(row => row.Context is not null) ||
            call.ContextUpdates.Any(row => row.Frame != frame || row.Factory == Guid.Empty || row.Context == Guid.Empty) ||
            call.Returned && call.ContextUpdates.Any(row => !row.Returned || row.Failure is not null))
            throw new InvalidDataException("Player animation omitted or substituted a reached real AnimData update.");
    }
}
