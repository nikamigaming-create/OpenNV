using System.Numerics;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutMoonTexture(string Path, string ResourceSource, string Sha256);
internal sealed record FalloutMoonMeshInputs(Vector3[] Vertices, Vector2[] Uv, Vector4[] Colors, int[] Triangles);
internal sealed record FalloutMoonNativeFields(bool ParentPublished, bool PrimaryChildPublished, bool ShadowChildPublished,
    bool PrimaryGeometryPublished, bool ShadowGeometryPublished, bool PrimaryHasTexture, bool ShadowHasTexture,
    FalloutMoonTexture? PrimaryTexture, FalloutMoonTexture? ShadowTexture, bool ParentHidden, bool PrimaryHidden, bool ShadowHidden,
    uint PrimaryAlphaBits, uint ShadowAlphaBits);
internal sealed record FalloutMoonSnapshot(FalloutMoonRole Role, FalloutMoonSettings Settings, Guid CapturedIdentity,
    long Changed, uint AngleBits, uint LastHourBits, int Pending, int? LastPhaseAttempt, FalloutMoonNativeFields? Native, string? Failure);
internal sealed record FalloutSkyMoonSnapshot(string Schema, FalloutMoonSource Source, string Stack, Guid CapturedSky,
    Guid CapturedProcess, long Changed, int Phase, uint StoredDays, FalloutMoonClimate? Climate,
    IReadOnlyList<FalloutMoonSnapshot> Moons, bool FactoryEntered, string? Failure);
internal sealed record FalloutMoonFrame(float Hour, uint DaysPassed, bool ForcePhase, float NightAlpha,
    Vector3 NightColor, int SkyMode);
internal sealed record FalloutMoonNativeTextureReturn(Guid Moon, FalloutMoonTexture? Loaded, bool HasTexture);
internal interface IFalloutNativeMoon
{
    Guid Identity { get; }
    FalloutMoonRole Role { get; }
    FalloutMoonNativeFields Capture();
    FalloutMoonNativeTextureReturn LoadPhase(string? path);
    void Apply(FalloutMoonFrame frame, float angle, float primaryAlpha, float shadowAlpha);
    void Retire();
}
