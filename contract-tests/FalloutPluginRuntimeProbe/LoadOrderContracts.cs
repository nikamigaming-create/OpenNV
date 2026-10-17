using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class LoadOrderContracts
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opennv-active-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            void Plugin(string name, bool master, int seconds, string? directory = null)
            {
                var header = new byte[24];
                "TES4"u8.CopyTo(header);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), master ? 1u : 0u);
                var path = Path.Combine(directory ?? root, name);
                File.WriteAllBytes(path, header);
                File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, seconds, DateTimeKind.Utc));
            }
            Plugin("FalloutNV.esm", true, 0);
            Plugin("Inactive.esm", true, 1);
            Plugin("LateMaster.esp", true, 4);
            Plugin("EarlyMaster.esm", true, 2);
            Plugin("A.esp", false, 3);
            Plugin("Z.esm", false, 3);
            var profile = Path.Combine(root, "plugins.txt");
            File.WriteAllLines(profile, ["# Explicit activation, deliberately unsorted", "A.esp", "falloutnv.ESM", "Z.esm", "LateMaster.esp", "EarlyMaster.esm"]);
            var plugins = RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.FalloutNewVegas, profile);
            Require(plugins.Select(Path.GetFileName).SequenceEqual(["FalloutNV.esm", "EarlyMaster.esm", "LateMaster.esp", "Z.esm", "A.esp"]),
                "Active load order ignored header flags, timestamps, or filename ties.");
            File.WriteAllText(profile, "FalloutNV.esm\n");
            Require(RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.FalloutNewVegas, profile).Count == 1,
                "An installed inactive master entered the active world.");
            foreach (var bad in new[] { "Missing.esm", "A.esp\na.ESP", "../A.esp", "*A.esp" })
            {
                File.WriteAllText(profile, bad);
                Fail(() => RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.FalloutNewVegas, profile));
            }
            Fail(() => RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.FalloutNewVegas, profile + ".missing"));
            Plugin("caf\u00e9.esp", false, 5);
            File.WriteAllBytes(profile, [0x63, 0x61, 0x66, 0xe9, 0x2e, 0x65, 0x73, 0x70]);
            Require(Path.GetFileName(RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.FalloutNewVegas, profile)[1]) == "caf\u00e9.esp",
                "The retail Windows-1252 plugin name was changed during decoding.");
            File.WriteAllBytes(profile, [0]);
            Fail(() => RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.FalloutNewVegas, profile));
            File.WriteAllText(profile, string.Empty);
            Require(RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.FalloutNewVegas, profile).Count == 1,
                "An empty active profile enabled installed plugins.");
            Plugin("Fallout3.esm", true, 0);
            Require(Path.GetFileName(RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.Fallout3, profile).Single()) == "Fallout3.esm",
                "The active Fallout 3 profile selected another game's master.");
            Plugin("Implicit.esp", false, 6);
            Plugin("Implicit.esm", true, 7);
            File.WriteAllText(Path.Combine(root, "iMPLICIT.NAM"), "Display title");
            File.WriteAllText(Path.Combine(root, "Orphan.nam"), string.Empty);
            Require(RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.FalloutNewVegas, profile).Select(Path.GetFileName)
                .SequenceEqual(["FalloutNV.esm", "Implicit.esm", "Implicit.esp"]),
                "NAM activation failed to admit matching ESM/ESP files or admitted unrelated masters.");
            Require(RuntimeLiveContentSource.ResolvePluginOrder(root, NativeGame.Fallout3, profile).Count == 1,
                "FNV NAM activation leaked into Fallout 3.");
            Plugin("DeadMoney.esm", true, 8);
            Plugin("HonestHearts.esm", true, 7);
            File.WriteAllText(profile, "Implicit.esp\nLateMaster.esp\n");
            var vanilla = RuntimeLiveContentSource.ResolveVanillaPlugins(root);
            Require(vanilla.SequenceEqual(["FalloutNV.esm", "HonestHearts.esm", "DeadMoney.esm"]),
                "Vanilla selection admitted a custom NAM/profile mod, omitted installed official content, or changed timestamp order.");
            Require(RuntimeLiveContentSource.ResolveSelectedPluginOrder(new FalloutContentLayers([root]), NativeGame.FalloutNewVegas,
                    vanilla, includeImplicitPlugins: false).Select(Path.GetFileName).SequenceEqual(vanilla),
                "Explicit vanilla selection reacquired custom NAM plugins during runtime source opening.");
            Require(RuntimeLiveContentSource.ResolveSelectedPluginOrder(new FalloutContentLayers([root]), NativeGame.FalloutNewVegas,
                    vanilla).Any(path => Path.GetFileName(path) == "Implicit.esp"),
                "Explicit vanilla selection changed the installed/modded NAM activation path.");
            var fo3Root = Path.Combine(root, "fo3");
            Directory.CreateDirectory(fo3Root);
            Plugin("Fallout3.esm", true, 0, fo3Root);
            Plugin("Zeta.esm", true, 2, fo3Root);
            Plugin("Unselected.esp", false, 3, fo3Root);
            File.WriteAllText(Path.Combine(fo3Root, "Unselected.nam"), string.Empty);
            Require(RuntimeLiveContentSource.ResolveVanillaPlugins(fo3Root).SequenceEqual(["Fallout3.esm", "Zeta.esm"]),
                "Standalone vanilla Fallout 3 selected an unrelated plugin or another game's master.");

            foreach (var name in new[] { "Base.bsa", "EarlyMaster.bsa", "EarlyMaster - Main.bsa", "Inactive - Main.bsa", "Unconfigured.bsa", "Update.bsa" })
                File.WriteAllBytes(Path.Combine(root, name), []);
            var ini = Path.Combine(root, "Fallout.ini");
            File.WriteAllText(ini, "[Other]\nsArchiveList=Unconfigured.bsa\n[Archive]\nsArchiveList=Base.bsa, Base.bsa\n");
            var sources = plugins.Select(path => new FalloutPluginSource(Path.GetFileName(path), path)).ToArray();
            var archives = RuntimeLiveContentSource.ResolveArchiveOrder(root, sources, NativeGame.FalloutNewVegas, ini);
            Require(archives.Select(Path.GetFileName).SequenceEqual(["Base.bsa", "EarlyMaster - Main.bsa", "EarlyMaster.bsa", "Update.bsa"]),
                "Archive activation included an inactive/unconfigured archive or lost the base patch.");
            Require(!RuntimeLiveContentSource.ResolveArchiveOrder(root, sources, NativeGame.Fallout3, ini)
                .Any(path => Path.GetFileName(path) == "Update.bsa"), "FNV patch policy leaked into Fallout 3.");
            File.WriteAllText(ini, "[Archive]\nsArchiveList=Missing.bsa\n");
            Fail(() => RuntimeLiveContentSource.ResolveArchiveOrder(root, sources, NativeGame.FalloutNewVegas, ini));
            Console.WriteLine("OPENNV_ACTIVE_SOURCE_CONTRACT_PASS activation=plugins-and-nam vanilla=isolated-official order=header-timestamp archives=selected-only");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static void Owned(string root)
    {
        using var source = RuntimeLiveContentSource.Open(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var vanilla = RuntimeLiveContentSource.Open(root, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(root), includeImplicitPlugins: false);
        source.ArchiveWarmup.GetAwaiter().GetResult();
        vanilla.ArchiveWarmup.GetAwaiter().GetResult();
        using var records = FalloutPluginStack.Load(source.PluginSources);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-active-source-audit/v1",
            source.SaveCompatibilityId,
            plugins = records.Plugins.Select(plugin => new { name = plugin.Plugin.Name, plugin.Sha256 }),
            archives = source.ArchivePaths.Select(Path.GetFileName),
            vanilla = new { vanilla.SaveCompatibilityId, plugins = vanilla.PluginSources.Select(plugin => plugin.Name),
                archives = vanilla.ArchivePaths.Select(Path.GetFileName) },
            parity = "unverified",
        }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Fail(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid source selection was accepted.");
    }
}
