namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    // Production current XYZ/mode/ParentCELL/RZ are borrowed from separate
    // real owners. This checks only fields this owner actually publishes.
    // The World/save aggregate checks those independent owners before use.
    internal string? OwnedFieldsSaveBlocker => _busy ? "source-package-command-field-consumer-in-flight" : _retired ?
        "source-package-command-field-owner-retired" : _failure ?? _scriptInitializer?.SaveBlocker ??
        (_savedScriptInitialization is not null && _scriptInitializer is null ? "source-cold-Script-initializer-current-binding-unbound" : null) ??
        _availability.Values.FirstOrDefault(value => !value.Available && value.Lane is
            (FalloutPackageCommandFieldLane.ScriptExtra or FalloutPackageCommandFieldLane.SavedPosition or
                FalloutPackageCommandFieldLane.Animation))?.Boundary;
}
