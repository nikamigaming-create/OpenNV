using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private ulong _packageBaseAdoptFrame = ulong.MaxValue;
    internal FalloutActorAnimationSnapshot PackageBaseClock => _baseClock.Capture() ??
        throw new InvalidOperationException("NPC package has no bound base animation clock.");
    internal void AdoptPackageBaseClock(FalloutActorAnimationSnapshot clock, RuntimeNativeNifAnimation animation)
    {
        if (!animation.Source.Sha256.Equals(clock.Sha256, StringComparison.OrdinalIgnoreCase) || animation.Sequence.CycleType != 0)
            throw new InvalidDataException("NPC package base rebind differs from its owned looping source.");
        if (!clock.Resource.Equals(_baseResource, StringComparison.OrdinalIgnoreCase))
        {
            _baseAnimation = animation; _baseResource = clock.Resource; _baseHash = clock.Sha256;
            _baseAmbient = false; _baseLocomotionMoving = false;
            SetMeta("opennv_base_animation_source", clock.Resource);
        }
        if (!clock.Resource.Equals(_baseClock.Resource, StringComparison.OrdinalIgnoreCase))
            _baseClock.Change(clock.Resource, clock.Sha256);
        _baseClock.Restore(clock); _packageBaseAdoptFrame = Engine.GetProcessFrames();
        // Adoption resumes an established clock. The ordinary base-binding
        // path may seed a new ambient loop and must not reinitialize it here.
        var sequence = _baseAnimation?.Sequence ??
            throw new InvalidOperationException("NPC package base clock has no source animation.");
        _baseElapsedSeconds = clock.ElapsedSeconds;
        var elapsed = _baseElapsedSeconds * sequence.Frequency;
        var duration = sequence.StopTime - sequence.StartTime;
        _baseAnimationSeconds = sequence.CycleType switch
        {
            0 when duration > 0 => sequence.StartTime + (float)(elapsed % duration),
            2 => sequence.StartTime + (float)Math.Min(elapsed, duration),
            _ => throw new NotSupportedException("NPC package base clock has an unsupported source cycle.")
        };
    }

    private void RestorePackageBaseClockBinding()
    {
        var clock = _baseClock.Capture();
        if (clock is null) return;
        if (FurnitureProcedureActive && !_furnitureApproaching)
        {
            if (!_baseResource.Equals(clock.Resource, StringComparison.OrdinalIgnoreCase) ||
                !_baseHash.Equals(clock.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Furniture base binding differs from its retained physical clock.");
            return;
        }
        var layers = _aiReferenceState?.PackageMotion?.AnimationLayers;
        if (layers?.OwnsIdleClock == true &&
            (!clock.Resource.Equals(layers.Idle.Resource, StringComparison.OrdinalIgnoreCase) ||
             !clock.Sha256.Equals(layers.Idle.Sha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Retained package Idle0 differs from its family clock source.");
        if (_baseResource.Equals(clock.Resource, StringComparison.OrdinalIgnoreCase)) return;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned package base source is absent.");
        // Furniture and other non-Idle family bindings retain their existing
        // restoration owner. An Idle0 clock survives walking-layer retirement.
        var (_, sequence) = NativeActorCombatAnimation.ReadSource(clock.Resource, content);
        var group = sequence.Name.Split('_')[0];
        if (!group.Equals("Idle", StringComparison.OrdinalIgnoreCase) && !group.Equals("DynamicIdle", StringComparison.OrdinalIgnoreCase)) return;
        var clip = new NativeActorCombatAnimation(clock.Resource, content, Skeleton, null, true);
        if (!clip.Hash.Equals(clock.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Retained package base differs from its winning animation.");
        AdoptPackageBaseClock(clock, clip.Animation);
    }
}
