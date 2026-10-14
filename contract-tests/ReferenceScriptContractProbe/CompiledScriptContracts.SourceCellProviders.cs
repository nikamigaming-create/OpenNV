using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    private static void RunFo3TemporaryProviderContracts()
    {
        foreach (var mapped in new[] { false, true })
        {
            var directory = Path.Combine(Path.GetTempPath(), "opennv-cell-provider-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var early = SourceCellReference(0xa10, false);
                if (mapped) UInt(early, 8, 0x4000); // Unselected input has no entered partial-load effects.
                File.WriteAllBytes(Path.Combine(directory, "Bytecode.esm"), Join(Tes4(),
                    Record("STAT", 0x90, Field("EDID", Text("AuthoredCellBase"))), Record("CELL", 0x800, Field("DATA", [1])),
                    SourceCellGroup(6, 0x800, SourceCellGroup(9, 0x800, early, SourceCellReference(0xa20, false)))));
                var middle = mapped ? SourceCellOnamHeader([0xa10], "Bytecode.esm") : Tes4("Bytecode.esm");
                File.WriteAllBytes(Path.Combine(directory, "Middle.esp"), Join(middle,
                    Record("CELL", 0x800, Field("DATA", [1])), SourceCellGroup(6, 0x800, SourceCellGroup(9, 0x800,
                        SourceCellReference(0xa10, false), SourceCellReference(0xa20, false), SourceCellReference(0x01000900, false)))));
                var last = mapped ? SourceCellOnamHeader([0xa10], "Bytecode.esm", "Middle.esp") : Tes4("Bytecode.esm", "Middle.esp");
                if (mapped)
                    last = Record("TES4", 0, last[FalloutPlugin.RecordHeaderSize..], Field("ONAM", U32(0xa20)));
                File.WriteAllBytes(Path.Combine(directory, "Last.esp"), Join(last,
                    Record("CELL", 0x800, Field("DATA", [1])), SourceCellGroup(6, 0x800, SourceCellGroup(9, 0x800,
                        SourceCellReference(0xa10, false), SourceCellReference(0xa20, false), SourceCellReference(0x02000900, false)))));
                using (var records = FalloutPluginStack.Load(directory, ["Bytecode.esm", "Middle.esp", "Last.esp"]))
                {
                    var cellSource = new FalloutCellProcessSource(records); var cell = cellSource.Read(Key(0x800));
                    var rows = cell.References.ToDictionary(row => row.Reference, FalloutFormKeyComparer.Instance);
                    using var reader = new FalloutSourceCellReferenceIngestion(FalloutSourceCellLoaderDeclaration.Read(FalloutSourceMainFamily.Fallout3),
                        records, "authored-ONAM-source-stack");
                    using var links = new FalloutSourceCellReferenceLinks(
                        FalloutSourceCellReferenceLinksDeclaration.ForExecutable(FalloutSourceMainFamily.Fallout3), "authored-ONAM-source-stack",
                        cellSource.Read, form => rows[form], reader);
                    var root = new object(); links.PublishNativeOwner(root); reader.PublishNativeOwner(root);
                    var evidence = reader.ReadEvidence(cell);
                    var expected = mapped ? new[] { new FalloutFormKey("Last.esp", 0x900), Key(0xa10), new("Middle.esp", 0x900), Key(0xa20) } :
                        new[] { new FalloutFormKey("Last.esp", 0x900), new("Middle.esp", 0x900), Key(0xa20), Key(0xa10) };
                    Require(links.Read(cell.Source.Cell).OrderedReferences.Select(row => row.Reference).SequenceEqual(expected),
                        "FO3 ONAM provider table was replaced by final winner order or repeated head insertion.");
                    Require(evidence.Inputs.Count(row => row.Disposition == FalloutSourceCellIngestionDisposition.InsertSelectedTemporary) == 4 &&
                        evidence.Inputs.Count(row => row.Disposition == FalloutSourceCellIngestionDisposition.SkipUnselectedTemporaryProvider) == (mapped ? 2 : 0) &&
                        evidence.Inputs.Count(row => row.Disposition == FalloutSourceCellIngestionDisposition.SkipAlreadyConstructed) == (mapped ? 2 : 4) &&
                        evidence.Inputs.All(row => row.Refusal is null),
                        "FO3 missing-provider allowance, later provider replacement or already constructed object check changed.");
                    var saved = Copy(links.Capture());
                    for (var cycle = 0; cycle < 3; cycle++)
                    {
                        using var coldReader = new FalloutSourceCellReferenceIngestion(evidence.Source, records, saved.Stack);
                        using var coldLinks = new FalloutSourceCellReferenceLinks(saved.Source, saved.Stack, cellSource.Read, form => rows[form], coldReader, saved);
                        var coldRoot = new object(); coldLinks.PublishNativeOwner(coldRoot); coldReader.PublishNativeOwner(coldRoot);
                        Require(coldLinks.Read(cell.Source.Cell).OrderedReferences.Select(row => row.Reference).SequenceEqual(expected) &&
                            coldLinks.Capture().Membership == saved.Membership && coldLinks.Capture().Attempts.Count == saved.Attempts.Count,
                            "FO3 cold provider authentication replayed source construction or changed linked order.");
                        using var state = JsonDocument.Parse(JsonSerializer.Serialize(coldReader.State));
                        Require(state.RootElement.GetProperty("calls").GetInt64() == 0, "FO3 cold provider authentication entered source insertion.");
                        saved = Copy(coldLinks.Capture());
                    }
                }
                if (mapped)
                {
                    var malformed = Record("TES4", 0, Tes4("Bytecode.esm", "Middle.esp")[FalloutPlugin.RecordHeaderSize..], Field("ONAM", [1, 2, 3]));
                    File.WriteAllBytes(Path.Combine(directory, "Last.esp"), malformed);
                    using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm", "Middle.esp", "Last.esp"]);
                    var source = new FalloutCellProcessSource(records);
                    using var reader = new FalloutSourceCellReferenceIngestion(FalloutSourceCellLoaderDeclaration.Read(FalloutSourceMainFamily.Fallout3),
                        records, "authored-malformed-ONAM-source-stack");
                    Reject(() => reader.ReadEvidence(source.Read(Key(0x800))));
                }
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
        Console.WriteLine("OPENNV_FO3_CELL_PROVIDER_PASS missingEntryPermits=true laterONAMReplaces=true firstONAM=true " +
            "unselectedPartialNotEntered=true existingObjectSkips=true coldCycles=3 fullReader=true originalNative=UNEXECUTED");
    }
}
