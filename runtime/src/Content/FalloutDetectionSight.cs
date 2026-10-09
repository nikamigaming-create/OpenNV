namespace OpenNV.Runtime.Content;

// Detection binds part 1 and part 0's BPNT independently of the part selected
// for LookIK. No head-tracking flag, part Node fallback or cone is substituted.
internal sealed record FalloutDetectionSightBindings(FalloutFormKey BodyParts,
    string? HeadTarget, string? TorsoTarget)
{
    internal static FalloutDetectionSightBindings Read(FalloutBodyPartData parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Parts is null || parts.Parts.Any(part => part is null || part.Type >= 15) ||
            parts.Parts.Select(part => part.Type).Distinct().Count() != parts.Parts.Count)
            throw new InvalidDataException("Detection sight has an invalid source body-part table.");
        string? Target(byte slot)
        {
            var part = parts.Parts.SingleOrDefault(part => part.Type == slot);
            return part is null || part.Target.Length == 0 ? null : part.Target;
        }
        return new(parts.Form, Target(1), Target(0));
    }
}
