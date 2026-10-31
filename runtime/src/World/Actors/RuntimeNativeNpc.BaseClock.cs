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
        FalloutActorAnimationSnapshot? continuation = null, bool publishPose = true)
    {
        _baseResource = resource; _baseHash = source.Sha256; _baseAmbient = ambient;
        if (_bindingInitialBase)
        {
            // A newly selected physical procedure owns its actual base source.
            // Retained walking Idle0 cannot override it after initial binding.
            if (_furnitureClip is not null && !_furnitureApproaching && _aiReferenceState?.FurnitureContinuation is null)
            {
                if (!_baseClock.Resource.Equals(resource, StringComparison.OrdinalIgnoreCase) ||
                    !_baseClock.Sha256.Equals(_baseHash, StringComparison.OrdinalIgnoreCase))
                    _baseClock.Change(resource, _baseHash);
                if (_aiReferenceState?.PackageMotion is { } prior)
                    _aiReferenceState.PackageMotion = prior with { AnimationLayers = null };
            }
            return;
        }
        _baseClock.Change(resource, _baseHash);
        if (continuation is not null) _baseClock.Restore(continuation);
        ResumeBaseClock(retainedTravel: continuation is not null, publishPose: publishPose);
    }

    private void ResumeBaseClock(bool retainedTravel = false, bool publishPose = true)
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
        if (publishPose) _baseAnimation.ApplySourceTime(_baseAnimationSeconds);
    }
}
