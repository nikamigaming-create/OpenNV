using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessAnimationQueueFields(Guid? Head, Guid? Next, string? Writer = null)
{
    internal static FalloutProcessAnimationQueueFields Constructed => new(null, null);
}
internal sealed record FalloutProcessAnimationQueueCall(FalloutPlayerLateFrameInvocation Frame,
    FalloutProcessSceneQuery Common, uint ClockKind, FalloutPlayerTailSettingRead? Delay,
    Guid? Head, Guid? Next, long Sequence, bool Returned);
internal sealed record FalloutProcessAnimationContextInvalidation(FalloutReferenceModelDataInvocation Invocation,
    long Changed, string Boundary);

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutProcessSceneQuery ReadAnimationContextForReturnTail(FalloutPlayerLateFrameInvocation frame,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(requireActual); requireActual(frame);
        var process = ReadSceneQueries(frame.Common.Actor.Reference, frame.Common.Epoch);
        RequireTailProcess(process, frame);
        if (Require(process.Actor.Reference).SceneFields?.AnimationContextWriter is { } boundary)
            throw new NotSupportedException(boundary);
        requireActual(frame); return process;
    }

    internal FalloutProcessAnimationQueueCall UpdatePlayerAnimationPairs(FalloutPlayerReturnTailSource source,
        FalloutPlayerLateFrameInvocation frame, FalloutPlayerAnimationClockRead clock, FalloutPluginStack records,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        source.Validate(); RequireNotBusy(); ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(requireActual); requireActual(frame);
        var current = ReadSceneQueries(frame.Common.Actor.Reference, frame.Common.Epoch); RequireTailProcess(current, frame);
        if (source.Scene.Main.EngineSha256 != _source.ExecutableSha256 || clock.Frame != frame)
            throw new InvalidDataException("Player pair queue changed the source clock or selected process.");
        // Lower virtuals are independently qualified pure returns. High reads
        // the clock kind and selected setting before either inline-list cell.
        if (current.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh))
        { requireActual(frame); return new(frame, current, clock.Kind, null, null, null, current.Changed, true); }
        var name = clock.Kind == 4 ? FalloutPlayerReturnTailSource.PairVatsDelayName : FalloutPlayerReturnTailSource.PairDelayName;
        var receipt = records.LoadReceipt; records.RequireLiveLoadReceipt(receipt);
        var delay = records.NumericSettings.Float(name); requireActual(frame); records.RequireLiveLoadReceipt(receipt);
        if (!float.IsFinite(delay)) throw new NotSupportedException("source-High-animation-pair-nonfinite-delay-unowned");
        var setting = new FalloutPlayerTailSettingRead(name, BitConverter.SingleToUInt32Bits(delay), receipt.Factory, receipt.SourceHash);
        var fields = Require(current.Actor.Reference).SceneFields?.AnimationPairs ??
            throw new NotSupportedException("source-High-animation-pair-inline-constructor-unavailable");
        if (fields.Writer is { } writer) throw new NotSupportedException(writer);
        if (fields.Next is not null || fields.Head is not null)
            throw new NotSupportedException("source-High-animation-pair-current-node-payload-retain-release-and-head-removal-unowned");
        // No source store or node creation occurs on the genuine empty arm.
        requireActual(frame); return new(frame, current, clock.Kind, setting, fields.Head, fields.Next, current.Changed, true);
    }

    internal void ObserveEnteredPlayerAnimationLoad(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActualLoad)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(requireActualLoad); requireActualLoad(invocation);
        var process = ReadSceneQueries(invocation.Actor.Reference, invocation.Epoch);
        if (invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || invocation.Call == Guid.Empty ||
            invocation.CommonProcess != _process || process.Actor != invocation.Actor ||
            invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Animation process writer lost its actual entered Player 3D load.");
        var current = Require(invocation.Actor.Reference);
        if (current.SceneFields is not { } fields)
            throw new NotSupportedException("source-Player-animation-process-current-constructor-unavailable");
        var changed = Next();
        const string boundary = "source-Player-three-D-load-Common-AnimData-factory-store-unowned";
        _current[invocation.Actor.Reference] = current with
        {
            SceneFields = fields with
            {
                AnimationContextWriter = boundary,
                AnimationContextLoad = new(invocation, changed, boundary)
            },
            Changed = changed
        };
        requireActualLoad(invocation);
    }

    internal void ObserveUnownedAnimationPairWriter(FalloutFormKey actor, long epoch, string producer, Action requireActualWriter)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(producer); ArgumentNullException.ThrowIfNull(requireActualWriter);
        requireActualWriter(); _ = ReadSceneQueries(actor, epoch); var current = Require(actor);
        var fields = current.SceneFields?.AnimationPairs ?? throw new NotSupportedException("source-animation-pair-constructor-unavailable");
        _current[actor] = current with
        {
            SceneFields = current.SceneFields! with
            { AnimationPairs = fields with { Writer = "source-animation-pair-current-writer-unowned:" + producer } },
            Changed = Next()
        };
        requireActualWriter();
    }
    private static void RequireTailProcess(FalloutProcessSceneQuery process, FalloutPlayerLateFrameInvocation frame)
    {
        if (process.Process != frame.Common.Process || process.Stack != frame.Common.Stack || process.Actor != frame.Common.Actor ||
            process.Epoch != frame.Common.Epoch || process.Level != frame.Common.Level ||
            process.GameplayOwnership != frame.Common.GameplayOwnership || process.ExecutableSha256 != frame.Common.ExecutableSha256 ||
            frame.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player animation changed its actual current process class/epoch/lease.");
    }
    private static void ValidatePlayerAnimationQueueFields(FalloutProcessCommonEntry entry, string executable)
    {
        var expected = FalloutSourceMainFamily.IsFallout3(executable) &&
            entry.Level is FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh;
        if (expected != (entry.SceneFields?.AnimationPairs is not null) ||
            entry.SceneFields is { AnimationContextWriter: { Length: 0 } } ||
            entry.SceneFields is { } scene && (scene.AnimationContextWriter is null) != (scene.AnimationContextLoad is null) ||
            entry.SceneFields?.AnimationPairs is { } queue &&
                (queue.Head == Guid.Empty || queue.Next == Guid.Empty || queue.Writer is { Length: 0 } ||
                 queue.Writer is null && (queue.Head is not null || queue.Next is not null)))
            throw new InvalidDataException("Animation queue lost its inherited inline cells or admitted an unowned writer.");
        if (entry.SceneFields?.AnimationContextLoad is { } writer &&
            (writer.Boundary != entry.SceneFields.AnimationContextWriter || writer.Changed < 1 || writer.Changed > entry.Changed ||
             writer.Invocation.Call == Guid.Empty || writer.Invocation.Actor != entry.Source || writer.Invocation.Epoch != entry.Epoch ||
             writer.Invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || writer.Invocation.ReferenceProcess == Guid.Empty ||
             writer.Invocation.ReferenceConstructor == Guid.Empty || writer.Invocation.CommonProcess == Guid.Empty || writer.Invocation.ThreadId < 1))
            throw new InvalidDataException("Animation context invalidation lost its actual entered Player 3D-load receipt.");
    }
}
