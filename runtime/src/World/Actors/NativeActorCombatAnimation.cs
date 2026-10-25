using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed class NativeActorCombatAnimation
{
    internal string Path { get; }
    internal string Hash { get; }
    internal RuntimeNativeNifAnimation Animation { get; }
    internal FalloutWeaponAnimationTimeline Events { get; }
    private readonly FalloutNifAnimationSampler? _root;
    private readonly bool _loop;
    internal double Duration { get; }

    internal NativeActorCombatAnimation(string path, RuntimeLiveContentSource content, RuntimeNativeNifSkeleton skeleton,
        NativeActorWeaponAttachment? weapon, bool loop, bool weaponCadence = false, NativeActorIdleObjects? objects = null)
    {
        Path = path; _loop = loop;
        var source = NativeAnimationResources.For(content).Read(path);
        Hash = source.Sha256.ToUpperInvariant();
        var sequences = source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var declared = content.ActorAnimations.SelectedGroup(path);
        var matching = sequences.Where(sequence => declared is not null
            ? sequence.Name.Equals(declared, StringComparison.OrdinalIgnoreCase)
            : name.EndsWith(sequence.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = declared is null && sequences.Length == 1 ? sequences[0] : matching.Length == 1 ? matching[0] :
            throw new NotSupportedException("Actor combat animation has no unique requested sequence.");
        var root = selected.ControlledBlocks.SingleOrDefault(link => link.NodeName == selected.TargetName && link.ControllerType == "NiTransformController");
        _root = root is null ? null : new(source, root.Interpolator);
        var external = new HashSet<string>(weapon is null ? Array.Empty<string>() : weapon.Targets, StringComparer.Ordinal);
        if (objects is not null) external.UnionWith(objects.Targets);
        Animation = new(source, selected, skeleton, link => objects?.Bind(link) ?? weapon?.Bind(source, link), accumulationRoot: _ => { },
            externalObjectTargets: external);
        if (Animation.UnboundChannels.Count != 0) throw new NotSupportedException("Actor combat channels are unbound: " +
            string.Join("; ", Animation.UnboundChannels.Select(channel => channel.Source.NodeName + "/" + channel.Reason)));
        Events = new(Animation.TextKeys, selected.StartTime, selected.StopTime, selected.Frequency, loop,
            weaponCadence && selected.CycleType == 0);
        Duration = (selected.StopTime - selected.StartTime) / selected.Frequency;
    }

    internal float Time(double elapsed)
    {
        var sequence = Animation.Sequence;
        return sequence.StartTime + (float)(Events.SampleSeconds(elapsed, _loop) * sequence.Frequency);
    }

    internal Vector3 RootDisplacement(double from, double to)
    {
        if (_root is null) return Vector3.Zero;
        var sequence = Animation.Sequence;
        Vector3 Sample(float seconds)
        {
            var sample = _root.Sample(seconds);
            if (sample.Scale is { } scale && scale != 1 || sample.Rotation is { } rotation &&
                (MathF.Abs(rotation.W) < .99999f || MathF.Abs(rotation.X) + MathF.Abs(rotation.Y) + MathF.Abs(rotation.Z) > .0001f))
                throw new NotSupportedException("Combat accumulation rotation/scale requires its motion extraction owner.");
            return sample.Translation is { } p ? GamebryoCoordinate.ConvertVector(new(p.X, p.Y, p.Z)) : Vector3.Zero;
        }
        var delta = Sample(Time(to)) - Sample(Time(from));
        if (_loop) delta += (Sample(sequence.StopTime) - Sample(sequence.StartTime)) *
            (float)(Math.Floor(to / Duration) - Math.Floor(from / Duration));
        return delta;
    }
}
