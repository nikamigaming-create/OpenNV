namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    internal FalloutPackageCommandAnimationRead BorrowReturnedAnimation(FalloutPackageCommandFieldInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy || _thread is { } thread && thread != Environment.CurrentManagedThreadId || invocation.Actor != Actor ||
            invocation.Epoch != Epoch)
            throw new InvalidOperationException("Package presentation lost its returned Actor/field/thread lifetime.");
        RequireLane(FalloutPackageCommandFieldLane.Animation);
        if (LastAnimationRead is not { Returned: true, Failure: null } read || read.Invocation != invocation)
            throw new NotSupportedException("source-returned-package-current-animation-read-unbound");
        _thread ??= Environment.CurrentManagedThreadId;
        return read;
    }
}
