using System.Text;
using OpenNV.Runtime.Content;

internal static class ModContentContracts
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opennv-mod-layers-{Guid.NewGuid():N}");
        var game = Path.Combine(root, "Game");
        var data = Path.Combine(game, "Data");
        var mod = Path.Combine(root, "Mod");
        var patch = Path.Combine(root, "Patch");
        foreach (var directory in new[] { data, mod, patch }) Directory.CreateDirectory(directory);
        try
        {
            ModInstallationContracts.WritePlugin(data, "FalloutNV.esm");
            ModInstallationContracts.WritePlugin(data, "Dlc.esm", "FalloutNV.esm");
            File.WriteAllText(Path.Combine(data, "Dlc.nam"), "Owned DLC");
            ModInstallationContracts.WritePlugin(data, "Inactive.esp");
            ModInstallationContracts.WritePlugin(mod, "Dependency.esm", "FalloutNV.esm");
            ModInstallationContracts.WritePlugin(mod, "Selected.esp", "Dependency.esm");
            WriteArchive(Path.Combine(data, "Base.bsa"), "base");
            WriteArchive(Path.Combine(mod, "Base.bsa"), "replacement base archive");
            WriteArchive(Path.Combine(mod, "Selected-Main.bsa"), "selected mod");
            WriteArchive(Path.Combine(mod, "Inactive.bsa"), "inactive archive must stay out");
            var ini = Path.Combine(game, "Archives.ini");
            File.WriteAllText(ini, "[Archive]\nsArchiveList=Base.bsa\n");
            using (var source = Open([mod]))
            {
                Require(source.PluginSources.Select(value => value.Name).SequenceEqual(
                    ["FalloutNV.esm", "Dlc.esm", "Dependency.esm", "Selected.esp"]), "Selected masters or NAM DLC order changed.");
                Require(source.ArchivePaths.SequenceEqual([Path.Combine(mod, "Base.bsa"), Path.Combine(mod, "Selected-Main.bsa")]),
                    "Archive folder precedence, compact suffix, or inactive archive admission differs.");
                Read(source, "selected mod", "Selected-Main.bsa");
                source.ArchiveWarmup.GetAwaiter().GetResult();
                Read(source, "selected mod", "Selected-Main.bsa");
                Require(source.ResourcePathsUnder("TeXtUrEs").SequenceEqual(["textures\\shared.dds"]), "Archive inventory changed logical identity.");
            }
            var textureDirectory = Path.Combine(patch, "Textures");
            Directory.CreateDirectory(textureDirectory);
            File.WriteAllText(Path.Combine(textureDirectory, "SHARED.DDS"), "loose patch");
            File.WriteAllText(Path.Combine(textureDirectory, "extra.dds"), "patch-only member");
            string identity;
            using (var source = Open([mod, patch]))
            {
                Read(source, "loose patch", "SHARED.DDS");
                identity = source.SaveCompatibilityId;
                Require(source.ResourcePathsUnder("textures").SequenceEqual(["textures\\extra.dds", "textures\\shared.dds"]),
                    "Loose inventory lost patch members or repeated overridden paths.");
            }
            using (var cold = Open([mod, patch]))
            {
                Read(cold, "loose patch", "SHARED.DDS");
                Require(cold.SaveCompatibilityId == identity, "Cold source reopen changed the same selected stack.");
            }
            using (var reordered = Open([patch, mod]))
                Require(reordered.SaveCompatibilityId != identity, "Changing selected folder priority retained a compatible save identity.");
            var layers = new FalloutContentLayers([mod, patch, mod]);
            Require(layers.Roots.SequenceEqual([patch, mod]), "A repeated folder silently retained its earlier priority.");
            Reject(() => layers.ResolveFile("../outside"));
            Reject(() => RuntimeLiveContentSource.ResolveSelectedPluginOrder(layers, NativeGame.FalloutNewVegas, ["../Selected.esp"]));
            Reject(() => RuntimeLiveContentSource.ResolveSelectedPluginOrder(new FalloutContentLayers([data, mod]),
                NativeGame.FalloutNewVegas, ["Selected.esp", "SELECTED.ESP"]));
            File.Delete(Path.Combine(mod, "Dependency.esm"));
            Reject(() => Open([mod]));
            Console.WriteLine("OPENNV_MOD_CONTENT_CONTRACT_PASS folders=ordered archives=effective loose=effective masters=closed coldSelection=stable");

            RuntimeLiveContentSource Open(string[] additional) => RuntimeLiveContentSource.Open(game,
                RuntimeLiveContentSource.FalloutNewVegasGame, additional, ["Selected.esp"], ini);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Read(RuntimeLiveContentSource source, string expected, string origin)
    {
        Require(source.TryRead("TEXTURES/shared.dds", "Base.bsa", out var bytes, out var resolved) &&
            Encoding.UTF8.GetString(bytes) == expected && resolved.Contains(origin, StringComparison.Ordinal),
            "An archive hint or base folder bypassed the selected winning resource.");
    }

    internal static void WriteArchive(string path, string value)
    {
        var folder = Encoding.ASCII.GetBytes("textures\0");
        var name = Encoding.ASCII.GetBytes("shared.dds\0");
        var payload = Encoding.UTF8.GetBytes(value);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x00415342u); writer.Write(104u); writer.Write(36u); writer.Write(3u);
        writer.Write(1u); writer.Write(1u); writer.Write((uint)folder.Length); writer.Write((uint)name.Length); writer.Write(2u);
        writer.Write(0ul); writer.Write(1u); writer.Write((uint)(52 + name.Length));
        writer.Write((byte)folder.Length); writer.Write(folder);
        writer.Write(0ul); writer.Write((uint)payload.Length); writer.Write((uint)(52 + 1 + folder.Length + 16 + name.Length));
        writer.Write(name); writer.Write(payload);
    }

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException) { return; }
        throw new InvalidOperationException("Invalid mod content was accepted.");
    }
}
