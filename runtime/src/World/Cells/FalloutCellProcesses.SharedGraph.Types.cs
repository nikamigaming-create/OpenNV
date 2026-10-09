using OpenNV.Runtime.Content;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Security.Cryptography;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCellNativeSource(FalloutCellProcessIdentity Cell, FalloutFormKey? Landscape,
    string? LandscapeSha256, string? TransportSha256)
{
    internal static string TransportDigest(FalloutLandscapeTransport source) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            source.ActiveCell,
            x = source.ActiveCoordinates.X,
            y = source.ActiveCoordinates.Y,
            source.Worldspace,
            source.Landscape,
            source.Flags,
            source.Heights,
            source.Normals,
            source.Colors,
            source.BaseLayers,
            source.AlphaLayers,
            textures = source.Textures.Select(pair => new { identity = pair.Key, source = pair.Value }).ToArray(),
            source.HeightDefault
        }))).ToLowerInvariant();
}
internal sealed record FalloutCellNativeConsumers(FalloutCellNativeSource Source,
    FalloutCellProcessChildPhase Phase, IReadOnlyList<ulong> NativeObjects, string Owner);
internal enum FalloutCellSharedGraphPhase { Preparing, Publishing, Cancelling, Complete, Cancelled, RetiringRoot, RootRetired }
internal sealed record FalloutCellSharedGraphChange(Guid Identity, Guid Attachment, Guid Process, ulong NativeRoot,
    FalloutFormKey BeforeActive, FalloutFormKey AfterActive,
    [property: JsonConverter(typeof(FalloutCellProcessEpochsJson))] IReadOnlyDictionary<FalloutFormKey, long> BeforeEpochs,
    [property: JsonConverter(typeof(FalloutCellProcessEpochsJson))] IReadOnlyDictionary<FalloutFormKey, long> AfterEpochs,
    IReadOnlyList<FalloutCellProcessChild> BeforeChildren,
    IReadOnlyList<FalloutCellProcessPlacedChild> TargetChildren,
    IReadOnlyList<FalloutCellNativeSource> TargetCellSources,
    IReadOnlyList<FalloutCellNativeConsumers> BeforeCellConsumers,
    IReadOnlyList<FalloutCellProcessChild> PublishedChildren,
    IReadOnlyList<FalloutCellNativeConsumers> PublishedCellConsumers,
    IReadOnlyList<FalloutFormKey> DestroyedReferences,
    IReadOnlyList<FalloutFormKey> DestroyedCellConsumers,
    FalloutCellSharedGraphPhase Phase, long Entered, long Changed, string? Failure);
