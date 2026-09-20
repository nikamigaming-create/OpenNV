using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed class NativeActorCombatAnimation
{
    internal string Path { get; }
    internal string Hash { get; }
    internal RuntimeNativeNifAnimation Animation { get; }
    internal FalloutNifTextKeyTimeline Events { get; }
    private readonly FalloutNifAnimationSampler? _root;
    private readonly bool _loop;
    internal double Duration { get; }

    internal NativeActorCombatAnimation(string path, RuntimeLiveContentSource content, RuntimeNativeNifSkeleton skeleton,
        NativeActorWeaponAttachment? weapon, bool loop)
    {
        Path = path; _loop = loop;
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Actor combat animation is absent.", path);
        Hash = Convert.ToHexString(SHA256.HashData(bytes));
        var source = FalloutNifFile.Read(bytes);
        var sequences = source.Roots.Select(source.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var matching = sequences.Where(sequence => name.EndsWith(sequence.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = sequences.Length == 1 ? sequences[0] : matching.Length == 1 ? matching[0] :
            throw new NotSupportedException("Actor combat animation has no unique requested sequence.");
        var root = selected.ControlledBlocks.SingleOrDefault(link => link.NodeName == selected.TargetName && link.ControllerType == "NiTransformController");
        _root = root is null ? null : new(source, root.Interpolator);
        Animation = new(source, selected, skeleton, link => weapon?.Bind(source, link), accumulationRoot: _ => { },
            externalObjectTargets: weapon?.Targets);
        if (Animation.UnboundChannels.Count != 0) throw new NotSupportedException("Actor combat channels are unbound: " +
            string.Join("; ", Animation.UnboundChannels.Select(channel => channel.Source.NodeName + "/" + channel.Reason)));
        Events = new(Animation.TextKeys, selected.StartTime, selected.StopTime, loop ? 0u : 2u, selected.Frequency);
        Duration = (selected.StopTime - selected.StartTime) / selected.Frequency;
    }

    internal float Time(double elapsed)
    {
        var sequence = Animation.Sequence;
        return sequence.StartTime + (float)((_loop ? elapsed % Duration : Math.Min(elapsed, Duration)) * sequence.Frequency);
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
