using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class OwnedIdlePackageFieldsProbe
{
    internal static void Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        _ = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var total = 0; var present = 0; var emptyCount = 0; var shortData = 0;
        var failures = new List<string>();
        foreach (var record in records.EffectiveRecords("PACK"))
        {
            try
            {
                var fields = record.ReadSubrecords().ToArray();
                var data = fields.Single(field => field.Signature == "PKDT").Data;
                var selected = FalloutIdlePackageRecordFields.Read(record);
                if (selected.Kind != data.Span[4]) throw new InvalidDataException("Raw package kind differs.");
                ++total; if (data.Length == 8) ++shortData;
                if (selected.HasIdleCollection) ++present;
                foreach (var count in fields.Where(field => field.Signature == "IDLC"))
                {
                    var value = count.Data.Length switch
                    {
                        1 => count.Data.Span[0],
                        4 => BinaryPrimitives.ReadUInt32LittleEndian(count.Data.Span),
                        _ => throw new InvalidDataException("Idle count has unknown source extent.")
                    };
                    if (value == 0)
                    {
                        if (!selected.HasIdleCollection) throw new InvalidDataException("Empty collection became a null pointer.");
                        ++emptyCount;
                    }
                }
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException)
            { failures.Add(record.FormKey + ":" + error.Message); }
        }
        if (total == 0 || failures.Count != 0)
            throw new InvalidDataException("Owned idle package field audit failed: " + string.Join(';', failures.Take(8)));
        Console.WriteLine("OPENNV_OWNED_IDLE_PACKAGE_FIELDS_PASS " + JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-idle-package-fields/v1", stack = content.StackId, total,
            collections = present, emptyCount, shortData, failures = failures.Count,
            ownedGetterAndConstructorProof = true, sourceRuntimePackageFactory = "UNEXECUTED",
            gameLaunched = false, framesRecorded = false, gameplay = "UNEXECUTED"
        }));
    }
}
