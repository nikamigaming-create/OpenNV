using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void ConfigureCurrentImagePhase(FalloutImageSpacePhaseClockSnapshot? cold)
    {
        var time = _nativeGameTime ?? throw new NotSupportedException("Source image phase has no actual current calendar.");
        var source = _nativePluginStack?.OwnedSource ?? throw new InvalidOperationException("Source image phase has no exact campaign selection.");
        var process = _nativeReferences?.ActualSourceProcessIdentity ?? throw new InvalidOperationException("Source image phase has no current process factory.");
        var phase = FalloutExecutableStringTable.ReadDoubleVisionPhase(source.FalloutExecutablePath);
        _nativeImageSpaceState.RetireDoubleVisionClock();
        if (cold is null) _nativeImageSpaceState.ConstructDoubleVisionClock(phase, time, process);
        else _nativeImageSpaceState.RestoreDoubleVisionClock(cold, phase, time, process);
    }
    private void RetireCurrentImagePhase()
    {
        // No presentation producer may sample a replacement campaign cache
        // through a previous process' still-living node.
        if (GodotObject.IsInstanceValid(_nativeImageSpaceClock)) _nativeImageSpaceClock!.ProcessMode = ProcessModeEnum.Disabled;
        if (GodotObject.IsInstanceValid(_nativeCurrentCellRoot))
            foreach (var presenter in _nativeCurrentCellRoot!.GetChildren().OfType<RuntimeNativeImageSpace>())
                presenter.ProcessMode = ProcessModeEnum.Disabled;
        _nativeImageSpaceState.RetireDoubleVisionClock();
    }
}
