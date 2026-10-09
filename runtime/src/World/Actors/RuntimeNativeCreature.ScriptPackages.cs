using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private long _observedScriptPackageRevision;
    private long _bindingScriptPackageRevision;
    private long _boundScriptPackageRevision;
    private long ScriptPackageRevision => _aiState?.ScriptPackage?.Revision ?? 0;

    private bool PackageLocationReached(FalloutFormKey? package) => package is not null &&
        (_travelPackage?.Form == package && _travelProgress?.Complete == true ||
         _guardPackage?.Form == package && _guardProgress?.Complete == true ||
         _dialoguePackage?.Form == package && _dialogueRequested ||
         _aiWorld?.RetainedPackageLocationReached(Appearance.Reference!.Value, package) == true);

    private FalloutPluginRecord? SelectSourcePackage(bool reevaluateScript)
        => _aiWorld!.SelectActorPackage(Appearance.Reference!.Value, PackageCondition,
            _aiState!.Templates, _aiClock, _packageEvents?.Active?.Form, _packageEvents?.Done == true,
            reevaluateScript, PackageLocationReached(_packageEvents?.Active?.Form));

    private void RestoreScriptPackageLifecycle()
    {
        _boundScriptPackageRevision = _aiState?.PackageAssignment?.ScriptPackageRevision ?? 0;
        _observedScriptPackageRevision = ScriptPackageRevision;
    }

    private long BeginScriptPackageEvent(FalloutScriptPackage package, string kind)
    {
        if (kind == "POBA")
            _boundScriptPackageRevision = _aiWorld!.BeginActorScriptPackage(Appearance.Reference!.Value,
                package.Form, _bindingScriptPackageRevision);
        return _boundScriptPackageRevision;
    }

    private void CompleteScriptPackageEvent(FalloutScriptPackage package, string kind, long startedRevision)
    {
        if ((kind is "POEA" or "POCA") && startedRevision != 0)
            _aiWorld!.RetireActorScriptPackage(Appearance.Reference!.Value, package.Form, startedRevision);
    }
}
