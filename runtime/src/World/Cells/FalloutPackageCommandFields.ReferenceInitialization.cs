using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    internal const string SavedInitializationKind = "constructed-SavedPosition-Extra-prefix";
    internal FalloutPackageCommandSavedPositionInitialization? LastSavedPositionInitialization { get; private set; }
    internal bool HasConstructedSavedPositionInitialization => LastSavedPositionInitialization is not null;

    internal FalloutPackageCommandSavedPositionInitialization InitializeConstructedPlayerSavedPosition(
        FalloutPackageCommandFieldInvocation invocation, FalloutReferencePositionPrefix prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (LastSavedPositionInitialization is not null)
            throw new InvalidOperationException("SavedPosition constructor prefix cannot replay or retry.");
        Execute(invocation, SavedInitializationKind, scope =>
        {
            ValidateSavedPrefix(prefix, invocation, Source);
            if (_values.SavedPosition is not null || _values.PendingSavedPosition is not null ||
                _operations.Any(value => value.InvalidatedLane == FalloutPackageCommandFieldLane.SavedPosition ||
                    value.Stores.Any(store => IsSavedPositionField(store.Field))))
                throw new NotSupportedException("source-SavedPosition-preceding-Extra-writer-unbound");
            return true;
        }, scope => scope.SavedPositionInitialization = new(invocation, Source.Identity, Factory, Constructor,
            scope.Entered, scope.Entered, prefix, false, null));
        Admit(FalloutPackageCommandFieldLane.SavedPosition);
        return LastSavedPositionInitialization!;
    }

    private static bool IsSavedPositionField(FalloutPackageCommandField field) => field is
        FalloutPackageCommandField.SavedConstruction or FalloutPackageCommandField.SavedPresence or
        FalloutPackageCommandField.SavedSpace or FalloutPackageCommandField.SavedX or FalloutPackageCommandField.SavedY or
        FalloutPackageCommandField.SavedZ or FalloutPackageCommandField.SavedRotationZ;

    private static void ValidateSavedPrefix(FalloutReferencePositionPrefix prefix,
        FalloutPackageCommandFieldInvocation invocation, FalloutPackageCommandFieldsSource source)
    {
        if (!prefix.Returned || prefix.Failure is not null || prefix.Source != FalloutReferencePositionSource.Reviewed(source).Identity ||
            prefix.Factory == Guid.Empty || prefix.Constructor == Guid.Empty || prefix.Invocation.Actor != invocation.Actor ||
            prefix.Invocation.Epoch != invocation.Epoch || prefix.Invocation.CommonProcess != invocation.Process ||
            prefix.BaseSha256 != invocation.Actor.BaseSha256 || prefix.ReferenceRecordPresent || prefix.ConfiguredCell != "" ||
            prefix.ConfiguredX != "" || prefix.Dependencies is not { Count: >= 3 })
            throw new InvalidDataException("SavedPosition admission lost its actual returned independent reference writer prefix.");
    }

    private static void ValidatePositionBorrow(FalloutReferencePositionRead read, FalloutPackageCommandFieldInvocation invocation,
        FalloutPackageCommandFieldsSource source, string stack)
    {
        if (read.Source != FalloutReferencePositionSource.Reviewed(source).Identity || read.Stack != stack || read.Actor != invocation.Actor ||
            read.Process == Guid.Empty || read.Factory == Guid.Empty || read.Constructor == Guid.Empty || read.Sequence < 0 || read.Mode != 0)
            throw new InvalidDataException("Saved position lost its actual raw current-reference position owner.");
    }
    private static void ValidateParentBorrow(FalloutReferenceParentCellRead read, FalloutPackageCommandFieldInvocation invocation,
        FalloutPackageCommandFieldsSource source, string stack)
    {
        if (string.IsNullOrWhiteSpace(read.Source) || read.Stack != stack || read.Actor != invocation.Actor ||
            read.Process == Guid.Empty || read.Factory == Guid.Empty || read.Constructor == Guid.Empty || read.Sequence < 0 ||
            read.ExecutableSha256 != source.Controls.Main.EngineSha256)
            throw new InvalidDataException("Saved position lost its actual World current ParentCELL owner.");
        ValidateForm(read.Cell);
    }
    private static void ValidateWorldspaceBorrow(FalloutReferenceWorldspaceRead read, FalloutPackageCommandFieldInvocation invocation,
        FalloutPackageCommandFieldsSource source, string stack)
    {
        if (read.Source != FalloutReferencePositionSource.Reviewed(source).Identity || read.Stack != stack || read.Actor != invocation.Actor ||
            read.Process == Guid.Empty || read.Factory == Guid.Empty || read.Constructor == Guid.Empty || read.Sequence < 0 ||
            !read.PersistentPresent && read.PersistentCell is not null ||
            read.Parent.Cell is not null && (read.Flags is not null || read.PersistentLookup) ||
            read.Parent.Cell is null && read.Flags is null ||
            read.Parent.Cell is null && (!read.PersistentLookup || !read.PersistentPresent || read.PersistentCell is null) && read.Worldspace is not null)
            throw new InvalidDataException("Saved position lost original ParentCELL/persistent Extra/Worldspace getter order.");
        ValidateParentBorrow(read.Parent, invocation, source, stack);
        ValidateForm(read.PersistentCell); ValidateForm(read.Worldspace);
        if (read.Flags is { } flags && (flags.Source != FalloutReferenceScriptExtraInitializerSource.Reviewed(source).Identity ||
            flags.Stack != stack || flags.Actor != invocation.Actor || flags.Process != invocation.Process ||
            flags.Constructor != read.Constructor || flags.Epoch != invocation.Epoch || flags.Factory == Guid.Empty ||
            flags.FieldsFactory == Guid.Empty || flags.Sequence < 0))
            throw new InvalidDataException("Worldspace borrow changed its independent current reference flag authority.");
    }
}
