using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceArchiveRegistryContracts
{
    internal static void RunLayeredNamespace()
    {
        var fixture = Directory.CreateTempSubdirectory("opennv-sound-namespace-");
        var readers = new List<FalloutBsaArchive>();
        try
        {
            var installation = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Game")).FullName;
            var data = Directory.CreateDirectory(Path.Combine(installation, "Data")).FullName;
            var lower = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Lower")).FullName;
            var upper = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Upper")).FullName;
            var paths = new[] { Path.Combine(data, "Authored.bsa"), Path.Combine(lower, "Authored.bsa"),
                Path.Combine(upper, "Authored.bsa"), Path.Combine(installation, "Direct.bsa"), Path.Combine(upper, "Direct.bsa") };
            for (var at = 0; at < paths.Length; at++) Write(paths[at], checked((byte)(11 + 10 * at)));
            var originals = paths.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToArray();
            var auxiliary = Path.Combine(installation, "AuthoredPlugins.txt");
            File.WriteAllText(auxiliary, "Authored.esp\n");
            File.WriteAllText(Path.Combine(upper, "AuthoredPlugins.txt"), "Ignored.esp\n");
            File.WriteAllText(Path.Combine(upper, "Invalidation.txt"), "layered-only");
            var layers = new FalloutContentLayers([data, lower, upper]);
            var search = new FalloutSoundSearchNamespace(installation, data, layers);
            search.RequireLocalMasterPath("Data\\");
            Require(search.FindStartupFile("AUTHORED.BSA") == paths[2] && search.FindStartupFile("Direct.bsa") == paths[3],
                "Startup lost direct-before-prefix search or selected DATA winner.");
            Require(search.FindAuxiliaryFile("AuthoredPlugins.txt") == auxiliary &&
                search.FindStartupFile("Invalidation.txt") == Path.Combine(upper, "Invalidation.txt"),
                "Auxiliary lookup merged DATA content or invalidation ignored its selected prefix.");
            var directInvalidation = Path.Combine(installation, "Invalidation.txt");
            File.WriteAllText(directInvalidation, "direct");
            var freshSearch = new FalloutSoundSearchNamespace(installation, data, new([data, lower, upper]));
            Require(freshSearch.FindStartupFile("Invalidation.txt") == directInvalidation &&
                File.ReadAllText(directInvalidation) == "direct", "Source invalidation content was merged or reordered.");
            var upperSounds = Directory.CreateDirectory(Path.Combine(upper, "sound", "a")).FullName;
            var dataSounds = Directory.CreateDirectory(Path.Combine(data, "sound", "a")).FullName;
            File.WriteAllBytes(Path.Combine(dataSounds, "voice.wav"), [1]);
            var voice = Path.Combine(upperSounds, "voice.wav"); File.WriteAllBytes(voice, [2]);
            var currentSearch = new FalloutSoundSearchNamespace(installation, data, new([data, lower, upper]));
            Require(currentSearch.FindPhysicalFile("DATA/sound/a/voice.wav") == voice &&
                currentSearch.FindPhysicalFile("sound\\a\\voice.wav") == voice,
                "Physical fallback bypassed the winning loose namespace.");
            Reject<NotSupportedException>(() => currentSearch.RequirePhysicalDirectoryRoot());
            Reject<NotSupportedException>(() => currentSearch.RequireLocalMasterPath("Other\\"));
            Reject<NotSupportedException>(() => currentSearch.FindStartupFile("..\\Authored.bsa"));
            Reject<NotSupportedException>(() => currentSearch.FindPhysicalFile("data\\..\\voice.wav"));
            Reject<NotSupportedException>(() => new FalloutSoundSearchNamespace(installation, upper, layers));
            Directory.CreateDirectory(Path.Combine(upper, "Directory.bsa"));
            File.WriteAllBytes(Path.Combine(data, "Directory.bsa"), [1]);
            var nonFile = new FalloutSoundSearchNamespace(installation, data, new([data, upper]));
            Reject<InvalidDataException>(() => nonFile.FindStartupFile("Directory.bsa"));
            var vanishedRoot = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Vanished")).FullName;
            var vanished = new FalloutSoundSearchNamespace(installation, data, new([data, vanishedRoot]));
            Directory.Delete(vanishedRoot);
            Reject<DirectoryNotFoundException>(() => vanished.FindStartupFile("Authored.bsa"));
            var single = new FalloutSoundSearchNamespace(installation, data, new([data]));
            Require(single.RequirePhysicalDirectoryRoot() == data && single.FindDataFile("Authored.bsa") == paths[0],
                "Single-root native directory producer or original lookup changed.");

            var opened = new Dictionary<string, FalloutBsaArchive>(StringComparer.OrdinalIgnoreCase);
            FalloutBsaArchive Read(string name)
            {
                var path = search.FindStartupFile(name) ?? throw new FileNotFoundException("Selected startup archive missing.", name);
                if (!opened.TryGetValue(path, out var reader))
                { reader = new(path); opened.Add(path, reader); readers.Add(reader); }
                return reader;
            }
            var input = new FalloutArchiveStartupInput(new('a', 64), new(new('b', 64), "AuthoredPlugins.txt", false, 0, null),
                ["Authored.bsa", "Direct.bsa"]);
            var source = Source(FalloutArchiveRegistrationOrder.Append);
            var registry = new FalloutSourceArchiveRegistry(source, input, Read);
            var winner = registry.Resolve("sound\\a\\a.wav") ?? throw new InvalidDataException("Selected registry member missing.");
            Require(winner.Archive.Archive == paths[2] && Read("Authored.bsa").Read(winner.Member.LogicalPath).SequenceEqual(new byte[] { 32 }) &&
                registry.ReadDirectory("sound\\a", ".wav").Count == 4,
                "Layered startup changed registry-first bytes or duplicate directory weighting.");
            var saved = registry.Capture();
            var cold = new FalloutSourceArchiveRegistry(source, input, Read, saved);
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved), "Cold lookup replayed source registration.");
            var changed = new FalloutSoundSearchNamespace(installation, data, new([data, upper, lower]));
            FalloutBsaArchive Changed(string name)
            {
                var reader = new FalloutBsaArchive(changed.FindStartupFile(name) ?? throw new FileNotFoundException(name));
                readers.Add(reader); return reader;
            }
            Reject<InvalidDataException>(() => new FalloutSourceArchiveRegistry(source, input, Changed, saved));
            var missing = new FalloutSourceArchiveRegistry(source, input with { Names = ["Authored.bsa", "Missing.bsa"] }, Read);
            Require(missing.Capture().Registrations[1].Phase == FalloutArchiveRegistrationPhase.Failed,
                "Missing selected source became an empty registration.");
            Reject<FileNotFoundException>(() => missing.Resolve("sound\\a\\a.wav"));
            registry.Retire(); cold.Retire(); missing.Retire();
            for (var at = 0; at < paths.Length; at++)
                Require(SHA256.HashData(File.ReadAllBytes(paths[at])).SequenceEqual(originals[at]), "Source namespace wrote an original archive.");
            Console.WriteLine("OPENNV_SOUND_SEARCH_NAMESPACE_PASS layeredData=true directPrefixOrder=true auxiliaryUnmerged=true " +
                "physicalWinner=true actualRegistryBytes=true missingRetained=true coldDriftRefused=true sourceUnchanged=true " +
                "layeredLooseOrder=UNOWNED gameplay=UNEXECUTED");
        }
        finally
        {
            foreach (var reader in readers) reader.Dispose();
            Directory.Delete(fixture.FullName, recursive: true);
        }
    }
}
