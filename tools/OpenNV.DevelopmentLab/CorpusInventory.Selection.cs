using System.Globalization;
using System.Numerics;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CorpusInventory
{
    internal sealed record Command(DevelopmentLabSource.Command Source, bool SelectionVariants);
    internal sealed record Variant(string Mask, IReadOnlyList<FalloutModSelection> Mods, bool AutomaticOrder);
    internal sealed record Admission(FalloutModStackInstallation? Setup, IReadOnlyList<string> CompatibilityIssues)
    {
        internal bool SourceAdmitted => (Setup is null || Setup.MissingDependencies.Count == 0) && CompatibilityIssues.Count == 0;
    }

    internal static Command ParseCommand(string[] arguments)
    {
        if (arguments.Length < 2) throw new ArgumentException("corpus requires an installation and a fresh output directory.");
        var flags = arguments.Select((value, index) => (value, index)).Where(row => row.value == "--selection-variants").ToArray();
        if (flags.Length > 1) throw new ArgumentException("Repeated corpus option: --selection-variants.");
        var mod = Array.IndexOf(arguments, "--mod");
        if (flags.Length == 1 && (flags[0].index < 2 || mod >= 0 && mod < flags[0].index))
            throw new ArgumentException("--selection-variants follows the output directory and precedes a final --mod dependency list.");
        var source = DevelopmentLabSource.ParseCommand(["corpus", .. arguments.Where(value => value != "--selection-variants")]);
        if (source.Arguments.Length != 3) throw new ArgumentException("Unexpected corpus argument.");
        return new(source, flags.Length != 0);
    }

    internal static IEnumerable<Variant> SelectionVariants(FalloutModStackSelection? selection)
    {
        var mods = ConfiguredMods(selection);
        var count = BigInteger.One << mods.Count;
        for (BigInteger mask = 0; mask < count; ++mask)
            yield return new(mask.ToString(CultureInfo.InvariantCulture),
                mods.Where((_, index) => (mask & (BigInteger.One << index)) != 0).ToArray(), selection?.AutomaticOrder ?? true);
    }

    private static IReadOnlyList<FalloutModSelection> ConfiguredMods(FalloutModStackSelection? selection)
    {
        var mods = selection?.Mods ?? [];
        if (mods.Any(mod => mod is null || string.IsNullOrWhiteSpace(mod.Id) || string.IsNullOrWhiteSpace(mod.Root) ||
            mod.AdditionalRoots is null || mod.AdditionalRoots.Any(string.IsNullOrWhiteSpace)) ||
            mods.Select(mod => mod.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mods.Count)
            throw new InvalidDataException("Configured variant axes require unique catalog identities and complete root selections.");
        foreach (var mod in mods) _ = FalloutModCatalog.Get(mod.Id);
        return mods;
    }

    internal static Admission InspectVariant(string selectedRoot, Variant variant)
    {
        if (variant.Mods.Count == 0) return new(null, []);
        var setup = FalloutModStackInstallation.Detect(selectedRoot, variant.Mods, variant.AutomaticOrder);
        return new(setup, FalloutModLoadOrder.CompatibilityIssues(setup.ActivePlugins));
    }

    internal static int RunCommand(string[] arguments)
    {
        Command command;
        try { command = ParseCommand(arguments); _ = ConfiguredMods(command.Source.Selection); }
        catch (Exception error) when (SourceFailure(error))
        { Console.Error.WriteLine(error); return 2; }
        var selectedRoot = command.Source.Arguments[1];
        var output = command.Source.Arguments[2];
        var selection = command.Source.Selection;
        var selectedRoots = new[] { selectedRoot }.Concat((selection?.Mods ?? []).SelectMany(mod =>
            new[] { mod.Root }.Concat(mod.AdditionalRoots))).ToArray();
        var outputWasFresh = false;
        // Refuse any existing output before an admission error can append to somebody else's evidence.
        try
        {
            var fullOutput = Path.GetFullPath(output);
            ValidateOutputLocation(fullOutput, selectedRoots);
            if (Directory.Exists(fullOutput) || System.IO.File.Exists(fullOutput)) throw new IOException("Corpus output must be a fresh directory.");
            outputWasFresh = true;
            using var reuse = new CorpusByteEvidenceCache();
            if (command.SelectionVariants) return RunVariants(selectedRoot, selection, output, selectedRoots, reuse);
            var setup = selection?.Resolve(selectedRoot);
            using var content = setup is null ? DevelopmentLabSource.Open(selectedRoot, null) : setup.OpenSource();
            using var records = FalloutPluginStack.Load(content.PluginSources);
            return Run(records, content, output, reuse, setup?.Dependencies);
        }
        catch (Exception error) when (SourceFailure(error))
        {
            Console.Error.WriteLine(error);
            // Source admission can fail before the ordinary corpus owner creates its directory.
            // Keep the original refusal, never synthesize a narrower or partially loaded stack.
            if (outputWasFresh)
            {
                try
                {
                    var directory = Directory.Exists(Path.GetFullPath(output)) ? Path.GetFullPath(output) : FreshDirectory(output, selectedRoots);
                    WriteReport(directory, "source-admission-refusal.json", new { schema = "opennv-corpus-source-refusal/v1",
                        selectedRoot, selection, errorType = error.GetType().FullName, error = error.ToString(),
                        winningRecordDenominator = "unknown", winningResourceDenominator = "unknown", runtimeReady = false });
                }
                catch (Exception outputError) when (SourceFailure(outputError)) { Console.Error.WriteLine(outputError); }
            }
            return 1;
        }
    }

    private static int RunVariants(string selectedRoot, FalloutModStackSelection? selection, string output,
        IReadOnlyList<string> selectedRoots, CorpusByteEvidenceCache reuse)
    {
        var variants = SelectionVariants(selection);
        var directory = FreshDirectory(output, selectedRoots);
        var planned = (BigInteger.One << (selection?.Mods.Count ?? 0)).ToString(CultureInfo.InvariantCulture);
        BigInteger visited = 0, failed = 0;
        var complete = false;
        using var rows = new StreamWriter(Path.Combine(directory, "selection-variants.jsonl"), append: false);
        WriteReport(directory, "selection-catalog.json", FalloutModCatalog.All.Select(definition => new
        {
            definition.Id, definition.Title, definition.PluginChoices, definition.Settings,
            configuredSelections = (selection?.Mods ?? []).Where(mod => mod.Id == definition.Id).ToArray(),
            configuration = (selection?.Mods ?? []).Any(mod => mod.Id == definition.Id) ? "configured" : "unconfigured",
            alternateEntryChoice = "ordinary-launcher-selection-only", nativeExtensionExecution = "uninspected"
        }).ToArray());
        void Summary() => WriteReport(directory, "selection-summary.json", new
        {
            schema = "opennv-owned-corpus-selection-domain/v1", selectedRoot, selection,
            configuredAxes = selection?.Mods.Select(mod => mod.Id).ToArray() ?? [], plannedVariants = planned,
            visitedVariants = visited.ToString(CultureInfo.InvariantCulture), failedVariants = failed.ToString(CultureInfo.InvariantCulture),
            domainEnumerationComplete = complete, remainingVariantDomain = complete ? "none" : "unexecuted",
            sourceReadOutcome = complete && failed.IsZero ? "accounted" : "failed-or-unexecuted", runtimeReady = false,
            reuse = reuse.State,
            boundary = "Exactly the on/off subsets of supplied catalog selections, retaining their actual package/dependency roots and automatic/manual mode. Each subset is resolved by the existing launcher/catalog and separately audits its actual winning graph. Off axes do not remove roots retained by another selection. This does not enumerate unavailable packages, alternative installation choices, arbitrary orders/settings, JAM toggles, native interfaces or behavioral combinations. No file-presence or byte-read outcome accepts decoding, runtime or parity."
        });
        Summary();
        try
        {
            foreach (var variant in variants)
            {
                var child = Path.Combine(directory, "variant-" + variant.Mask);
                FalloutModStackInstallation? setup = null;
                var variantSelection = variant.Mods.Count == 0 ? null : new FalloutModStackSelection(variant.Mods, variant.AutomaticOrder);
                try
                {
                    var admission = InspectVariant(selectedRoot, variant);
                    setup = admission.Setup;
                    var issues = admission.CompatibilityIssues;
                    if (!admission.SourceAdmitted && setup is not null)
                    {
                        ++failed;
                        WriteRow(rows, new { variant.Mask, selection = variantSelection, admission = "refused",
                            setup.ContentRoots, setup.ActivePlugins, setup.Settings, setup.Dependencies,
                            missingDependencies = setup.MissingDependencies, compatibilityIssues = issues,
                            corpus = "uninspected", winningDenominator = "unknown", runtimeReady = false });
                    }
                    else
                    {
                        using var content = setup is null ? DevelopmentLabSource.Open(selectedRoot, null) : setup.OpenSource();
                        using var records = FalloutPluginStack.Load(content.PluginSources);
                        var exit = Run(records, content, child, reuse, setup?.Dependencies);
                        if (exit != 0) ++failed;
                        WriteRow(rows, new { variant.Mask, selection = variantSelection, admission = "source-resolved",
                            content.ContentRoots, activePlugins = content.PluginSources.Select(plugin => plugin.Name).ToArray(),
                            content.Settings, dependencies = setup?.Dependencies, content.SaveCompatibilityId,
                            auditDirectory = child, corpusExitCode = exit, corpus = exit == 0 ? "source-reads-accounted" : "failed",
                            decoding = "uninspected", runtimeReady = false });
                    }
                }
                catch (Exception error) when (SourceFailure(error))
                {
                    ++failed;
                    WriteRow(rows, new { variant.Mask, selection = variantSelection, admission = "failed", setup,
                        errorType = error.GetType().FullName, error = error.ToString(), corpus = "failed-or-uninspected", runtimeReady = false });
                }
                ++visited; rows.Flush(); Summary();
            }
            complete = true;
        }
        finally { Summary(); }
        Console.WriteLine(JsonSerializer.Serialize(new { directory, plannedVariants = planned,
            visitedVariants = visited.ToString(CultureInfo.InvariantCulture), failedVariants = failed.ToString(CultureInfo.InvariantCulture),
            domainEnumerationComplete = complete, runtimeReady = false }));
        return complete && failed.IsZero ? 0 : 1;
    }
}
