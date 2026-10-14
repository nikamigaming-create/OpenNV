using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// The reference flag word remains in this existing owner. Reference position
// and the shared change manager borrow it; no second flag authority is created.
internal sealed partial class FalloutReferenceScriptExtraInitializer
{
    private readonly List<FalloutReferenceInitializedFlagWrite> _referenceFlagWrites = [];

    internal FalloutReferenceFlagRead ReadReferenceFlags(FalloutReferencePositionInvocation invocation,
        Action<FalloutReferencePositionInvocation> requireActual)
    {
        var fault = _fault;
        RequireReferenceFlagBorrow(invocation, requireActual, fault);
        var flags = _referenceFlags;
        RequireReferenceFlagBorrow(invocation, requireActual, fault);
        return new(Source.Identity, _fields.Stack, Process, Factory, FieldsFactory, Constructor,
            Actor, Epoch, _sequence, flags);
    }

    internal FalloutReferenceInitializedFlagWrite SetReferenceInitialized(FalloutReferencePositionInvocation invocation,
        bool initialized, Action<FalloutReferencePositionInvocation> requireActual) =>
        StoreReferenceFlag(invocation, initialized, FalloutReferenceScriptExtraInitializerSource.InitializedFlag, null, requireActual);

    internal FalloutReferenceInitializedFlagWrite SetBaseDestructibleFlag(FalloutReferencePositionInvocation invocation,
        FalloutReferenceBaseAssignmentInputs inputs, Action<FalloutReferencePositionInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(inputs); inputs.Validate(FalloutReferencePositionSource.Reviewed(Source.Fields), Actor);
        return StoreReferenceFlag(invocation, inputs.Destructible, FalloutReferenceBaseAssignmentInputs.DestructibleReferenceFlag, inputs, requireActual);
    }
    private FalloutReferenceInitializedFlagWrite StoreReferenceFlag(FalloutReferencePositionInvocation invocation,
        bool enabled, uint mask, FalloutReferenceBaseAssignmentInputs? assignment, Action<FalloutReferencePositionInvocation> requireActual)
    {
        var fault = _fault;
        RequireReferenceFlagBorrow(invocation, requireActual, fault);
        if (!_invocations.Add(invocation.Call)) throw new InvalidOperationException("Reference initialized setter repeated its original caller.");
        var before = _referenceFlags; var entered = checked(++_sequence);
        _busy = true; string? failure = null;
        try
        {
            Guard();
            _referenceFlags = enabled ? before | mask : before & ~mask;
            Guard();
        }
        catch (Exception error)
        { failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message; _failure ??= failure; throw; }
        finally
        {
            _referenceFlagWrites.Add(new(invocation, Source.Identity, Factory, FieldsFactory, entered,
                checked(++_sequence), before, _referenceFlags, enabled, failure is null, failure, mask, assignment));
            _busy = false;
        }
        return _referenceFlagWrites[^1];

        void Guard()
        {
            requireActual(invocation);
            if (_fault != fault || invocation.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Reference initialized setter lost its actual source caller or swallowed reentry.");
        }
    }

    private void RequireReferenceFlagBorrow(FalloutReferencePositionInvocation invocation,
        Action<FalloutReferencePositionInvocation> requireActual, long fault)
    {
        ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(requireActual);
        if (_busy) { _fault++; throw new InvalidOperationException("Reference flag owner is already entered."); }
        requireActual(invocation);
        if (invocation.Call == Guid.Empty || invocation.Actor != Actor || invocation.CommonProcess != Process ||
            invocation.ReferenceProcess != _threadContext.Process || invocation.ReferenceConstructor != Constructor ||
            invocation.Epoch != Epoch || invocation.ThreadId != Environment.CurrentManagedThreadId || _fault != fault ||
            _thread is { } thread && thread != invocation.ThreadId)
            throw new InvalidOperationException("Reference flag borrow changed its actual reference, Common, Scene, constructor or thread.");
        if (_calls.LastOrDefault(IsReferenceFlagWriter)?.WriterBoundary is { } boundary)
            throw new NotSupportedException(boundary);
    }

    private static bool IsReferenceFlagWriter(FalloutReferenceScriptExtraInitializationCall call) =>
        call.InvalidatedWriter is FalloutReferenceScriptExtraWriter.ReferenceFlags or FalloutReferenceScriptExtraWriter.ScriptCallback;
}
