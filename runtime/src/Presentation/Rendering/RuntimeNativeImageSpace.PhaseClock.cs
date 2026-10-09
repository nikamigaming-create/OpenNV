using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeImageSpace
{
    private Guid _phaseProcess;
    internal void BindCurrentPhaseClock(FalloutGameTime time, Guid process)
    {
        if (!ReferenceEquals(time, _gameTime) || process == Guid.Empty || _phaseProcess != Guid.Empty && _phaseProcess != process)
            throw new InvalidDataException("Native image phase writer changed its actual shared calendar/process.");
        var phase = _effect.DoubleVisionPhase ?? throw new NotSupportedException(_effect.DoubleVisionPhaseError ?? "Image phase declaration is absent.");
        _state.BindDoubleVisionClock(phase, time, process); _phaseProcess = process;
    }
}
