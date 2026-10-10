using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial record FalloutRoomPortalSnapshot(string EngineSha256, string ContractSha256,
    FalloutFormKey Cell, string RegistrationSha256, long LastNativeGeneration,
    IReadOnlyList<FalloutFormKey?> CachedRooms, string? Failure)
{
    internal void Validate()
    {
        static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        static bool Key(FalloutFormKey value) => !string.IsNullOrWhiteSpace(value.OwnerPlugin) && value.ObjectId is > 0 and <= FalloutFormKey.ObjectIdMask;
        if (!Hash(EngineSha256) || !Hash(ContractSha256) || !Hash(RegistrationSha256) || !Key(Cell) ||
            LastNativeGeneration <= 0 || CachedRooms is null || CachedRooms.Count == 0 ||
            CachedRooms.Any(room => room is { } key && !Key(key)) || Failure is { Length: 0 })
            throw new InvalidDataException("Saved room/portal source camera state is invalid.");
    }
}
