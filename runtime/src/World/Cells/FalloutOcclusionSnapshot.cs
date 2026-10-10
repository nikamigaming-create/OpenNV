using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutOcclusionPlaneSnapshot(FalloutFormKey Reference, string SourceSha256,
    string MembershipSha256, FalloutFormKey? Room, bool Enabled, FalloutOcclusionCullMode Mode);
internal sealed record FalloutOcclusionSnapshot(string EngineSha256, string ContractSha256,
    FalloutFormKey Cell, string RegistrationSha256, long LastNativeGeneration,
    IReadOnlyList<FalloutOcclusionPlaneSnapshot> Planes, string? Failure)
{
    internal void Validate()
    {
        static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        static bool Key(FalloutFormKey value) => !string.IsNullOrWhiteSpace(value.OwnerPlugin) && value.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        if (!Hash(EngineSha256) || !Hash(ContractSha256) || !Hash(RegistrationSha256) || !Key(Cell) ||
            LastNativeGeneration <= 0 || Planes is null || Planes.Any(row => row is null) || Failure is { Length: 0 } ||
            Planes.Select(row => row.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != Planes.Count)
            throw new InvalidDataException("Saved source occlusion graph has invalid source/order/lifetime identity.");
        foreach (var row in Planes)
            if (!Key(row.Reference) || !Hash(row.SourceSha256) || !Hash(row.MembershipSha256) ||
                row.Room is { } room && !Key(room) || row.Mode != FalloutOcclusionCullMode.Automatic)
                throw new NotSupportedException("Saved occlusion plane contains an unowned source mode or invalid declaration.");
        FalloutOcclusionCullingSource.Read(EngineSha256).RequireCurrent();
    }
}

internal sealed partial record FalloutRoomPortalSnapshot
{
    public FalloutOcclusionSnapshot? Occlusion { get; init; }
}
