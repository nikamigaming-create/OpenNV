using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScriptExtraInitializer
{
    // A frame borrow has its own typed caller. It is not a fabricated position
    // initializer or compiled package command used to bypass their admission.
    internal FalloutReferenceFlagRead ReadFrameReferenceFlags(FalloutPlayerLateFrameInvocation invocation,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(requireActual);
        var fault = _fault; Check(); var flags = _referenceFlags; Check();
        return new(Source.Identity, _fields.Stack, Process, Factory, FieldsFactory, Constructor,
            Actor, Epoch, _sequence, flags);
        void Check()
        {
            if (_busy) { _fault++; throw new InvalidOperationException("Reference flag owner reentered a frame borrow."); }
            requireActual(invocation);
            if (invocation.Common.Actor != Actor || invocation.Common.Process != Process ||
                invocation.PlayerProcess != _threadContext.Process || invocation.PlayerConstructor != Constructor ||
                invocation.Common.Epoch != Epoch || invocation.ThreadId != Environment.CurrentManagedThreadId ||
                _thread is { } thread && thread != invocation.ThreadId || _fault != fault)
                throw new InvalidOperationException("Frame flag borrow changed the actual reference/Common/Scene constructor or thread.");
            if (_calls.LastOrDefault(IsReferenceFlagWriter)?.WriterBoundary is { } boundary)
                throw new NotSupportedException(boundary);
        }
    }
}
