namespace OpenNV.Runtime.Content;

// Read-only refusal evidence. It grants no completion or save admission.
internal sealed record FalloutAnimationSoundCaptureBlocker(long Generation, FalloutFormKey Sound,
    string SoundSha256, string TextKey, string? Path, string? MediaSha256,
    FalloutAnimationSoundEnd End, string? Error);

internal sealed record FalloutAnimationSoundHistoryDiagnostic(FalloutFormKey Reference,
    long Generation, bool Ready, string? OpaqueError, IReadOnlyList<FalloutAnimationSoundCaptureBlocker> Unsettled);
