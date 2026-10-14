using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static object AuditOwnedSourceCellIngestion(FalloutPluginStack records, string engine, FalloutFormKey? selectedCell = null)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("CELL ingestion audit requires the actual owned records lifetime.");
        records.RequireLiveLoadReceipt(records.LoadReceipt);
        var declaration = FalloutExecutableStringTable.ReadCellReferenceLinks(owned.FalloutExecutablePath, engine);
        var loader = FalloutSourceCellLoaderDeclaration.Read(engine);
        var cell = selectedCell ?? records.EffectiveRecordsInRegistrationOrder("CELL").First(record =>
            !record.IsDeleted && (record.Flags & 0x400) == 0 && FalloutCellSceneReader.ParentWorldspace(record) is null).FormKey;
        var source = new FalloutCellProcessSource(records);
        var data = source.Read(cell);
        var references = data.References.ToDictionary(row => row.Reference, FalloutFormKeyComparer.Instance);
        using var reader = new FalloutSourceCellReferenceIngestion(loader, records, owned.StackId);
        using var links = new FalloutSourceCellReferenceLinks(declaration, owned.StackId, source.Read, form => references[form], reader);
        var root = new object(); reader.PublishNativeOwner(root); links.PublishNativeOwner(root);
        var evidence = reader.ReadEvidence(data);
        Require(evidence.Inputs.All(input => input.Refusal is null), "Owned CELL input requires an independent source producer.");
        var current = links.Read(cell);
        Require(current.InitialInsertionsReturned && current.Members.Count == data.References.Count,
            "Owned CELL did not return the complete actual selected insertion stream.");
        var order = current.OrderedReferences.ToArray();
        var saved = Copy(links.Capture());
        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var coldReader = new FalloutSourceCellReferenceIngestion(loader, records, owned.StackId);
            using var cold = new FalloutSourceCellReferenceLinks(declaration, owned.StackId, source.Read, form => references[form], coldReader, saved);
            var coldRoot = new object(); coldReader.PublishNativeOwner(coldRoot); cold.PublishNativeOwner(coldRoot);
            Require(cold.Read(cell).OrderedReferences.SequenceEqual(order) && cold.Capture().Membership == saved.Membership &&
                cold.Capture().Attempts.Count == saved.Attempts.Count,
                "Owned cold CELL reconstruction replayed insertion or changed original linked membership order.");
            using var state = JsonDocument.Parse(JsonSerializer.Serialize(coldReader.State));
            Require(state.RootElement.GetProperty("calls").GetInt64() == 0, "Owned cold CELL authentication entered initial reference loading.");
            Reject(() => coldReader.RequireEvidence(evidence with { ProviderHeadersSha256 = new('f', 64) }, data));
            saved = Copy(cold.Capture());
        }
        var headers = records.Plugins.Select(context => context.Plugin.Records.First().ReadSubrecords()
            .FirstOrDefault(field => field.Signature == "ONAM")).ToArray();
        records.RequireLiveLoadReceipt(records.LoadReceipt);
        return new
        {
            source = declaration,
            loader,
            cell,
            referenceCount = current.Members.Count,
            engineConstructedStaticBases = current.Members.Count(member => member.Source.BaseSha256 is null),
            selectedInputs = evidence.Inputs.Count,
            providerHeaders = headers.Count(field => field.Signature is not null),
            providerWords = headers.Sum(field => field.Data.Length / sizeof(uint)),
            evidence.ProviderHeadersSha256,
            providerSkipped = evidence.Inputs.Count(input => input.Disposition == FalloutSourceCellIngestionDisposition.SkipUnselectedTemporaryProvider),
            existingObjectSkipped = evidence.Inputs.Count(input => input.Disposition == FalloutSourceCellIngestionDisposition.SkipAlreadyConstructed),
            coldCycles = 3,
            scope = "AUTHORED_CALLER_PROTOCOL_WITH_ACTUAL_OWNED_RECORDS",
            originalExecution = "UNEXECUTED",
            wholeCell = "UNRETURNED",
        };
    }
}
