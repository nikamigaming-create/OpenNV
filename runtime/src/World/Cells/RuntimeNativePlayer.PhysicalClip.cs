using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private sealed record PlayerPhysicalClip(FalloutFurnitureClipSnapshot Identity,
        RuntimeNativeNifAnimation Animation, FalloutNifTextKeyTimeline Events,
        double Duration, bool Loop, Vector3 Start, Vector3 End, FalloutNifAnimationSampler? Root)
    {
        internal float SourceTime(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) throw new InvalidDataException("Player physical source clock is invalid.");
            var sequence = Animation.Sequence;
            return sequence.StartTime + (float)((Loop ? seconds % Duration : Math.Min(seconds, Duration)) * sequence.Frequency);
        }
        internal Vector3 RootDisplacement(double from, double to, Func<FalloutNifAnimationSample, Vector3> convert) =>
            Root is null ? Vector3.Zero : convert(Root.Sample(SourceTime(to))) - convert(Root.Sample(SourceTime(from)));
    }
    private PlayerPhysicalClip ReadPlayerPhysicalClip(FalloutPluginRecord idle, bool loop,
        RuntimeNativeNifSkeleton skeleton, Action<FalloutNifAnimationSample>? root)
    {
        var records = _physicalRecords!; var content = RuntimeLiveContentSource.Current!;
        var source = FalloutActorIdleSource.Resolve(records, idle);
        if (source.Objects.Count != 0) throw new NotSupportedException("Player physical IDLE has an unowned ANIO object.");
        var timing = FalloutIdleAnimationData.Read(idle);
        if (timing.ReplayDelaySeconds != 0 || !timing.AdmitsAdditionalLoops(0))
            throw new NotSupportedException("Player physical IDLE needs its additional-loop/replay-delay owner.");
        if (!content.TryRead(source.AnimationPath, null, out var bytes, out _))
            throw new FileNotFoundException("Player physical source KF is absent.", source.AnimationPath);
        var nif = FalloutNifFile.Read(bytes);
        var sequence = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().Single();
        if (!float.IsFinite(sequence.Frequency) || sequence.Frequency <= 0 || sequence.StopTime <= sequence.StartTime ||
            sequence.CycleType != (loop ? 0u : 2u)) throw new NotSupportedException("Player physical KF clock requires a different source owner.");
        if (sequence.ControlledBlocks.Any(link => link.ControllerType != "NiTransformController"))
            throw new NotSupportedException("Player physical KF visual/material channels need their separate cold continuation owner.");
        var rootLink = sequence.ControlledBlocks.SingleOrDefault(link => link.NodeName == sequence.TargetName && link.ControllerType == "NiTransformController");
        if (loop && rootLink is not null) throw new NotSupportedException("Player physical loop accumulation has no collision-motion consumer.");
        Vector3 start = default, end = default; FalloutNifAnimationSampler? sampler = null;
        if (!loop && rootLink is not null)
        {
            sampler = new FalloutNifAnimationSampler(nif, rootLink.Interpolator);
            start = Translation(sampler.Sample(sequence.StartTime)); end = Translation(sampler.Sample(sequence.StopTime));
        }
        var animation = new RuntimeNativeNifAnimation(nif, sequence, skeleton, accumulationRoot: root);
        if (animation.UnboundChannels.Count != 0) throw new NotSupportedException("Player physical KF has unowned controller channels.");
        foreach (var key in animation.TextKeys)
            if (FalloutNifTextKeyDeclarations.Read(key.Value).Any(declaration => declaration.Kind == FalloutNifTextKeyDeclarationKind.Unbound))
                throw new NotSupportedException("Player physical KF has an unowned original event; it cannot be skipped.");
        var identity = new FalloutFurnitureClipSnapshot(idle.FormKey, Convert.ToHexString(SHA256.HashData(idle.ReadData())),
            source.AnimationPath, nif.Sha256);
        return new(identity, animation, new(animation.TextKeys, sequence.StartTime, sequence.StopTime, sequence.CycleType,
            sequence.Frequency), ((double)sequence.StopTime - sequence.StartTime) / sequence.Frequency, loop, start, end, sampler);
    }
}
