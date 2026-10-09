using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeExteriorEnvironment
{
    private FalloutReferenceWorld? _moonWorld;
    private FalloutGameTime? _moonTime;
    internal void BindSourceMoonInputs(FalloutReferenceWorld world, FalloutGameTime time)
    {
        ArgumentNullException.ThrowIfNull(world); ArgumentNullException.ThrowIfNull(time);
        if (IsInsideTree() || _moonWorld is not null || _moonTime is not null)
            throw new InvalidOperationException("Sky source clock/CELL inputs must bind once before actual native construction.");
        _moonWorld = world; _moonTime = time;
    }
    private void BindSourceMoonChildren()
    {
        if (_sky.SourceTransfer is null) return;
        _layers.BindSourceMoons(_sky, _moonWorld ?? throw new NotSupportedException("Source Moon factory has no real Player/CELL owner."),
            _moonTime ?? throw new NotSupportedException("Source Moon factory has no real shared calendar owner."), _units);
    }
}
