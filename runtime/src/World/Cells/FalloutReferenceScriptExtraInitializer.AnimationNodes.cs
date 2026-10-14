using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScriptExtraInitializer
{
    internal void RequireActualAnimationRecords(FalloutPluginStack records)
    {
        if (!ReferenceEquals(records, _records))
            throw new InvalidDataException("Player animation replaced its actual retained winning records owner.");
        _records.RequireLiveLoadReceipt(_records.LoadReceipt);
    }
    internal void RequireAnimationNodeConstructorPrefix()
    {
        if (_busy || !_returnedInitialization || _records.TryGetWinner(Actor.Reference, out _) ||
            _calls.Any(call => call.InvalidatedWriter is FalloutReferenceScriptExtraWriter.ExtraCopyOrLoad or
                FalloutReferenceScriptExtraWriter.ScriptCallback || !call.Returned))
            throw new NotSupportedException("source-reference-Extra41-constructor-and-entered-membership-writer-prefix-unavailable");
    }
    internal FalloutReferenceAnimationNodeExtraRead ReadPlayerAnimationNodeExtra(FalloutPlayerReturnTailSource source,
        FalloutPlayerLateFrameInvocation frame, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        if (_busy) { ++_fault; throw new InvalidOperationException("Reference Extra node query reentered its actual initializer/list owner."); }
        requireActual(frame);
        if (frame.Common.Actor != Actor || frame.Common.Process != Process || frame.Common.Epoch != Epoch ||
            frame.PlayerProcess != _threadContext.Process || frame.PlayerConstructor != Constructor ||
            frame.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Reference Extra node query changed its actual current frame/source owner.");
        return _fields.ReadAnimationNodeExtra(source, frame, Factory, requireActual);
    }
}
