using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private IEnumerable<uint>? NativeActiveMenus()
    {
        var codes = _nativeOpeningStageDriver?.ActiveMenus().ToHashSet() ?? [];
        if (_nativeLoadingLayer is not null || _loadingPhase is not null) codes.Add(1007);
        if (_nativeSessionMenu is not null) codes.Add(1013);
        if (_nativeTerminalLayer is not null) codes.Add(1057);
        if (_nativeContainerLayer is { } container)
        {
            codes.Add(1008);
            if (container.FindChildren("*", "", true, false).OfType<NativeOwnedQuantityMenu>().Any()) codes.Add(1016);
        }
        if (_nativePipBoy is not null && _nativeOpeningStageDriver?.PipBoy is { Open: true } pipBoy)
            codes.Add(pipBoy.Page switch { FalloutPipBoyPage.Items => 1002u, FalloutPipBoyPage.Stats => 1003u, _ => 1023u });
        // These adapters still need source-specific menu declarations. Do not
        // report a known empty set while an unbound native modal owns input.
        if (codes.Count == 0 && (_nativeLockpickLayer is not null || _nativeHackingLayer is not null ||
            _nativeCombatWheel is not null)) return null;
        foreach (var code in codes.ToArray()) codes.Add(FalloutScriptMenus.Category(code));
        return codes;
    }
}
