using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class CellGraphAuditContracts
{
    private static void SourceOptions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-audit-source-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var game = Folder("Game/Data");
            var jam = Folder("JAM");
            var ttw = Folder("TTW");
            var nmc = Folder("NMC");
            var dependencies = Folder("Shared dependencies");
            ModInstallationContracts.WritePlugin(game, "FalloutNV.esm");
            ModInstallationContracts.WritePlugin(ttw, "Fallout3.esm", "FalloutNV.esm");
            ModInstallationContracts.WritePlugin(ttw, "TaleOfTwoWastelands.esm", "Fallout3.esm");
            ModInstallationContracts.WritePlugin(jam, "JustAssortedMods.esp", "TaleOfTwoWastelands.esm");
            foreach (var (name, path) in FalloutModInstallation.ExtensionFiles("jam").Concat(FalloutModInstallation.ExtensionFiles("ttw")))
            {
                var file = Path.Combine(dependencies, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, name + " synthetic file-presence fixture");
            }
            File.WriteAllText(Path.Combine(dependencies, "nvse_1_4.dll"), "synthetic presence only");
            foreach (var root in new[] { jam, nmc })
            {
                Directory.CreateDirectory(Path.Combine(root, "textures"));
                File.WriteAllText(Path.Combine(root, "textures", "shared.dds"), root, Encoding.UTF8);
            }
            var selected = new FalloutModSelection[] { new("nmc", nmc, []), new("jam", jam, [dependencies]), new("ttw", ttw, [dependencies]) };
            var list = Path.Combine(directory, "launcher list.json");
            File.WriteAllText(list, JsonSerializer.Serialize(selected));
            foreach (var automatic in new[] { true, false })
            {
                var mode = automatic ? "automatic" : "manual";
                var command = DevelopmentLabSource.ParseCommand(["quest-graph", game, "fresh-output", "--mod-stack", list, "--mod-order", mode]);
                var cell = CellGraphAudit.ParseOptions([game, "fresh-cell-output", "--mod-order", mode,
                    "--seed", "FalloutNV.esm:800", "--mod-stack", list, "--runtime-config", "runtime.json"]);
                var launcherOptions = new Dictionary<string, string>(StringComparer.Ordinal);
                new FalloutModStackSelection(selected, automatic).WriteOptions(launcherOptions);
                var expected = FalloutModStackSelection.ReadOptions(launcherOptions)!.Resolve(game);
                Require(command.Arguments.SequenceEqual(["quest-graph", game, "fresh-output"]) &&
                    command.Selection!.AutomaticOrder == automatic && cell.SourceSelection!.AutomaticOrder == automatic,
                    "Source options changed positional arguments or lost the explicit launcher order.");
                foreach (var actual in new[] { command.Selection!.Resolve(game), cell.SourceSelection!.Resolve(game) })
                {
                    Require(actual.ContentRoots.SequenceEqual(expected.ContentRoots) && actual.ActivePlugins.SequenceEqual(expected.ActivePlugins) &&
                        actual.Settings.SequenceEqual(expected.Settings) && actual.Mods.Select(mod => mod.Id).SequenceEqual(expected.Mods.Select(mod => mod.Id)),
                        "The lab changed the launcher's selected roots, active plugins, settings or order.");
                    Require(actual.Settings.Any(setting => setting.Section == "General" && setting.Key == "SCharGenQuest" && setting.Value == "001FFFF8") &&
                        actual.Settings.Any(setting => setting.Section == "General" && setting.Key == "SIntroMovie" && setting.Value == ""),
                        "TTW's source profile settings were lost by explicit audit selection.");
                    var winner = new FalloutContentLayers(actual.ContentRoots).ResolveFile("textures/shared.dds");
                    Require(winner == Path.Combine(automatic ? nmc : jam, "textures", "shared.dds"),
                        "The selected order did not preserve the actual loose resource winner.");
                }
            }
            var standalone = DevelopmentLabSource.ParseCommand(["corpus", game, "output"]);
            Require(standalone.Selection is null && standalone.Arguments.SequenceEqual(["corpus", game, "output"]),
                "Standalone arguments invented a mod selection.");
            foreach (var audit in new[] { "corpus", "quest-graph" })
            {
                Reject(() => DevelopmentLabSource.ParseCommand([audit, game, "output", "--mod-stak", list]), "Unknown");
                Reject(() => DevelopmentLabSource.ParseCommand([audit, game, "output", "--unknown"]), "Unknown");
                Reject(() => DevelopmentLabSource.ParseCommand([audit, game, "output", "unexpected"]), "unexpected");
                Reject(() => DevelopmentLabSource.ParseCommand([audit, game, "output", "unexpected", "--mod-stack", list]), "unexpected");
            }
            var contains = DevelopmentLabSource.ParseCommand(["script", game, "--contains", "authored source fragment"]);
            Require(contains.Selection is null && contains.Arguments.SequenceEqual(["script", game, "--contains", "authored source fragment"]),
                "Strict audit source parsing removed a legitimate command operand.");
            var legacy = DevelopmentLabSource.ParseCommand(["corpus", game, "output", "--mod", "ttw", ttw, dependencies]);
            Require(legacy.Selection!.Mods.Single().Id == "ttw" && legacy.Selection!.Mods.Single().AdditionalRoots.SequenceEqual([dependencies]) &&
                legacy.Selection!.Resolve(game).Settings.Count == 2, "Legacy --mod lost its identity, dependency roots or settings.");

            Refuse(["--mod-stack"], "Missing value");
            Refuse(["--mod-stack", " "], "Missing value");
            Refuse(["--mod"], "Missing value");
            Refuse(["--mod", "ttw"], "Missing value");
            Refuse(["--mod-order"], "Missing value");
            Refuse(["--mod-order", "manual"], "requires --mod-stack");
            Refuse(["--mod-order", "manual", "--mod", "ttw", ttw], "requires --mod-stack");
            Refuse(["--mod-stack", list, "--mod-order", "unordered"], "Unknown mod load-order");
            Refuse(["--mod-stack", list, "--mod-stack", list], "Repeated");
            Refuse(["--mod-stack", list, "--mod-order", "manual", "--mod-order", "automatic"], "Repeated");
            Refuse(["--mod-stack", list, "--mod", "ttw", ttw], "combining");
            Refuse(["--mod", "ttw", ttw, "--mod-stack", list], "last");
            Refuse(["--mod", "ttw", ttw, "--mod", "ttw", ttw], "last");
            Refuse(["--mod-stack", list, "--unknown"], "Unknown");
            Refuse(["--mod-stack", list, "unexpected-positional"], "Unknown");
            Refuse(["--mod-stack", Path.Combine(directory, "missing.json")], "Could not find file");
            Reject(() => DevelopmentLabSource.ParseCommand(["corpus", game, "--mod-stack", list]), "follow");
            foreach (var (json, message) in new[] { ("null", "list"), ("[]", "at least one"),
                (JsonSerializer.Serialize(new[] { selected[0], selected[0] }), "once"),
                ("[{\"Id\":\"ttw\",\"Root\":\"some-folder\"}]", "identity, folder") })
            {
                File.WriteAllText(list, json);
                Reject(() => DevelopmentLabSource.ParseCommand(["corpus", game, "output", "--mod-stack", list]).Selection!.Resolve(game), message);
            }
            Console.WriteLine("OPENNV_AUDIT_SOURCE_OPTIONS_CONTRACT_PASS launcherSelection=true automaticOrder=true manualOrder=true sourceSettings=true looseWinner=true legacy=true malformedRefused=true");

            string Folder(string name) { var path = Path.Combine(directory, name); Directory.CreateDirectory(path); return path; }
            void Refuse(string[] options, string message)
            {
                Reject(() => DevelopmentLabSource.ParseCommand(["corpus", game, "output", .. options]), message);
                Reject(() => CellGraphAudit.ParseOptions([game, "output", "--seed", "FalloutNV.esm:800", "--runtime-config", "runtime.json", .. options]), message);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
