using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutExplosionExposure(FalloutFormKey Explosion, string SourceSha256, FalloutFormKey Cell,
    float[] Position, double ElapsedSeconds);
