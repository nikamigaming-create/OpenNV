using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutImageSpaceSkyHdr(uint Bits, string? Unowned);

internal sealed partial class FalloutImageSpaceState
{
    private readonly Guid _skyHdrImageOwner = Guid.NewGuid();
    private long _skyHdrImagePublication;
    private static FalloutImageSpaceSkyHdr ReadSkyHdrComposition(float scalar, IReadOnlyList<FalloutActiveImageModifier> modifiers,
        IReadOnlyList<FalloutSourceSkyImageContribution> sky)
    {
        if (!float.IsFinite(scalar)) throw new InvalidDataException("Image-space Sky scalar is not an actual finite Float32.");
        string? failure = null;
        // Neutral IMAD transport is admitted separately from a general weighted
        // original HDR curve manager. The current source fields are still
        // composed by the ordinary IMGS owner; no lighting sampler is used.
        foreach (var active in modifiers)
        {
            var time = active.Source.NormalizedTime(active.ElapsedSeconds);
            if (FalloutImageSpaceModifier.Sample(active.Source.Multiply[8], time, 1f) != 1f ||
                FalloutImageSpaceModifier.Sample(active.Source.Add[8], time, 0f) != 0f)
                failure ??= "source-ImageSpace-Sky-HDR-nonneutral-modifier-manager-order-and-curve-consumer-unowned/" + active.Source.Form;
        }
        foreach (var contribution in sky)
            if (contribution.Instance.Program == FalloutSourceSkyImageProgramKind.WinningImad)
                failure ??= "source-ImageSpace-Sky-HDR-winning-weather-IMAD-manager-consumer-unowned/" + contribution.Instance.Identity;
        return new(BitConverter.SingleToUInt32Bits(scalar), failure);
    }
    internal FalloutSkyHdrImageInput ReadCurrentSkyHdrInput(FalloutImageSpace source, FalloutImageSpaceFrame composed,
        FalloutSkyHdrState hdr, bool alternate)
    {
        var value = composed.SkyHdr ?? throw new NotSupportedException("Image-space did not return its actual source Sky scalar field.");
        return new(_skyHdrImageOwner, hdr.Process, Guid.NewGuid(), checked(++_skyHdrImagePublication), source.Form,
            source.FormVersion, source.DnamSha256, value.Bits, hdr.Source.EngineSha256, hdr.Source.Contract, alternate, value.Unowned);
    }
}
