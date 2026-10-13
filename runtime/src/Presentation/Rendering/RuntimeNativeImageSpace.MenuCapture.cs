using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeImageSpace
{
    private IDisposable BeginSourceMenuBackground(FalloutPluginStack records, FalloutMenuBackgroundKind kind)
    {
        RequireCurrentFactory();
        if (_menuBackground is not null)
            throw new InvalidOperationException("Another static menu background already owns the world capture.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Menu capture has no owned content source.");
        var selector = FalloutExecutableStringTable.ReadMenuBackgroundDeclarations(content.FalloutExecutablePath);
        var modifier = FalloutImageSpaceModifierReader.Read(records.GetEffective(records.RuntimeFormKey(selector.Form(kind))));
        var auxiliary = records.RuntimeFormKey(_state.SourceImageMenuAuxiliaryForm);
        var serial = checked(++_menuSerial);
        FalloutImageSpaceFrame? captured = null;
        _state.CaptureSourceImageMenu(modifier, auxiliary, _sourceMenuClock ??
            throw new NotSupportedException("Source menu capture has no actual shared process clock."), output =>
        {
            captured = _state.ReadSourceMainImageFrame(output);
            var fields = SourceFields ?? throw new InvalidOperationException("Menu capture lost its actual receiver field publication.");
            // Staging immutable fields is distinct from a completed GPU world
            // capture. Headless has no such consumer and submits no draw.
            if (_headlessSourceOutput is null) _effect.SetSourceMenuBackground(serial, captured, fields);
        });
        _menuBackground = captured ?? throw new InvalidOperationException("Menu capture returned without its field publication.");
        Frame = _menuBackground;
        SetMeta("opennv_menu_background_source", modifier.Form.ToString());
        SetMeta("opennv_menu_background_source_sha256", modifier.SourceSha256);
        SetMeta("opennv_menu_background_selector_sha256", selector.SourceSha256);
        SetMeta("opennv_menu_background_active", true);
        SetMeta("opennv_menu_auxiliary_interface_gate", "UNOWNED");
        SetMeta("opennv_menu_background_capture", _headlessSourceOutput is null ? "STAGED_UNVERIFIED" : "UNEXECUTED_HEADLESS");
        PublishObservation();
        GD.Print($"OPENNV_SOURCE_MENU_BACKGROUND source={modifier.Form} owner=shared-manager-capture-remove-restore " +
            $"auxiliaryGate=UNOWNED capture={(_headlessSourceOutput is null ? "STAGED_UNVERIFIED" : "UNEXECUTED_HEADLESS")} parity=unmeasured");
        return new BackgroundLease(this, serial);
    }
}
