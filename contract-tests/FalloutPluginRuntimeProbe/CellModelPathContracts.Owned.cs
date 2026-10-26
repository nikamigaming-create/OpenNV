using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellModelPathContracts
{
    internal static void Owned(string root)
    {
        using var source = RuntimeLiveContentSource.Open(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var records = FalloutPluginStack.Load(source.PluginSources);
        var files = new Dictionary<string, (long Bytes, DateTime Modified)>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in source.PluginSources) RetainFile(plugin.AbsolutePath);
        var checkedModels = 0;
        foreach (var name in new[] { "NVbarrelcactus01", "NVbarrelcactus02", "pricklypearcactus02" })
        {
            var record = FalloutDialogueTopic.Find(records, "STAT", name);
            var sourceHash = SHA256.HashData(record.ReadData());
            var authored = FalloutDialogueTopic.Text(record.ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span);
            if (!FalloutBsaArchive.CanonicalPath(authored).StartsWith("trees\\", StringComparison.Ordinal) ||
                !authored.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Selected owned model no longer has its authored relative trees/NIF layout.");
            var path = FalloutCellSceneReader.NormalizeModelPath(authored, record.Signature);
            var expected = "meshes\\" + FalloutBsaArchive.CanonicalPath(authored);
            if (path != expected || !source.TryRead(path, null, out var bytes, out var winner))
                throw new FileNotFoundException("Owned NIF model did not resolve under its meshes root.", path);
            var separator = winner.IndexOf("::", StringComparison.Ordinal);
            if (separator <= 0 || winner[(separator + 2)..] != path)
                throw new InvalidDataException("Selected owned model is not the expected indexed BSA member.");
            var archivePath = winner[..separator];
            RetainFile(archivePath);
            using var archive = new FalloutBsaArchive(archivePath);
            if (!archive.Contains(path) || !bytes.AsSpan().SequenceEqual(archive.Read(path)))
                throw new InvalidDataException("Resolved NIF bytes differ from the exact winning BSA member.");
            var nif = FalloutNifFile.Read(bytes);
            if (nif.Roots.Count == 0 || !sourceHash.AsSpan().SequenceEqual(SHA256.HashData(record.ReadData())))
                throw new InvalidDataException("Owned model has no NIF root or its source record changed.");
            Console.WriteLine($"OPENNV_OWNED_TREE_NIF_MODEL source={record.FormKey} path={path} archive={Path.GetFileName(archivePath)} " +
                $"bytes={bytes.Length} sha256={nif.Sha256} blocks={nif.Blocks.Count} sourceUnchanged=true rendering=unverified");
            checkedModels++;
        }
        foreach (var (path, before) in files)
        {
            var file = new FileInfo(path);
            if ((file.Length, file.LastWriteTimeUtc) != before)
                throw new InvalidDataException("Owned model audit observed a changed installation input.");
        }
        Console.WriteLine($"OPENNV_OWNED_TREE_NIF_MODELS_PASS models={checkedModels} winningRecords=true exactBsaBytes=true nifHeaders=true sourceUnchanged=true nativePresentation=separate");

        void RetainFile(string path)
        {
            if (files.ContainsKey(path)) return;
            var file = new FileInfo(path);
            files.Add(path, (file.Length, file.LastWriteTimeUtc));
        }
    }
}
