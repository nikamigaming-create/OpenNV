using System.Security.Cryptography;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSleepWaitBed(FalloutFormKey Reference, string ReferenceSha256,
    FalloutFormKey Base, string BaseSha256, string ModelPath, string ModelSha256, IReadOnlyList<int> EnabledMarkers)
{
    internal FalloutRestRequest Request => new(FalloutRestKind.Sleep, FalloutRestOrigin.BedActivation, Reference, ReferenceSha256, this);
    internal static FalloutSleepWaitBed Read(FalloutPluginStack records, FalloutPlacedReference reference, FalloutNifFile model)
        => Read(records, reference.FormKey, reference.Base, model);
    private static FalloutSleepWaitBed Read(FalloutPluginStack records, FalloutFormKey reference, FalloutFormKey baseKey, FalloutNifFile model)
    {
        var baseRecord = records.GetEffective(baseKey);
        if (FalloutFurnitureSource.ReadKind(baseRecord) != FalloutPlayerFurnitureKind.Sleeping)
            throw new InvalidDataException("Source bed request belongs to another furniture kind.");
        var path = "meshes/" + FalloutDialogueTopic.Text(baseRecord.ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/');
        var seats = FalloutFurnitureSource.ReadSeats(records, baseRecord, model, true);
        var winner = records.GetEffective(reference);
        var source = new FalloutSleepWaitBed(reference, Hash(winner), baseRecord.FormKey, Hash(baseRecord), path, model.Sha256,
            seats.Select(seat => seat.Index).ToArray());
        source.Validate(); return source;
    }
    internal static FalloutSleepWaitBed ReadCurrent(FalloutPluginStack records, FalloutFormKey reference)
    {
        var winner = records.GetEffective(reference);
        var baseKey = FalloutDialogueTopic.RequiredForm(winner, "NAME");
        var baseRecord = records.GetEffective(baseKey);
        var path = "meshes/" + FalloutDialogueTopic.Text(baseRecord.ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/');
        var content = records.OwnedSource ?? throw new NotSupportedException("Rest bed has no selected owned resource source.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Rest bed source model is absent.", path);
        return Read(records, reference, baseKey, FalloutNifFile.Read(bytes));
    }
    internal void Validate()
    {
        if (Reference.ObjectId == 0 || Base.ObjectId == 0 || string.IsNullOrWhiteSpace(Reference.OwnerPlugin) ||
            string.IsNullOrWhiteSpace(Base.OwnerPlugin) || !FalloutPlayerPhysicalSource.Digest(ReferenceSha256) ||
            !FalloutPlayerPhysicalSource.Digest(BaseSha256) || !FalloutPlayerPhysicalSource.Digest(ModelSha256) ||
            string.IsNullOrWhiteSpace(ModelPath) || !ModelPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ||
            EnabledMarkers is null || EnabledMarkers.Count == 0 || EnabledMarkers.Distinct().Count() != EnabledMarkers.Count ||
            EnabledMarkers.Any(marker => marker is < 0 or > 29))
            throw new InvalidDataException("Rest bed has no complete winning record/model/marker identity.");
    }
    internal void RequireCurrent(FalloutSleepWaitBed current)
    {
        Validate(); current.Validate();
        if (Reference != current.Reference || ReferenceSha256 != current.ReferenceSha256 || Base != current.Base ||
            BaseSha256 != current.BaseSha256 || ModelPath != current.ModelPath || ModelSha256 != current.ModelSha256 ||
            !EnabledMarkers.SequenceEqual(current.EnabledMarkers))
            throw new InvalidDataException("Source bed/model/marker identity changed during its rest request.");
    }
    private static string Hash(FalloutPluginRecord record) => Convert.ToHexString(SHA256.HashData(record.ReadData()));
}
