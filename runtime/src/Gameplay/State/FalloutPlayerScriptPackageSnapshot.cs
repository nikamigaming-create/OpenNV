using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerScriptPackageSnapshot(FalloutFormKey Package, string PackageSha256,
    FalloutFormKey? Idle, string? AnimationSha256, int Cursor, bool PackageEvent, bool Complete,
    double Elapsed, double Wait, string? EventKind = null, FalloutFormKey? PendingPackage = null,
    string? PendingPackageSha256 = null)
{
    internal string? Phase => EventKind ?? (PackageEvent ? "POBA" : null);
    internal void Validate()
    {
        static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        if (string.IsNullOrWhiteSpace(Package.OwnerPlugin) || Package.ObjectId == 0 || !Hash(PackageSha256) ||
            Idle is { } idle && (string.IsNullOrWhiteSpace(idle.OwnerPlugin) || idle.ObjectId == 0) ||
            Idle.HasValue != (AnimationSha256 is not null) || Idle.HasValue && !Hash(AnimationSha256) ||
            Cursor < 0 || !double.IsFinite(Elapsed) || Elapsed < 0 || !double.IsFinite(Wait) || Wait < 0 ||
            Idle is null && (Elapsed != 0 || PackageEvent) || Complete && (Idle is not null || Wait != 0) ||
            EventKind is not (null or "POBA" or "POCA") || EventKind is not null && !PackageEvent ||
            PendingPackage.HasValue != (PendingPackageSha256 is not null) || PendingPackage.HasValue && !Hash(PendingPackageSha256) ||
            PendingPackage is { } pending && (string.IsNullOrWhiteSpace(pending.OwnerPlugin) || pending.ObjectId == 0) ||
            (Phase == "POCA") != PendingPackage.HasValue)
            throw new InvalidDataException("Saved player script-package state is invalid.");
    }
}
