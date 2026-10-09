using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private long _observedScriptPackageRevision;
    private long _bindingScriptPackageRevision;
    private long _boundScriptPackageRevision;
    private long ScriptPackageRevision => _aiReferenceState?.ScriptPackage?.Revision ?? 0;

    private bool ScriptPackageAssignmentPending => _aiReferenceState?.ScriptPackage is { Pending: true, Package: { } package } &&
        package == _selectedSourcePackage;

    private bool PackageLocationReached(FalloutFormKey? package) => package is not null &&
        (_nativeMarkerTravel?.Form == package && _nativeMarkerTravelProgress?.Complete == true ||
         _editorTravel?.Form == package && _editorTravelProgress?.Complete == true ||
         _escortPackage?.Form == package && _escortProgress?.Complete == true ||
         _dialoguePackage?.Form == package && _dialogueWaitReached ||
         _aiPackage?.FormKey == package && (_sitting is 1 or 4 || _travelProgress?.ArrivalPending == true) ||
         _aiWorld?.RetainedPackageLocationReached(Appearance.Reference!.Value, package) == true);

    private void RestoreScriptPackageLifecycle()
    {
        _boundScriptPackageRevision = _aiReferenceState?.PackageAssignment?.ScriptPackageRevision ?? 0;
        _observedScriptPackageRevision = ScriptPackageRevision;
    }

    private long BeginScriptPackageEvent(FalloutScriptPackage package, string kind)
    {
        if (_aiWorld is null) return 0;
        if (kind == "POBA")
            _boundScriptPackageRevision = _aiWorld.BeginActorScriptPackage(Appearance.Reference!.Value,
                package.Form, _bindingScriptPackageRevision);
        return _boundScriptPackageRevision;
    }

    private void CompleteScriptPackageEvent(FalloutScriptPackage package, string kind, long startedRevision)
    {
        if ((kind is "POEA" or "POCA") && startedRevision != 0)
            _aiWorld?.RetireActorScriptPackage(Appearance.Reference!.Value, package.Form, startedRevision);
    }
}
