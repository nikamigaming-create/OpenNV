namespace OpenNV.Runtime.Formats.Gamebryo;

// The declaration supplies the generation limit, probability and inclusive
// copy-count range. Variation equations require a separate engine contract.
internal static class FalloutNifParticleSpawnRules
{
    internal static void Validate(FalloutNifParticleSpawn source)
    {
        if (!float.IsFinite(source.Probability) || source.Probability is < 0 or > 1 || source.Minimum > source.Maximum ||
            !float.IsFinite(source.SpeedVariation) || source.SpeedVariation < 0 ||
            !float.IsFinite(source.DirectionVariation) || source.DirectionVariation < 0 ||
            !float.IsFinite(source.Life) || source.Life < 0 || !float.IsFinite(source.LifeVariation) || source.LifeVariation < 0)
            throw new InvalidDataException($"Particle spawn modifier {source.Block.Index} has invalid source ranges.");
    }

    internal static void RequireExecutionOwner(FalloutNifParticleSpawn source)
    {
        Validate(source);
        if (source.Generations == 0 || source.Probability == 0 || source.Maximum == 0) return;
        if (source.SpeedVariation != 0 || source.DirectionVariation != 0 || source.LifeVariation != 0)
            throw new NotSupportedException($"Particle spawn modifier {source.Block.Index} requires its source variation equations.");
        if (source.Life == 0)
            throw new NotSupportedException($"Particle spawn modifier {source.Block.Index} requires instantaneous birth/death ordering.");
    }

    internal static int Copies(FalloutNifParticleSpawn source, ushort generation, double probabilitySample, double countSample)
    {
        RequireExecutionOwner(source);
        if (!double.IsFinite(probabilitySample) || probabilitySample is < 0 or >= 1 ||
            !double.IsFinite(countSample) || countSample is < 0 or >= 1)
            throw new InvalidDataException("Particle spawn random sample is outside its unit interval.");
        if (generation >= source.Generations || probabilitySample >= source.Probability || source.Maximum == 0) return 0;
        return source.Minimum + (int)(countSample * (source.Maximum - source.Minimum + 1));
    }
}
