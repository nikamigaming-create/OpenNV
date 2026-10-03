using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class TbcKeyContracts
{
    internal static void Run(Func<FalloutNifScalarKey[], FalloutNifVectorKey[], int, FalloutNifFile> fixture)
    {
        var times = new[] { 0f, .3f, 1.1f, 2f };
        var affine = times.Select(time => new FalloutNifScalarKey(time, 2 + 4 * time, null, null, new(0, 0, 0), 3)).ToArray();
        static FalloutNifVectorKey Vector(FalloutNifScalarKey key) =>
            new(key.Time, new(key.Value, 2 * key.Value + 1, .5f - 3 * key.Value), null, null, key.Tbc, 3);
        var affineSampler = new FalloutNifAnimationSampler(fixture(affine, affine.Select(Vector).ToArray(), 0), 0);
        foreach (var time in new[] { -1f, 0f, .1f, .3f, .7f, 1.1f, 1.8f, 2f, 3f })
            Check(affineSampler.Sample(time), 2 + 4 * Math.Clamp(time, 0, 2), "TBC nonuniform affine reproduction");

        FalloutNifScalarKey[] controlled =
        [
            new(0, 0, null, null, new(0, 0, 0), 3),
            new(1, 2, null, null, new(.2f, .3f, -.4f), 3),
            new(3, 1, null, null, new(-.1f, -.2f, .5f), 3),
            new(4, 5, null, null, new(0, 0, 0), 3)
        ];
        var vectors = controlled.Select(Vector).ToArray();
        var sampler = new FalloutNifAnimationSampler(fixture(controlled, vectors, 0), 0);
        // Independent Hermite basis values: segment [1,3] has outgoing
        // tangent .309333333 and incoming tangent 4.986666667.
        Check(sampler.Sample(1.5f), 1.6535f, "TBC nonzero tension/bias/continuity");
        Check(sampler.Sample(2), .915333333f, "TBC midpoint uses both authored tangent owners");
        Check(sampler.Sample(.5f), 1.142666667f, "TBC first one-sided interval");
        Check(sampler.Sample(3.5f), 2.555f, "TBC last one-sided interval");
        foreach (var key in controlled) Check(sampler.Sample(key.Time), key.Value, "TBC exact authored key");
        Check(sampler.Sample(-10), 0, "TBC initial clamp");
        Check(sampler.Sample(10), 5, "TBC final clamp");
        Near(FalloutNifAnimationSampler.SampleScalar(controlled, 1.5f), 1.6535f, "Shared scalar TBC path");
        Near(FalloutNifAnimationSampler.SampleVector(vectors, 1.5f).Y, 4.307f, "Shared vector TBC path");
        var endpointParameters = controlled.ToArray();
        endpointParameters[0] = endpointParameters[0] with { Tbc = new(.5f, -.5f, .3f) };
        endpointParameters[^1] = endpointParameters[^1] with { Tbc = new(.3f, .4f, -.2f) };
        var endpointSampler = new FalloutNifAnimationSampler(fixture(endpointParameters, endpointParameters.Select(Vector).ToArray(), 0), 0);
        Check(endpointSampler.Sample(.5f), .998916667f, "TBC first endpoint parameters");
        Check(endpointSampler.Sample(3.5f), 2.677f, "TBC last endpoint parameters");
        var single = new FalloutNifAnimationSampler(fixture([controlled[1]], [vectors[1]], 0), 0);
        foreach (var time in new[] { -1f, 1f, 10f }) Check(single.Sample(time), 2, "Single TBC key remains literal");

        Invalid(() => new FalloutNifAnimationSampler(fixture(controlled, vectors, 1), 0));
        var zeroWidth = controlled.ToArray(); zeroWidth[1] = zeroWidth[1] with { Time = 0 };
        Invalid(() => new FalloutNifAnimationSampler(fixture(zeroWidth, [], 0), 0));
        var backwards = vectors.ToArray(); backwards[1] = backwards[1] with { Time = -1 };
        Invalid(() => new FalloutNifAnimationSampler(fixture([], backwards, 0), 0));
        var nonfinite = controlled.ToArray(); nonfinite[0] = nonfinite[0] with { Tbc = new(float.NaN, 0, 0) };
        Invalid(() => new FalloutNifAnimationSampler(fixture(nonfinite, [], 0), 0));
        var absent = controlled.ToArray(); absent[0] = absent[0] with { Tbc = null };
        Invalid(() => FalloutNifAnimationSampler.SampleScalar(absent, .5f));
        var overflow = controlled.ToArray(); overflow[1] = overflow[1] with { Tbc = new(float.MaxValue, float.MaxValue, 0) };
        Invalid(() => FalloutNifAnimationSampler.SampleScalar(overflow, 2));
        for (var warmup = 0; warmup < 100; warmup++) _ = sampler.Sample(2);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        float total = 0;
        for (var frame = 0; frame < 1000; frame++) total += sampler.Sample(frame % 40 / 10f).Scale!.Value;
        Require(GC.GetAllocatedBytesForCurrentThread() == allocated, "TBC per-frame sampling allocated managed objects.");
        GC.KeepAlive(total);
        Console.WriteLine("OPENNV_NIF_TBC_CHANNEL_PASS fullReader=true scalar=true vector=true nonconstant=true nonzeroParameters=true " +
            "nonuniformIntervals=true endpoints=true malformedRefused=true samples=1000 allocatedBytes=0 retailTiming=unverified");
    }

    internal static void Audit(string archivePath, string member)
    {
        using var archive = new FalloutBsaArchive(archivePath);
        var bytes = archive.Read(member); var hash = SHA256.HashData(bytes);
        var source = FalloutNifFile.Read(bytes);
        var sequences = source.Blocks.Where(block => block.TypeName == "NiControllerSequence")
            .Select(block => source.ReadControllerSequence(block.Index)).ToArray();
        int channels = 0, tbcChannels = 0, changing = 0, samples = 0;
        foreach (var sequence in sequences)
            foreach (var link in sequence.ControlledBlocks.Where(link => link.ControllerType == "NiTransformController"))
            {
                var sampler = new FalloutNifAnimationSampler(source, link.Interpolator);
                if (source.ReadObject(link.Interpolator) is FalloutNifTransformInterpolator { Data: >= 0 } interpolator &&
                    source.ReadObject(interpolator.Data) is FalloutNifTransformData data &&
                    (data.Translations.Any(key => key.Interpolation == 3) || data.Scales.Any(key => key.Interpolation == 3) ||
                    data.XyzRotations.Any(axis => axis.Any(key => key.Interpolation == 3)))) tbcChannels++;
                var initial = sampler.Sample(sequence.StartTime); var changed = false;
                for (var tick = 0; tick <= 120; tick++)
                {
                    var sample = sampler.Sample(sequence.StartTime + (sequence.StopTime - sequence.StartTime) * tick / 120f);
                    if (sample.Translation is { } p) Require(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), "Owned translation is nonfinite.");
                    if (sample.Rotation is { } q) Require(float.IsFinite(q.W) && float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z), "Owned rotation is nonfinite.");
                    if (sample.Scale is { } scale) Require(float.IsFinite(scale), "Owned scale is nonfinite.");
                    changed |= sample != initial; samples++;
                }
                channels++; if (changed) changing++;
            }
        Require(sequences.Length != 0 && tbcChannels != 0 && changing != 0, "Owned selection lacks advancing TBC controller channels.");
        Require(SHA256.HashData(archive.Read(member)).SequenceEqual(hash), "Owned animation bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_TBC_ANIMATION_PASS sequences={sequences.Length} transformChannels={channels} " +
            $"tbcChannels={tbcChannels} changingChannels={changing} samples={samples} sourceSha256={Convert.ToHexString(hash)} " +
            "sourceReadOnly=true recording=false nativeCollision=unverified retailTiming=unverified");
    }

    private static void Check(FalloutNifAnimationSample sample, float expected, string description)
    {
        Near(sample.Scale!.Value, expected, description + " scalar");
        Near(sample.Translation!.Value.X, expected, description + " x");
        Near(sample.Translation.Value.Y, 2 * expected + 1, description + " y");
        Near(sample.Translation.Value.Z, .5f - 3 * expected, description + " z");
    }
    private static void Near(float actual, float expected, string description) =>
        Require(MathF.Abs(actual - expected) < .00002f, $"{description}: {actual:R} != {expected:R}");
    private static void Invalid(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Malformed TBC declaration was accepted.");
    }
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
