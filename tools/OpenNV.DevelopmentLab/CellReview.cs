using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

// A diagnostic of an actual resident source set. It cannot award cell parity:
// the matched retail, final-frame, sound and gameplay evidence stays separate.
internal static class CellReview
{
    internal static int Run(string sourcePath, string snapshotPath)
    {
        RuntimeLiveContentSource.Configure(sourcePath, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var bytes = File.ReadAllBytes(snapshotPath);
        using var snapshot = JsonDocument.Parse(bytes);
        var report = Build(records, content.SaveCompatibilityId, snapshot.RootElement);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-cell-review/v1",
            snapshotSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            plugins = records.Plugins.Select(plugin => new { name = plugin.Plugin.Name, plugin.Sha256 }),
            report,
            parity = "unverified",
            boundary = "Resident reference presence and declared actor/reference failures only. Missing diagnostics never prove correctness. Matched retail state, geometry/material pixels, motion, audio, interactions, persistence and performance require independent evidence.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    internal sealed record Reference(string Identity, string SourceCell, string Base, string Signature, string EditorId, string? Model);
    internal sealed record Failure(string Lane, string Reason, IReadOnlyList<Reference> References);
    internal sealed record Report(string Cell, string SourceCompatibilityId, Guid RuntimeBuild, DateTime CapturedUtc,
        IReadOnlyList<Reference> SourceReferences, IReadOnlyList<Failure> Failures, int ExcludedNonresidentDiagnostics);

    internal static Report Build(FalloutPluginStack records, string sourceIdentity, JsonElement state)
    {
        if (state.GetProperty("detail").GetString() != "complete-runtime-snapshot")
            throw new InvalidDataException("Cell review requires the native detailed state command, not a live summary.");
        var scope = state.GetProperty("reviewScope");
        if (scope.GetProperty("sourceCompatibilityId").GetString() != sourceIdentity)
            throw new InvalidDataException("Cell review source stack differs from the captured runtime.");
        var build = scope.GetProperty("runtimeBuild").GetGuid();
        if (build == Guid.Empty) throw new InvalidDataException("Cell review has no captured runtime build.");
        var captured = scope.GetProperty("capturedUtc").GetDateTime();
        if (captured.Kind != DateTimeKind.Utc) throw new InvalidDataException("Cell review capture time is not UTC.");
        var cell = state.GetProperty("cell").GetString()!;
        if (records.GetEffective(Form(cell)).Signature != "CELL")
            throw new InvalidDataException("Cell review identity is not a winning CELL.");
        var references = new Dictionary<string, Reference>(StringComparer.Ordinal);
        foreach (var value in scope.GetProperty("references").EnumerateArray())
        {
            var identity = value.GetString()!;
            var record = records.GetEffective(Form(identity));
            if (record.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS"))
                throw new InvalidDataException("Cell review scope contains a non-reference.");
            var baseKey = FalloutDialogueTopic.RequiredForm(record, "NAME");
            var parent = FalloutCellSceneReader.ParentCell(record) ?? throw new InvalidDataException("Reference has no source cell.");
            var definition = records.TryGetEffective(baseKey, out var found) ? found : null;
            string Text(string field) => definition?.ReadSubrecords().Where(value => value.Signature == field)
                .Select(value => FalloutDialogueTopic.Text(value.Data.Span)).SingleOrDefault() ?? "";
            if (!references.TryAdd(identity, new(identity, parent.ToString(), baseKey.ToString(),
                    definition?.Signature ?? "unresolved-base", Text("EDID"), Text("MODL") is { Length: > 0 } model ? model : null)))
                throw new InvalidDataException("Cell review duplicates a resident reference.");
        }
        var failures = new Dictionary<(string Lane, string Reason), HashSet<string>>();
        var excluded = 0;
        void Add(string lane, string reason, string identity, bool allowNonresident)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new InvalidDataException("Cell review contains an empty failure.");
            if (!references.ContainsKey(identity))
            {
                if (!allowNonresident) throw new InvalidDataException("Missing reference is outside the captured resident scope.");
                excluded++; return;
            }
            if (!failures.TryGetValue((lane, reason), out var identities)) failures.Add((lane, reason), identities = []);
            identities.Add(identity);
        }
        var prefix = $"world/active-cell/{cell}/";
        foreach (var missing in state.GetProperty("missingRuntimeReferences").EnumerateArray())
        {
            var identity = missing.GetString()!;
            if (!identity.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Cell review has an unknown missing-reference scope.");
            Add("reference-presence", "No runtime observation for a source reference.", identity[prefix.Length..], false);
        }
        foreach (var (field, lane) in new[] { ("referenceDivergences", "reference-presentation"), ("actorDivergences", "actor") })
            foreach (var failure in state.GetProperty(field).EnumerateArray())
                Add(lane, failure.GetProperty("value").GetString()!, failure.GetProperty("key").GetString()!, true);
        return new(cell, sourceIdentity, build, captured, references.Values.OrderBy(value => value.Identity, StringComparer.Ordinal).ToArray(),
            failures.OrderByDescending(row => row.Value.Count).ThenBy(row => row.Key.Lane, StringComparer.Ordinal).ThenBy(row => row.Key.Reason, StringComparer.Ordinal)
                .Select(row => new Failure(row.Key.Lane, row.Key.Reason, row.Value.Order(StringComparer.Ordinal).Select(identity => references[identity]).ToArray())).ToArray(), excluded);
    }

    private static FalloutFormKey Form(string value)
    {
        var separator = value.LastIndexOf(':');
        if (separator < 1 || !uint.TryParse(value.AsSpan(separator + 1), System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out var id) || id is 0 or > FalloutFormKey.ObjectIdMask)
            throw new InvalidDataException("Cell review has an invalid source identity.");
        var key = new FalloutFormKey(value[..separator], id);
        return key.ToString() == value ? key : throw new InvalidDataException("Cell review identity is not canonical.");
    }
}
