using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class RotAccumContracts
{
    internal static void Audit(string archivePath, string member)
    {
        using var archive = new FalloutBsaArchive(archivePath);
        var bytes = archive.Read(member); var hash = SHA256.HashData(bytes);
        var source = FalloutNifFile.Read(bytes);
        var sequences = source.Blocks.Where(block => block.TypeName == "NiControllerSequence")
            .Select(block => source.ReadControllerSequence(block.Index)).ToArray();
        var accumulated = 0; var changing = 0; var samples = 0;
        foreach (var sequence in sequences)
            foreach (var link in sequence.ControlledBlocks.Where(link => link.ControllerType == "NiTransformController"))
            {
                if (source.Blocks[link.Interpolator].TypeName != "BSRotAccumTransfInterpolator") continue;
                var sampler = new FalloutNifAnimationSampler(source, link.Interpolator);
                var initial = sampler.Sample(sequence.StartTime); var changed = false;
                for (var tick = 0; tick <= 120; tick++)
                {
                    var sample = sampler.Sample(sequence.StartTime + (sequence.StopTime - sequence.StartTime) * tick / 120f);
                    if (sample.Rotation is { } rotation && (!float.IsFinite(rotation.W) || !float.IsFinite(rotation.X) ||
                        !float.IsFinite(rotation.Y) || !float.IsFinite(rotation.Z)))
                        throw new InvalidDataException("Owned accumulated rotation is nonfinite.");
                    changed |= sample.Rotation != initial.Rotation; samples++;
                }
                accumulated++; if (changed) changing++;
            }
        if (accumulated == 0 || changing == 0 || !SHA256.HashData(archive.Read(member)).SequenceEqual(hash))
            throw new InvalidDataException("Owned selection lost its changing accumulated rotation or source bytes.");
        Console.WriteLine($"OPENNV_OWNED_ROT_ACCUM_PASS channels={accumulated} changing={changing} samples={samples} " +
            $"sourceSha256={Convert.ToHexString(hash)} sourceReadOnly=true retailAccumulation=unverified");
    }
}
