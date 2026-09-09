using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class CellReviewContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        var report = CellReview.Build(records, "fixture:v1", State());
        Require(report.SourceReferences.Count == 2 && report.SourceReferences.Single(value => value.Identity == Key(0x902)).SourceCell == Key(0x801),
            "Adjacent resident references lost their original source cell.");
        Require(report.Failures.Count == 2 && report.Failures.Single(value => value.Lane == "reference-presentation").References.Count == 2 &&
            report.ExcludedNonresidentDiagnostics == 1, "Shared failures were duplicated or a previous cell's actor error entered the current scope.");
        Reject(() => CellReview.Build(records, "different-source", State()));
        Reject(() => CellReview.Build(records, "fixture:v1", State(detail: "live-summary")));
        Reject(() => CellReview.Build(records, "fixture:v1", State(build: Guid.Empty)));
        Reject(() => CellReview.Build(records, "fixture:v1", State(references: [Key(0x900), Key(0x900)])));
        Reject(() => CellReview.Build(records, "fixture:v1", State(missing: Key(0x903))));
        Reject(() => CellReview.Build(records, "fixture:v1", State(missing: Key(0x900), scopeCell: Key(0x801))));
        Console.WriteLine("OPENNV_CELL_REVIEW_CONTRACT_PASS sourceIdentity=true sourceAncestry=true groupedFailures=true residentScope=true invalidRejected=true parity=unverified");
    }

    private static JsonElement State(string detail = "complete-runtime-snapshot", Guid? build = null,
        string[]? references = null, string? missing = null, string? scopeCell = null) => JsonSerializer.SerializeToElement(new
        {
            detail,
            cell = Key(0x800),
            reviewScope = new
            {
                sourceCompatibilityId = "fixture:v1",
                runtimeBuild = build ?? Guid.Parse("ab40aa5e-943d-4d0e-8ab1-1932b812ab28"),
                capturedUtc = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc),
                references = references ?? [Key(0x900), Key(0x902)],
            },
            missingRuntimeReferences = new[] { $"world/active-cell/{scopeCell ?? Key(0x800)}/{missing ?? Key(0x900)}" },
            referenceDivergences = new[] { new { key = Key(0x900), value = "Unsupported source controller." }, new { key = Key(0x902), value = "Unsupported source controller." } },
            actorDivergences = new[] { new { key = Key(0x901), value = "A nonresident actor's failure." } },
        });
    private static string Key(uint id) => new FalloutFormKey("Base.esm", id).ToString();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid cell-review input was accepted.");
    }
}
