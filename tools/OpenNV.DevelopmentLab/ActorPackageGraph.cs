using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

// This inventories possible authored assignments, not conditions or runtime selection.
internal static class ActorPackageGraph
{
    internal static int Run(FalloutPluginStack records)
    {
        var actors = records.EffectiveRecords("NPC_").Concat(records.EffectiveRecords("CREA")).ToArray();
        var references = records.EffectiveRecords("ACHR").Concat(records.EffectiveRecords("ACRE")).ToArray();
        var actorRows = new List<object>();
        var actorFailures = new List<object>();
        foreach (var actor in actors)
        {
            try
            {
                var owner = FalloutActorTemplateOwner.Resolve(records, actor, 32);
                var packages = owner.ReadSubrecords().Where(field => field.Signature == "PKID").Select((field, index) =>
                {
                    if (field.Data.Length != 4) throw new InvalidDataException("Actor package identity has an invalid extent.");
                    var key = owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                    var source = records.GetEffective(key);
                    if (source.Signature != "PACK") throw new InvalidDataException($"Package {key} does not resolve to PACK.");
                    var data = FalloutPackageData.Read(source);
                    return new { priority = index, source = key.ToString(), source.IsDeleted, data.Procedure,
                        sha256 = Convert.ToHexString(SHA256.HashData(source.ReadData())) };
                }).ToArray();
                actorRows.Add(new { source = actor.FormKey.ToString(), actor.Signature, deleted = actor.IsDeleted,
                    sourceSha256 = Convert.ToHexString(SHA256.HashData(actor.ReadData())),
                    owner = owner.FormKey.ToString(), ownerSha256 = Convert.ToHexString(SHA256.HashData(owner.ReadData())), packages });
            }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or KeyNotFoundException)
            { actorFailures.Add(new { source = actor.FormKey.ToString(), actor.Signature, error = error.Message }); }
        }
        var referenceRows = new List<object>();
        var referenceFailures = new List<object>();
        foreach (var reference in references)
        {
            try
            {
                var baseKey = FalloutDialogueTopic.RequiredForm(reference, "NAME");
                var baseRecord = records.GetEffective(baseKey);
                referenceRows.Add(new { source = reference.FormKey.ToString(), reference.Signature, deleted = reference.IsDeleted,
                    baseForm = baseKey.ToString(), baseSignature = baseRecord.Signature,
                    cell = FalloutCellSceneReader.ParentCell(reference)?.ToString(),
                    sha256 = Convert.ToHexString(SHA256.HashData(reference.ReadData())) });
            }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or KeyNotFoundException or InvalidOperationException)
            { referenceFailures.Add(new { source = reference.FormKey.ToString(), reference.Signature, error = error.Message }); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-actor-package-graph/v1",
            actorDenominator = actors.Length, actorRows, actorFailures, referenceDenominator = references.Length,
            referenceRows, referenceFailures,
            boundary = "Authored winning links and invariant template owners only; unresolved leveled selections, eligibility, runtime execution and retail behavior are not inferred." }));
        return actorFailures.Count == 0 && referenceFailures.Count == 0 ? 0 : 1;
    }
}
