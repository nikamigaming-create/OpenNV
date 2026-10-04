using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutActorAnimationState _baseClock = new();
    private bool _bindingInitialBase;
    private string _baseResource = "", _baseHash = "";
    private bool _baseAmbient;

    private void BindBaseClock(FalloutNifFile source, string resource, bool ambient,
        FalloutActorAnimationSnapshot? continuation = null)
    {
        _baseResource = resource; _baseHash = source.Sha256; _baseAmbient = ambient;
        if (_bindingInitialBase) return;
        _baseClock.Change(resource, _baseHash);
        if (continuation is not null) _baseClock.Restore(continuation);
        ResumeBaseClock(retainedTravel: continuation is not null);
    }

    private void ResumeBaseClock(bool retainedTravel = false)
    {
        if (_travelActive && _nativeMarkerTravel is null && !_baseAmbient && !_baseClock.StartPending && !retainedTravel)
            throw new NotSupportedException("Saved actor travel requires its controller pose, route cursor and consumed root-motion continuation owner.");
        var sequence = _baseAnimation!.Sequence;
        if (_baseAmbient && Appearance.Reference is { } reference)
            _baseClock.StartAmbientLoop(reference, (sequence.StopTime - sequence.StartTime) / sequence.Frequency);
        _baseElapsedSeconds = _baseClock.ElapsedSeconds;
        var elapsed = _baseElapsedSeconds * sequence.Frequency;
        _baseAnimationSeconds = sequence.StartTime + (float)(sequence.CycleType == 0 ?
            elapsed % (sequence.StopTime - sequence.StartTime) : Math.Min(elapsed, sequence.StopTime - sequence.StartTime));
        _baseAnimation.ApplySourceTime(_baseAnimationSeconds);
    }
}
