using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutSkyResetStep
{
    DirtyStore, OverrideNull, PreviousNull, CurrentNull, TransitionFlagClear, TransitionZero,
    Clouds, Moon, ImageInstances,
}
internal enum FalloutSkyResetOrigin { PlayerTransfer, ClimateSelection }
internal enum FalloutSkyChildDisposition { ConstructorNull, Published, Unowned }
internal sealed record FalloutSkyResetContext(Guid Sky, Guid Process, Guid Call, Guid? Main, Guid? Request,
    long Ordinal, FalloutSkyResetOrigin Origin, string SourceContract);
internal sealed record FalloutSkyResetCall(FalloutSkyResetContext Context, long Changed,
    IReadOnlyList<FalloutSkyResetStep> Completed, FalloutSkyResetStep? EnteredChild,
    bool Returned, string? FailureType, string? Error);
internal sealed record FalloutSkyTransferReturn(Guid Sky, Guid Process, Guid Main, Guid Request,
    Guid Call, long Returned, string SourceContract);
internal sealed record FalloutSkyTimeCaches(uint SunriseStart, uint SunriseEnd, uint SunsetStart, uint SunsetEnd);
internal sealed record FalloutSkyChildSnapshot(string Role, string SourceSha256, string Resource,
    IReadOnlyList<FalloutSkyCloudSlotSnapshot> CloudSlots);
internal sealed record FalloutSkyCloudSlotSnapshot(int Slot, int Geometry, int Property,
    string? Texture, string? PropertyTexture, uint BlendBits);
internal sealed record FalloutSkyChildResetReturn(FalloutSkyResetContext Context, Guid Child,
    string Role, IReadOnlyList<string> Completed);
internal interface IFalloutSkyResetChild
{
    Guid Identity { get; }
    Guid Sky { get; }
    string Role { get; }
    FalloutSkyChildSnapshot Capture();
    FalloutSkyChildResetReturn Reset(FalloutSkyResetContext context);
    void Retire();
}
internal sealed record FalloutSkyChildBinding(FalloutSkyChildDisposition Disposition, string? Failure,
    FalloutSkyChildSnapshot? Source);
internal sealed record FalloutSkyTransferHandoff(Guid CapturedSky, Guid CurrentSky, Guid CapturedProcess,
    Guid CurrentProcess, IReadOnlyDictionary<Guid, Guid> ImageInstances);
internal sealed record FalloutSkyTransferSnapshot(string Schema, FalloutSkyTransferDeclaration Source, string Stack,
    Guid CapturedSky, Guid CapturedProcess, long Changed, uint Flags, int Mode, uint HourBits,
    uint BlendBits, uint TransitionBits, FalloutFormKey? Climate, FalloutSkyTimeCaches TimeCaches,
    FalloutFormKey? CurrentWeather, FalloutFormKey? PreviousWeather, FalloutFormKey? OverrideWeather, FalloutFormKey? TargetWeather,
    FalloutSkyChildBinding Clouds, FalloutSkyChildBinding Moon, IReadOnlyList<FalloutSourceSkyImageModifier> Images,
    FalloutSkyResetCall? LastCall, FalloutSkyTransferHandoff? Handoff, string? ModeFailure,
    string? ClockFailure, bool Retired, string? RetirementFailure);

internal readonly record struct FalloutSkyImageWeights(int Primary, int Secondary, float PrimaryWeight, bool Interpolated);
