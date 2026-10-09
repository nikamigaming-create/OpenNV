using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerPackageResultSnapshot(string Kind, long Executions,
    FalloutScriptResultReceipt Receipt);

internal sealed record FalloutPlayerScriptPackageSnapshot(FalloutFormKey Package, string PackageSha256,
    FalloutFormKey? Idle, string? AnimationSha256, int Cursor, bool PackageEvent, bool Complete,
    double Elapsed, double Wait, string? EventKind = null, FalloutFormKey? PendingPackage = null,
    string? PendingPackageSha256 = null, FalloutIdleAnimationPlaybackSnapshot? Playback = null,
    string? IdleSha256 = null, ulong? SoundRandomState = null,
    IReadOnlyList<FalloutPlayerPackageResultSnapshot>? EventResults = null, string? ExitAction = null)
{
    internal string? Phase => EventKind;
    internal void Validate()
    {
        static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId == 0 || !Hash(PackageSha256) ||
            Idle is { } idle && (string.IsNullOrWhiteSpace(idle.OwnerPlugin) || idle.ObjectId == 0) ||
            Idle.HasValue != (AnimationSha256 is not null) || Idle.HasValue && !Hash(AnimationSha256) ||
            Cursor < 0 || !double.IsFinite(Elapsed) || Elapsed < 0 || !double.IsFinite(Wait) || Wait < 0 ||
            Idle is null && (Elapsed != 0 || PackageEvent) || Complete && (Idle is not null || Wait != 0) ||
            EventKind is not (null or "POBA" or "POCA" or "POEA") || PackageEvent != (EventKind is not null) ||
            PendingPackage.HasValue != (PendingPackageSha256 is not null) || PendingPackage.HasValue && !Hash(PendingPackageSha256) ||
            PendingPackage is { } pending && (string.IsNullOrWhiteSpace(pending.OwnerPlugin) || pending.ObjectId == 0) ||
            PendingPackage.HasValue && Phase != "POCA" || Phase == "POCA" && PendingPackage is null && Playback is null ||
            (Playback is not null) != (IdleSha256 is not null) || Idle.HasValue != (Playback is not null) ||
            Playback is not null && !Hash(IdleSha256) ||
            ExitAction is not (null or "remove" or "complete") || (ExitAction is not null) != (Phase == "POEA") ||
            ExitAction is not null && PendingPackage is not null || EventResults is null ||
            EventResults.Any(result => result is null || result.Kind is not ("POBA" or "POCA" or "POEA") ||
                result.Executions <= 0 || result.Receipt is null || result.Receipt.Program != Package || !result.Receipt.Completed) ||
            EventResults.Select(result => result.Kind).Distinct(StringComparer.Ordinal).Count() != EventResults.Count)
            throw new InvalidDataException("Saved player script-package state is invalid.");
        Playback?.Validate();
    }
}
