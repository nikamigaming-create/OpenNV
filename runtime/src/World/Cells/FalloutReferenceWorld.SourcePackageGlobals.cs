using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutGlobalState? _sourcePackageGlobals;
    private FalloutFormKey? _sourcePackageGameHour;
    private Action? _requireSourcePackageGlobals;

    internal void BindStandalonePackageGlobals(FalloutGlobalState globals, FalloutGameTimeBindings forms,
        Action requireActualLifetime)
    {
        RequireStandaloneControlPublication(); ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(forms); ArgumentNullException.ThrowIfNull(requireActualLifetime);
        if (_sourcePackageGlobals is not null || _sourcePackageGameHour is not null)
            throw new InvalidOperationException("Package global binding replaced its actual shared state.");
        requireActualLifetime();
        var record = records.GetEffective(forms.Hour);
        if (record.Signature != "GLOB" || record.IsDeleted ||
            !globals.Sources.Any(value => value.Form == forms.Hour))
            throw new InvalidDataException("Package GameHour has no actual shared winning global.");
        _sourcePackageGlobals = globals; _sourcePackageGameHour = forms.Hour;
        _requireSourcePackageGlobals = requireActualLifetime;
    }

    private FalloutPackageGameHourRead ReadStandalonePackageGameHour(FalloutPackageCommandFieldInvocation invocation)
    {
        RequireActualSourcePackageCommand(invocation);
        var require = _requireSourcePackageGlobals ?? throw new NotSupportedException("source-package-current-GameHour-global-owner-unbound");
        require();
        var form = _sourcePackageGameHour ?? throw new NotSupportedException("source-package-current-GameHour-slot-binding-unbound");
        var globals = _sourcePackageGlobals ?? throw new NotSupportedException("source-package-current-GameHour-global-owner-unbound");
        var value = globals.Get(form);
        require(); RequireActualSourcePackageCommand(invocation);
        return new(form, BitConverter.SingleToUInt32Bits(value), "actual-shared-source-GameHour-global");
    }
}
