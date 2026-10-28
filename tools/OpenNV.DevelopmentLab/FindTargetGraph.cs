using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class FindTargetGraph
{
    internal static int Run(FalloutPluginStack records)
    {
        var packages = records.EffectiveRecords("PACK").ToArray();
        var finds = new List<object>();
        var failures = new List<object>();
        var nonFind = 0;
        foreach (var record in packages)
        {
            try
            {
                var data = FalloutPackageData.Read(record);
                if (data.Procedure != 0) { nonFind++; continue; }
                var find = FalloutFindPackage.Read(record);
                var target = find.Target;
                var declaration = find.Source;
                finds.Add(new { source = record.FormKey.ToString(), deleted = record.IsDeleted,
                    sha256 = Convert.ToHexString(SHA256.HashData(record.ReadData())), data.Flags, data.BehaviorFlags, data.SpecificFlags,
                    declaration.LocationType, location = declaration.LocationReference?.ToString(), declaration.LocationRadius,
                    rawLocation = declaration.LocationDeclaration?.RawLocation, resolvedLocation = declaration.LocationDeclaration?.Form?.ToString(),
                    target.Type, target.RawTarget, form = target.Form?.ToString(), target.CountOrDistance, target.AuxiliaryBits,
                    find.AllowKilling, find.AllowFalls });
            }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or KeyNotFoundException)
            { failures.Add(new { source = record.FormKey.ToString(), error = error.Message }); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-find-target-graph/v1", denominator = packages.Length,
            nonFind, decodedFind = finds.Count, failures, finds,
            boundary = "Winning source declarations only; runtime ownership, execution and retail behavior unverified." }));
        return failures.Count == 0 ? 0 : 1;
    }
}
