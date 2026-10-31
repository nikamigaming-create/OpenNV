using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private ulong _packageBaseAdoptFrame = ulong.MaxValue;
    internal string? PackageAnimationError => _aiError ?? Error;
    internal FalloutActorAnimationSnapshot PackageBaseClock => _clock.Capture() ??
        throw new InvalidOperationException("Creature package has no bound base animation clock.");
    internal void AdoptPackageBaseClock(FalloutActorAnimationSnapshot clock, RuntimeNativeNifAnimation animation)
    {
        if (!animation.Source.Sha256.Equals(clock.Sha256, StringComparison.OrdinalIgnoreCase) ||
            animation.Sequence.CycleType != 0)
            throw new InvalidDataException("Creature package base rebind differs from its owned looping source.");
        if (!clock.Resource.Equals(_clock.Resource, StringComparison.OrdinalIgnoreCase))
        {
            _animation = animation;
            var sequence = animation.Sequence;
            _textKeys = new(animation.TextKeys, sequence.StartTime, sequence.StopTime, sequence.CycleType, sequence.Frequency);
            _clock.Change(clock.Resource, clock.Sha256);
            SetMeta("opennv_selected_animation", clock.Resource);
        }
        _clock.Restore(clock); _packageBaseAdoptFrame = Engine.GetProcessFrames();
    }
}
