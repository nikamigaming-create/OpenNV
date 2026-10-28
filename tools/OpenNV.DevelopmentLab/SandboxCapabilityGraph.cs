using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class SandboxCapabilityGraph
{
    internal static int Run(FalloutPluginStack records)
    {
        var packages = records.EffectiveRecords("PACK").ToArray();
        var rows = new List<object>();
        var failures = new List<object>();
        var other = 0;
        foreach (var record in packages)
        {
            FalloutPackageData data;
            try { data = FalloutPackageData.Read(record); }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or KeyNotFoundException)
            { failures.Add(new { source = record.FormKey.ToString(), stage = "package-data", error = error.Message }); continue; }
            if (data.Procedure != 12) { other++; continue; }
            FalloutScriptPackage declaration;
            try { declaration = FalloutScriptPackage.Read(record); }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or KeyNotFoundException)
            { failures.Add(new { source = record.FormKey.ToString(), stage = "declaration", error = error.Message }); continue; }
            string? refusal = null;
            try { _ = FalloutSandboxPackage.Read(record); }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or KeyNotFoundException)
            { refusal = error.Message; }
            rows.Add(new { source = record.FormKey.ToString(), declaration.EditorId, deleted = record.IsDeleted,
                sha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())), data.Flags, data.BehaviorFlags,
                data.SpecificFlags, declaration.LocationType, location = declaration.LocationReference?.ToString(),
                rawLocation = declaration.LocationDeclaration?.RawLocation, resolvedLocation = declaration.LocationDeclaration?.Form?.ToString(),
                declaration.LocationRadius, idleCount = declaration.Idles.Count, eventCount = declaration.EventPrograms.Count,
                runtimeReaderAccepted = refusal is null, runtimeReaderRefusal = refusal });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-sandbox-capability-graph/v1",
            denominator = packages.Length, otherProcedures = other, decodedSandbox = rows.Count, failures, rows,
            boundary = "Winning declarations and Sandbox reader admission only; actor reachability, runtime execution, persistence and retail behavior remain unverified." }));
        return failures.Count == 0 ? 0 : 1;
    }
}
