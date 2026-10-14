using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutProcessPackageObservation? ObserveCurrentSourcePlayerPackage()
    {
        if (!HasStandalonePlayerScene || _sourcePackageRegistry is null) return null;
        RequireStandaloneControlPublication();
        var actor = ActorProcesses.Read(_enginePlayer);
        return ProcessCommon.ObserveSourcePackageFields(_enginePlayer, actor.Epoch);
    }

    internal bool TryReadCurrentSourcePlayerPackage(out FalloutFormKey? package)
    {
        package = null;
        if (!HasStandalonePlayerScene || _sourcePackageRegistry is null) return false;
        RequireStandaloneControlPublication();
        var actor = ActorProcesses.Read(_enginePlayer);
        if (actor.Retired) throw new InvalidOperationException("Current Player package belongs to a retired Actor.");
        var primary = ProcessCommon.ReadSourcePackageSlot(_enginePlayer, actor.Epoch, FalloutProcessPackageSlot.Primary);
        package = primary.Fields.Fields.Package ??
            ProcessCommon.ReadSourcePackageSlot(_enginePlayer, actor.Epoch, FalloutProcessPackageSlot.Current).Fields.Fields.Package;
        return true;
    }

    internal FalloutPackageCommandAnimationRead BorrowReturnedPackageAnimation(FalloutSourcePackageAssignmentCall command)
    {
        RequireStandaloneControlPublication();
        if (_enteredPackageCommand is not null || command.Step != FalloutSourcePackageAssignmentStep.Returned ||
            command.Result != true || command.Invocation.Actor.Reference != _enginePlayer ||
            !_sourcePackageFields.TryGetValue(_enginePlayer, out var fields))
            throw new InvalidDataException("Package presentation omitted its actual returned gameplay command.");
        var actor = ActorProcesses.Read(_enginePlayer);
        var current = ProcessCommon.ReadSourcePackageSlot(_enginePlayer, actor.Epoch, FalloutProcessPackageSlot.Current);
        if (actor.Retired || command.Invocation.Actor != actor.Source || command.Invocation.Epoch != actor.Epoch ||
            current.Fields.Fields.Package != command.Package ||
            !_sourcePackageCommandIndex.TryGetValue(command.Invocation.SourceCommand, out var retained) || !ReferenceEquals(retained, command))
            throw new InvalidDataException("Package presentation substituted another current package or Actor lifetime.");
        return fields.BorrowReturnedAnimation(command.Invocation);
    }
}
