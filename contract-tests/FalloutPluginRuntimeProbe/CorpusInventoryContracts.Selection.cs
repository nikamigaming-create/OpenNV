using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CorpusInventoryContracts
{
    private static void SelectionAdmission(string directory)
    {
        var game = Game(directory, "selection-game");
        var jam = Path.Combine(directory, "selection-jam");
        var ttw = Path.Combine(directory, "selection-ttw");
        var eve = Path.Combine(directory, "selection-eve");
        var yup = Path.Combine(directory, "selection-yup");
        var dependencies = Path.Combine(directory, "selection-dependencies");
        foreach (var root in new[] { jam, ttw, eve, yup, dependencies }) Directory.CreateDirectory(root);
        ModInstallationContracts.WritePlugin(jam, "JustAssortedMods.esp", "FalloutNV.esm", "TaleOfTwoWastelands.esm");
        ModInstallationContracts.WritePlugin(ttw, "Fallout3.esm", "FalloutNV.esm");
        ModInstallationContracts.WritePlugin(ttw, "TaleOfTwoWastelands.esm", "FalloutNV.esm", "Fallout3.esm");
        ModInstallationContracts.WritePlugin(ttw, "YUPTTW.esm", "TaleOfTwoWastelands.esm");
        File.WriteAllText(Path.Combine(ttw, "YUPTTW.nam"), "authored activation sidecar");
        ModInstallationContracts.WritePlugin(eve, "EVE FNV - ALL DLC.esp", "FalloutNV.esm");
        ModInstallationContracts.WritePlugin(yup, "YUP - Base Game + All DLC.esm", "FalloutNV.esm");
        foreach (var logical in new[] { "jam", "ttw" }.SelectMany(FalloutModInstallation.ExtensionFiles).Select(row => row.Path)
            .Append("nvse_1_4.dll").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(dependencies, logical.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "synthetic presence fixture; never loaded as a library");
        }
        var selection = new FalloutModStackSelection([new("jam", jam, [dependencies]), new("ttw", ttw, [dependencies]),
            new("eve", eve, []), new("yup", yup, [])], AutomaticOrder: false);
        var variants = CorpusInventory.SelectionVariants(selection).ToArray();
        Require(variants.Length == 16 && variants.Select(row => row.Mask).Distinct().Count() == 16 &&
            variants.All(row => !row.AutomaticOrder), "Configured on/off axes were capped, duplicated or given another order mode.");
        var originalHashes = FileHashes(game, jam, ttw, eve, yup, dependencies);
        var jamOnly = variants.Single(row => row.Mods.Select(mod => mod.Id).SequenceEqual(["jam"]));
        var missing = CorpusInventory.InspectVariant(game, jamOnly);
        Require(!missing.SourceAdmitted && missing.Setup!.MissingDependencies.Any(row => row.LogicalPath == "TaleOfTwoWastelands.esm"),
            "An absent actual MAST owner was fabricated for a selection subset.");
        var ttwOnly = variants.Single(row => row.Mods.Select(mod => mod.Id).SequenceEqual(["ttw"]));
        var source = CorpusInventory.InspectVariant(game, ttwOnly);
        var sourceSetup = source.Setup ?? throw new InvalidOperationException("The actual TTW selection lost its setup owner.");
        Require(source.SourceAdmitted && sourceSetup.ActivePlugins.Contains("YUPTTW.esm", StringComparer.OrdinalIgnoreCase) &&
            sourceSetup.Settings.Any(setting => setting.Key == "SCharGenQuest") &&
            sourceSetup.Dependencies.Any(row => row.LogicalPath == "Fallout3.esm" && row.SourcePath is not null),
            "Actual NAM/master/settings ownership was omitted from a TTW selection.");
        var combined = variants.Single(row => row.Mods.Select(mod => mod.Id).SequenceEqual(["jam", "ttw"]));
        var joint = CorpusInventory.InspectVariant(game, combined);
        var jointSetup = joint.Setup ?? throw new InvalidOperationException("The actual combined selection lost its setup owner.");
        Require(joint.SourceAdmitted && jointSetup.ContentRoots.Contains(dependencies) &&
            Array.IndexOf(jointSetup.ActivePlugins.ToArray(), "TaleOfTwoWastelands.esm") <
                Array.IndexOf(jointSetup.ActivePlugins.ToArray(), "JustAssortedMods.esp"),
            "Declared source masters or retained dependency roots changed across selection admission.");
        foreach (var ids in new[] { new[] { "ttw", "eve" }, new[] { "ttw", "yup" } })
        {
            var conflicting = variants.Single(row => row.Mods.Select(mod => mod.Id).SequenceEqual(ids));
            var conflict = CorpusInventory.InspectVariant(game, conflicting);
            Require(!conflict.SourceAdmitted && conflict.CompatibilityIssues.Count != 0,
                "Existing launcher compatibility refusal was turned into combination acceptance.");
        }
        Require(originalHashes.SequenceEqual(FileHashes(game, jam, ttw, eve, yup, dependencies)),
            "Selection/dependency admission mutated authored source files.");
        Reject(() => CorpusInventory.SelectionVariants(new([selection.Mods[0], selection.Mods[0]])).ToArray());
        Reject(() => CorpusInventory.SelectionVariants(new([new("unregistered", jam, [])])).ToArray());
        Require(CorpusInventory.SelectionVariants(null).Single().Mods.Count == 0,
            "A standalone source invented configured catalog axes.");
    }

    private static void CommandRefusals(string directory)
    {
        var stackFile = Path.Combine(directory, "command-stack.json");
        var chosen = new FalloutModSelection("jam", Path.Combine(directory, "not-present-jam"), [Path.Combine(directory, "dep")]);
        File.WriteAllText(stackFile, JsonSerializer.Serialize(new[] { chosen }));
        var command = CorpusInventory.ParseCommand(["owned-root", "fresh-output", "--selection-variants", "--mod-stack", stackFile,
            "--mod-order", "manual"]);
        Require(command.SelectionVariants && command.Source.Arguments.SequenceEqual(["corpus", "owned-root", "fresh-output"]) &&
            command.Source.Selection is { AutomaticOrder: false } && command.Source.Selection.Mods.Single().AdditionalRoots.Single() == chosen.AdditionalRoots.Single(),
            "Corpus options changed exact launcher JSON roots or order.");
        var legacy = CorpusInventory.ParseCommand(["owned-root", "fresh-output", "--selection-variants", "--mod", "jam", chosen.Root,
            chosen.AdditionalRoots.Single()]);
        Require(legacy.SelectionVariants && legacy.Source.Selection!.Mods.Single().Root == chosen.Root,
            "Corpus variant parsing changed the existing single-mod selection.");
        Reject(() => CorpusInventory.ParseCommand(["owned-root", "fresh-output", "--mod", "jam", chosen.Root, "--selection-variants"]));
        Reject(() => CorpusInventory.ParseCommand(["owned-root", "fresh-output", "--selection-variants", "--selection-variants"]));
        Reject(() => CorpusInventory.ParseCommand(["owned-root", "fresh-output", "--uninspected-success"]));
        Reject(() => CorpusInventory.ParseCommand(["owned-root", "fresh-output", "--mod-stack", stackFile, "--mod-order", "guess"]));
        var output = Path.Combine(directory, "missing-source-report");
        Require(CorpusInventory.RunCommand([Path.Combine(directory, "missing-game"), output]) == 1,
            "Actual source admission failure returned success.");
        using var refusal = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "source-admission-refusal.json")));
        Require(refusal.RootElement.GetProperty("winningRecordDenominator").GetString() == "unknown" &&
            !refusal.RootElement.GetProperty("runtimeReady").GetBoolean(), "Missing source invented a winning denominator.");
        var preserved = Hash(File.ReadAllBytes(Path.Combine(output, "source-admission-refusal.json")));
        Require(CorpusInventory.RunCommand([Path.Combine(directory, "missing-game"), output]) == 1 &&
            preserved == Hash(File.ReadAllBytes(Path.Combine(output, "source-admission-refusal.json"))),
            "A repeated failed command rewrote its retained refusal.");
    }
}
