using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

// A failed successor binding can retain the already-consumed retirement of its
// predecessor without declaring a running procedure or replaying source effects.
internal sealed record FalloutPackageRetirement(long Revision, string? LastEvent,
    FalloutFormKey? LastPackage, string? LastPackageSha256)
{
    internal static FalloutPackageRetirement Capture(FalloutPluginStack records, FalloutPackageEvents events)
    {
        if (events.Active is not null || events.Done || events.Error is not null)
            throw new NotSupportedException("Package failure capture requires a fully retired, healthy predecessor lifecycle.");
        var history = new FalloutPackageRetirement(events.Revision, events.LastEvent, events.LastPackage,
            events.LastPackage is { } package ? Convert.ToHexString(SHA256.HashData(records.GetEffective(package).ReadData())) : null);
        history.Validate(records);
        return history;
    }

    internal void Validate()
    {
        if (Revision < 0 || (Revision == 0 ? LastEvent is not null || LastPackage is not null || LastPackageSha256 is not null :
            LastEvent != "POCA" || LastPackage is not { ObjectId: > 0 } || string.IsNullOrWhiteSpace(LastPackage.Value.OwnerPlugin) ||
            LastPackageSha256 is not { Length: 64 } || !LastPackageSha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("Saved package retirement history is invalid.");
    }

    internal void Validate(FalloutPluginStack records)
    {
        Validate();
        if (LastPackage is not { } package) return;
        var source = records.GetEffective(package);
        if (source.Signature != "PACK" || !Convert.ToHexString(SHA256.HashData(source.ReadData()))
            .Equals(LastPackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved retired package differs from its winning source.");
    }
}
