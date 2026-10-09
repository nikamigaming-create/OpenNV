using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class SourceArchiveRegistryContracts
{
    internal static void Run()
    {
        var root = Directory.CreateTempSubdirectory("opennv-authored-source-registry-");
        try
        {
            var firstPath = Path.Combine(root.FullName, "First.bsa"); var secondPath = Path.Combine(root.FullName, "Second.bsa");
            Write(firstPath, 11); Write(secondPath, 22);
            var hashes = new[] { SHA256.HashData(File.ReadAllBytes(firstPath)), SHA256.HashData(File.ReadAllBytes(secondPath)) };
            using var first = new FalloutBsaArchive(firstPath); using var second = new FalloutBsaArchive(secondPath);
            var input = new FalloutArchiveStartupInput(new('a', 64), new(new('b', 64), "AuthoredPlugins.txt", false, 0, null),
                ["First.bsa", "Second.bsa"]);
            FalloutBsaArchive Read(string name) => name switch { "First.bsa" => first, "Second.bsa" => second,
                _ => throw new FileNotFoundException("authored actual registration missing", name) };
            var append = Source(FalloutArchiveRegistrationOrder.Append);
            var registry = new FalloutSourceArchiveRegistry(append, input, Read);
            var paths = registry.ReadDirectory("sound\\a", ".wav");
            Require(paths.SequenceEqual(["sound\\a\\a.wav", "sound\\a\\z.wav", "sound\\a\\a.wav", "sound\\a\\z.wav"]),
                "Registry traversal lost original per-table prepend or duplicate weighting.");
            var winner = registry.Resolve("sound\\a\\a.wav") ?? throw new InvalidDataException("Authored first registry member missing.");
            Require(winner.RegistrationOrdinal == 1 && winner.Archive.Archive == firstPath &&
                first.Read(winner.Member.LogicalPath).SequenceEqual(new byte[] { 12 }),
                "Directory's last contributor overrode the independent registry-first byte winner.");
            var saved = registry.Capture(); var cold = new FalloutSourceArchiveRegistry(append, input, Read, saved);
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved) && cold.ReadDirectory("sound\\a", ".wav").SequenceEqual(paths),
                "Cold verification invented a caller or changed actual original table/registry order.");
            SharedSelection(root.FullName, first, second, append, input);
            Reject<InvalidDataException>(() => new FalloutSourceArchiveRegistry(append, input with { Names = ["Second.bsa", "First.bsa"] }, Read, saved));
            Reject<InvalidDataException>(() => new FalloutSourceArchiveRegistry(append, input with
            { Auxiliary = input.Auxiliary with { Present = true, Bytes = 1, Sha256 = new('e', 64) } }, Read, saved));
            var duplicate = new FalloutSourceArchiveRegistry(append, input with { Names = ["First.bsa", "First.bsa"] }, Read);
            Require(duplicate.Capture().Registrations.Count == 2 && duplicate.ReadDirectory("sound\\a", ".wav").Count == 4,
                "Repeated original registrations were deduplicated by retained reader identity.");
            var failedInput = input with { Names = ["First.bsa", "Missing.bsa", "Second.bsa"] };
            var failed = new FalloutSourceArchiveRegistry(append, failedInput, Read);
            Require(failed.Capture().RegistryOrdinals.SequenceEqual(new long[] { 1, 3 }) &&
                failed.Capture().Registrations[1].Phase == FalloutArchiveRegistrationPhase.Failed,
                "Missing source registration erased prior rows or fabricated successful/empty input.");
            Reject<FileNotFoundException>(() => failed.ReadDirectory("sound\\a", ".wav"));
            var failedCold = new FalloutSourceArchiveRegistry(append, failedInput, Read, failed.Capture());
            Reject<FileNotFoundException>(() => failedCold.Resolve("sound\\a\\a.wav"));
            var inserted = new FalloutSourceArchiveRegistry(Source(FalloutArchiveRegistrationOrder.SourceSubstringInsertion), input, Read);
            Require(inserted.Capture().RegistryOrdinals.SequenceEqual(new long[] { 2, 1 }) &&
                inserted.Resolve("sound\\a\\a.wav")?.Archive.Archive == secondPath,
                "Unclassified source insertion was sorted or confused with tail append.");
            var categories = Source(FalloutArchiveRegistrationOrder.SourceSubstringInsertion);
            Require(categories.InsertionIndex("Base-Later.bsa", ["User.bsa"]) == 1 &&
                categories.InsertionIndex("LayerThree.bsa", ["User.bsa", "LayerOne.bsa", "Base.bsa"]) == 1 &&
                categories.Category("base.bsa") == 6 &&
                categories.InsertionIndex("LayerTwo.bsa", ["LayerTwo-LayerOne.bsa", "Base.bsa"]) == 0,
                "Source case-sensitive/overlapping substring tests lost their original independent thresholds.");
            Require(FalloutArchiveStartupInput.ReadConfiguredNames(" First.bsa,,\tSecond.bsa,First.bsa")
                .SequenceEqual(["First.bsa", "Second.bsa", "First.bsa"]), "Configured token order/repeated registration changed.");
            Require(FalloutArchiveStartupInput.ReadAuxiliaryArchiveNames(Encoding.ASCII.GetBytes(
                "# ignored.esm\r\nPlugin.esp.extra\r\nOther.esm\nUPPER.ESP\nPlugin.esp\n\u001aLater.esm\n"))
                .SequenceEqual(["Plugin.bsa", "Other.bsa", "Plugin.bsa"]), "Actual text-mode suffix/filter/duplicate input grammar changed.");
            registry.Retire(); Reject<ObjectDisposedException>(() => registry.Resolve("sound\\a\\a.wav"));
            Require(first.Read("sound\\a\\z.wav").SequenceEqual(new byte[] { 11 }) &&
                SHA256.HashData(File.ReadAllBytes(firstPath)).SequenceEqual(hashes[0]) &&
                SHA256.HashData(File.ReadAllBytes(secondPath)).SequenceEqual(hashes[1]), "Borrowed retirement closed or modified source archives.");
            Console.WriteLine("OPENNV_SOURCE_ARCHIVE_REGISTRY_PASS registrationOrder=true duplicateWeights=true " +
                "registryFirstBytes=true actualBsaReader=true retainedMissingPrefix=true coldNoCallerReplay=true sourceUnchanged=true nativeAudio=unexecuted");
        }
        finally { Directory.Delete(root.FullName, recursive: true); }
    }

    private static FalloutArchiveRegistrySource Source(FalloutArchiveRegistrationOrder order) => new(
        order == FalloutArchiveRegistrationOrder.Append
            ? "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"
            : "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
        new('c', 64), order, order == FalloutArchiveRegistrationOrder.Append ? [] :
            ["Base", "LayerOne", "LayerTwo", "LayerThree", "LayerFour", "LayerFive"],
        new('d', 64), FalloutArchiveRegistrySource.CurrentContractSha256);

    private static void SharedSelection(string root, FalloutBsaArchive first, FalloutBsaArchive second,
        FalloutArchiveRegistrySource source, FalloutArchiveStartupInput input)
    {
        var name = "Authored-Registry.esm"; var path = Path.Combine(root, name);
        var payload = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), (uint)FalloutSoundFlags.MenuSound);
        for (var index = 0; index < 5; index++) BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(12 + index * 2), 100);
        File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8]))),
            Record("SOUN", 0x810, Field("EDID", Encoding.ASCII.GetBytes("AuthoredRegistrySound\0")),
                Field("FNAM", Encoding.ASCII.GetBytes("data\\sound\\a\\\0")), Field("SNDD", payload))));
        using var records = FalloutPluginStack.Load(root, [name]);
        var sound = FalloutSoundRecordReader.Read(records, new(name, 0x810));
        var selectionSource = FalloutMenuSoundSelectionSource.Read(source.EngineSha256, source.RuntimeSha256);
        var modes = new FalloutArchiveStartupModes(1, 1, "authored-source", "authored-source",
            new("sArchiveList:Archive", "First.bsa,Second.bsa", "authored-source"),
            new("sInvalidationFile:Archive", "AuthoredInvalidation.txt", "authored-source"),
            new("sLocalMasterPath:General", "Data\\", "authored-source"), new('f', 64));
        var actualInput = input with { ModesSha256 = modes.Identity };
        FalloutBsaArchive Read(string filename) => filename == "First.bsa" ? first : second;
        var registry = new FalloutSourceArchiveRegistry(source, actualInput, Read);
        var files = new FalloutArchiveFileManager(FalloutArchiveFileManagerSource.Read(selectionSource), modes, [first, second],
            new(new('e', 64), false, 0, null), (_, _) => throw new IOException("Registered directory touched loose enumeration."),
            _ => throw new IOException("Registered directory consulted legacy byte-winner order."), registry: registry);
        var owner = new FalloutMenuSoundSelection(records, selectionSource, files.ReadDirectory, authoredTickProducer: () => 91,
            requireSourceDirectory: files.RequireColdDirectory, captureFileManager: files.Capture);
        _ = owner.Prepare(sound, new("authored-source-registry-caller", 1, 0, sound.FormKey));
        var saved = owner.Capture();
        Require(saved.Attempts.Single().Directories.Single().Paths.Count == 4 && saved.Random.Draws == 1 &&
            saved.FileManager?.Registry?.RegistryOrdinals.SequenceEqual(new long[] { 1, 2 }) == true,
            "Shared selector refused genuine duplicates, changed their draw bound, or lost actual registry identity.");
        var coldRegistry = new FalloutSourceArchiveRegistry(source, actualInput, Read, saved.FileManager!.Registry);
        var coldFiles = new FalloutArchiveFileManager(FalloutArchiveFileManagerSource.Read(selectionSource), modes, [first, second],
            new(new('e', 64), false, 0, null), (_, _) => throw new IOException("Cold source replayed loose enumeration."),
            _ => throw new IOException("Cold source consulted legacy winner."), saved.FileManager, coldRegistry);
        var cold = new FalloutMenuSoundSelection(records, selectionSource, coldFiles.ReadDirectory, saved,
            () => throw new IOException("Cold source reseeded RNG."), coldFiles.RequireColdDirectory, coldFiles.Capture);
        Require(cold.Capture().Random.Draws == 1 && coldFiles.Capture().Attempts.Count == 1,
            "Cold shared registry replayed a caller or RNG draw.");

        static byte[] Field(string signature, byte[] bytes) => Join(Encoding.ASCII.GetBytes(signature), BitConverter.GetBytes(checked((ushort)bytes.Length)), bytes);
        static byte[] Record(string signature, uint id, params byte[][] fields)
        {
            var bytes = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id);
            return Join(header, bytes);
        }
        static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
    }

    private static void Write(string path, byte payload)
    {
        var folder = Encoding.ASCII.GetBytes("sound\\a\0"); var names = Encoding.ASCII.GetBytes("z.wav\0a.wav\0");
        var end = 52 + 1 + folder.Length + 32; var data = new byte[end + names.Length + 2];
        U32(0, 0x00415342); U32(4, 104); U32(8, 36); U32(12, 3); U32(16, 1); U32(20, 2);
        U32(24, (uint)folder.Length); U32(28, (uint)names.Length); U32(32, 8);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(36), 0x667fb43c73075c61); U32(44, 2); U32(48, (uint)(52 + names.Length));
        data[52] = (byte)folder.Length; folder.CopyTo(data, 53); var at = 53 + folder.Length;
        foreach (var hash in new ulong[] { 0x9733cf9efa01007a, 0x9733cf9ee1010061 })
        { BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at), hash); U32(at + 8, 1); U32(at + 12, (uint)(end + names.Length + (at - 53 - folder.Length) / 16)); at += 16; }
        names.CopyTo(data, end); data[end + names.Length] = payload; data[end + names.Length + 1] = checked((byte)(payload + 1));
        File.WriteAllBytes(path, data);
        void U32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException("Authored registry boundary was accepted: " + typeof(T).Name);
    }
}
