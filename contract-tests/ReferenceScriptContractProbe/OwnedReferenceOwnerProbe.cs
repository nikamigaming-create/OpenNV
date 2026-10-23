using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedReferenceOwnerProbe
{
    internal static void Run(string game, string savePath)
    {
        var bytes = File.ReadAllBytes(savePath);
        var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(bytes) ?? throw new InvalidDataException("Campaign absent.");
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var cases = 0; var inherited = 0;
        foreach (var snapshot in saved.References ?? throw new InvalidDataException("Campaign references absent."))
        {
            if (records.GetEffective(snapshot.Base).Signature != "TERM") continue;
            var reference = records.GetEffective(snapshot.Reference);
            if (reference.ReadSubrecords().Any(field => field.Signature == "XTEL")) continue;
            var cell = records.GetEffective(snapshot.Placement?.Cell ?? snapshot.Cell);
            var cellFlags = cell.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
            if (cellFlags.IsEmpty || (cellFlags[0] & 1) == 0) continue;
            var sourceOwner = Form(reference, "XOWN");
            var local = snapshot.OwnershipOverride?.Owner ?? sourceOwner;
            var cellZone = Form(cell, "XEZN");
            var zone = Form(reference, "XEZN") ?? cellZone;
            FalloutFormKey? zoneOwner = null;
            var watched = new List<FalloutPluginRecord> { reference, cell, records.GetEffective(snapshot.Base) };
            if (zone is { } assigned && records.RuntimeFormId(assigned) != 0x1e)
            {
                var zoneRecord = records.GetEffective(assigned); watched.Add(zoneRecord);
                var data = zoneRecord.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
                if (data.Length != 8) throw new InvalidDataException("Owned zone DATA extent invalid.");
                zoneOwner = zoneRecord.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data));
            }
            var expected = local ?? zoneOwner ?? Form(cell, "XOWN");
            if (expected is null && cellZone is { } fallback && fallback != zone && records.RuntimeFormId(fallback) != 0x1e)
            {
                var zoneRecord = records.GetEffective(fallback); watched.Add(zoneRecord);
                var data = zoneRecord.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
                if (data.Length != 8) throw new InvalidDataException("Owned cell-zone DATA extent invalid.");
                expected = zoneRecord.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data));
            }
            var hashes = watched.Select(record => SHA256.HashData(record.ReadData())).ToArray();
            using var world = new FalloutReferenceWorld(records);
            world.Restore([snapshot]);
            if (world.Ownership(snapshot.Reference).Owner != expected)
                throw new InvalidDataException("Owned terminal effective owner differs from its source/saved inputs.");
            if (local is null && expected is not null) ++inherited;
            var capture = world.Capture();
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(capture))!);
            if (cold.Ownership(snapshot.Reference).Owner != expected ||
                JsonSerializer.Serialize(cold.Capture()) != JsonSerializer.Serialize(capture))
                throw new InvalidDataException("Owned effective owner changed cold or mutated its saved reference.");
            for (var index = 0; index < watched.Count; ++index)
                if (!SHA256.HashData(watched[index].ReadData()).SequenceEqual(hashes[index]))
                    throw new InvalidDataException("Owned source changed.");
            ++cases;
            Console.WriteLine($"OPENNV_OWNED_EFFECTIVE_OWNER_CASE reference={snapshot.Reference} cell={cell.FormKey} " +
                $"local={local} zone={zone} zoneOwner={zoneOwner} effective={expected} " +
                $"referenceHash={Convert.ToHexString(hashes[0])} cellHash={Convert.ToHexString(hashes[1])}");
        }
        if (cases == 0 || inherited == 0 || !File.ReadAllBytes(savePath).SequenceEqual(bytes))
            throw new InvalidDataException("Owned audit needs an actual inherited terminal owner and unchanged campaign bytes.");
        Console.WriteLine($"OPENNV_OWNED_EFFECTIVE_OWNER_PASS cases={cases} inherited={inherited} " +
            $"saveHash={Convert.ToHexString(SHA256.HashData(bytes))} selectedReferencesOnly=true " +
            "interiorSourceAndSavedOverrides=true cold=true sourceUnchanged=true " +
            "fullCampaignAdmissionAlarmAndNativeHacking=unverified");

        FalloutFormKey? Form(FalloutPluginRecord record, string field)
        {
            var fields = record.ReadSubrecords().Where(value => value.Signature == field).ToArray();
            if (fields.Length == 0) return null;
            if (fields.Length != 1 || fields[0].Data.Length != 4) throw new InvalidDataException("Owned ownership form extent invalid.");
            return record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
        }
    }
}
