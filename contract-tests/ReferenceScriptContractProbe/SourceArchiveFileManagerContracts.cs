using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class SourceArchiveFileManagerContracts
{
    private const string Engine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const ulong FolderHash = 0x667fb43c73075c61;
    private static readonly (string Name, ulong Hash)[] Members =
        [("z.wav", 0x9733cf9efa01007a), ("a.wav", 0x9733cf9ee1010061), ("x.bin", 0x8ce48bf978010078)];

    internal static void Run()
    {
        var selected = FalloutMenuSoundSelectionSource.Read(Engine, new('a', 64));
        var source = FalloutArchiveFileManagerSource.Read(selected);
        var root = Directory.CreateTempSubdirectory("opennv-authored-archive-file-manager-");
        try
        {
            var path = Path.Combine(root.FullName, "Authored-One.bsa"); var secondPath = Path.Combine(root.FullName, "Authored-Two.bsa");
            Write(path, 8); Write(secondPath, 8);
            var original = SHA256.HashData(File.ReadAllBytes(path));
            using var archive = new FalloutBsaArchive(path);
            using var second = new FalloutBsaArchive(secondPath);
            var modes = Startup(source);
            Require(modes.UseArchives == 1 && modes.InvalidateOlderFiles == 1 &&
                modes.ArchivesOrigin == "authored-executable" && modes.InvalidationOrigin == "authored-executable",
                "Independent source constructor defaults lost their genuine declarations.");
            var changed = Startup(source, use: 0);
            Require(changed.UseArchives == 0 && changed.InvalidateOlderFiles == 1 && changed.ArchivesOrigin == "authored-profile",
                "One Archive override changed or reused the other independent mode.");
            var other = FalloutArchiveFileManagerSource.Read(FalloutMenuSoundSelectionSource.Read(
                "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e", new('c', 64)));
            Require(Startup(other).UseArchives == 1 && Startup(other, invalidate: 0).UseArchives == 1 &&
                Startup(other, invalidate: 0).InvalidateOlderFiles == 0,
                "The independent selected source lost its separate Archive startup declarations.");
            Require(FalloutArchiveNameHash.Folder("sound\\a") == FolderHash &&
                Members.All(row => FalloutArchiveNameHash.File(row.Name) == row.Hash), "Source hash differs from independently authored table bytes.");
            var looseCalls = 0;
            FalloutLooseSoundDirectory NoLoose(string _, string __) { ++looseCalls; throw new IOException("Both source modes unexpectedly enumerated loose files."); }
            string? Resolve(string logical) => path + "::" + logical;
            var absent = new FalloutArchiveInvalidationInput(new('b', 64), false, 0, null);
            var owner = new FalloutArchiveFileManager(source, modes, [archive], absent, NoLoose, Resolve);
            var wav = owner.ReadDirectory("data\\sound\\a\\", ".wav");
            Require(wav.Paths.SequenceEqual(["sound\\a\\a.wav", "sound\\a\\z.wav"]) && looseCalls == 0,
                "Both-one source mode lost file-table prepend order or touched native loose enumeration.");
            var oggClass = owner.ReadDirectory("data\\sound\\a\\", ".ogg");
            Require(oggClass.Paths.SequenceEqual(["sound\\a\\x.bin"]), "Leading-star source hash was replaced by an extension text filter.");
            Require(archive.Read(wav.Paths[0]).SequenceEqual(new byte[] { 2 }) && archive.Read(wav.Paths[1]).SequenceEqual(new byte[] { 1 }),
                "Actual full archive reader selected another member payload.");
            var capture = owner.Capture();
            var cold = new FalloutArchiveFileManager(source, modes, [archive], absent, NoLoose, Resolve, capture);
            Require(cold.Capture().Attempts.Count == 2 && looseCalls == 0,
                "Cold source verification replayed a directory caller or loose callback.");
            cold.RequireColdDirectory(wav);
            Require(cold.Capture().Attempts.Count == 2, "Cold directory source check minted an additional call.");
            Reject<InvalidDataException>(() => new FalloutArchiveFileManager(source, changed, [], null, NoLoose, Resolve, capture));
            Reject<InvalidDataException>(() => new FalloutArchiveFileManager(source, modes, [second], absent, NoLoose, Resolve, capture));
            var multiple = new FalloutArchiveFileManager(source, modes, [archive, second], absent, NoLoose, Resolve);
            Reject<NotSupportedException>(() => multiple.ReadDirectory("data\\sound\\a\\", ".wav"));
            var failed = multiple.Capture();
            var failedCold = new FalloutArchiveFileManager(source, modes, [archive, second], absent, NoLoose, Resolve, failed);
            Reject<InvalidOperationException>(() => failedCold.ReadDirectory("data\\sound\\a\\", ".wav"));
            Require(failedCold.Capture().Attempts.Single().Phase == FalloutArchiveDirectoryPhase.Failed && looseCalls == 0,
                "A refused registry prefix became a cached success or replayed after cold.");
            var noVisibility = new FalloutArchiveFileManager(source, modes, [archive],
                absent with { UnownedReason = "authored actual search-root producer missing" }, NoLoose, Resolve);
            Reject<NotSupportedException>(() => noVisibility.ReadDirectory("data\\sound\\a\\", ".wav"));
            var nonempty = new FalloutArchiveFileManager(source, modes, [archive], new(new('c', 64), true, 1, new('d', 64)), NoLoose, Resolve);
            Reject<NotSupportedException>(() => nonempty.ReadDirectory("data\\sound\\a\\", ".wav"));
            var drift = new FalloutArchiveFileManager(source, modes, [archive], absent, NoLoose, logical => Path.Combine(root.FullName, "loose.wav"));
            Reject<NotSupportedException>(() => drift.ReadDirectory("data\\sound\\a\\", ".wav"));
            var alternate = new FalloutArchiveFileManager(source, Startup(source, invalidate: 0), [archive], absent, NoLoose, Resolve);
            Reject<NotSupportedException>(() => alternate.ReadDirectory("data\\sound\\a\\", ".wav"));
            Require(looseCalls == 0, "An unowned alternate provider invoked loose enumeration.");
            var loosePaths = new[] { "sound\\a\\z.wav", "sound\\a\\a.wav" };
            var looseOwner = new FalloutArchiveFileManager(source, changed, [], null,
                (_, _) => { ++looseCalls; return new(root.FullName, loosePaths, new('f', 64)); },
                logical => Path.GetFullPath(Path.Combine(root.FullName, logical.Replace('\\', Path.DirectorySeparatorChar))));
            Require(looseOwner.ReadDirectory("data\\sound\\a\\", ".wav").Paths.SequenceEqual(loosePaths) && looseCalls == 1,
                "UseArchives zero loaded a registry or sorted the actual loose producer.");
            Reject<NotSupportedException>(() => looseOwner.RefuseRuntimeSetter(FalloutArchiveModeSetter.UseArchives, "1", "authored-source-command"));
            var setter = looseOwner.Capture();
            var coldSetter = new FalloutArchiveFileManager(source, changed, [], null,
                (_, _) => new(root.FullName, loosePaths, new('f', 64)),
                logical => Path.GetFullPath(Path.Combine(root.FullName, logical.Replace('\\', Path.DirectorySeparatorChar))), setter);
            Reject<InvalidOperationException>(() => coldSetter.ReadDirectory("data\\sound\\a\\", ".wav"));
            Require(coldSetter.Capture().Modes.UseArchives == 0 && coldSetter.Capture().SetterFailure?.Requested == "1",
                "Unsupported actual setter changed startup bytes or vanished after cold.");
            var unregistered = new FalloutArchiveFileManager(source, modes with
            { ArchiveList = modes.ArchiveList with { Value = "Authored-Unrelated.bsa" } }, [archive], absent, NoLoose, Resolve);
            Reject<NotSupportedException>(() => unregistered.ReadDirectory("data\\sound\\a\\", ".wav"));
            var omitted = new FalloutArchiveFileManager(source, modes with
            { ArchiveList = modes.ArchiveList with { Value = "Authored-One.bsa,Authored-Missing.bsa" } }, [archive], absent, NoLoose, Resolve);
            Reject<NotSupportedException>(() => omitted.ReadDirectory("data\\sound\\missing\\", ".wav"));
            Require(omitted.Capture().Attempts.Single().Phase == FalloutArchiveDirectoryPhase.Failed,
                "A missing configured source archive was turned into an empty directory success.");
            var noSoundPath = Path.Combine(root.FullName, "Authored-NoSound.bsa"); Write(noSoundPath, 16);
            using var noSound = new FalloutBsaArchive(noSoundPath);
            var filtered = new FalloutArchiveFileManager(source, modes with
            { ArchiveList = modes.ArchiveList with { Value = "Authored-NoSound.bsa" } }, [noSound], absent, NoLoose, Resolve);
            Require(filtered.ReadDirectory("data\\sound\\a\\", ".wav").Paths.Count == 0,
                "Archive filename/text extension invented original SOUND type membership.");
            var badPath = Path.Combine(root.FullName, "Authored-BadHash.bsa"); Write(badPath, 8, corruptHash: true);
            using var bad = new FalloutBsaArchive(badPath);
            var badModes = modes with { ArchiveList = modes.ArchiveList with { Value = Path.GetFileName(badPath) } };
            var badOwner = new FalloutArchiveFileManager(source, badModes, [bad], absent, NoLoose, logical => badPath + "::" + logical);
            Reject<InvalidDataException>(() => badOwner.ReadDirectory("data\\sound\\a\\", ".wav"));
            SharedSelector(root.FullName, source, modes, absent, archive, second, NoLoose, Resolve);
            owner.Retire(); Reject<ObjectDisposedException>(() => owner.ReadDirectory("data\\sound\\a\\", ".wav"));
            Require(archive.Read("sound\\a\\z.wav").SequenceEqual(new byte[] { 1 }), "File-manager retirement closed its source-owned borrowed archive.");
            Require(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(original), "Source selection mutated its original authored archive.");
            Console.WriteLine("OPENNV_SOURCE_ARCHIVE_FILE_MANAGER_PASS modes=independent actualFullReader=true tablePrepend=true extensionClass=true " +
                "coldNoCallerReplay=true sharedPrefix=true failedPrefix=true forgedPrefixRefused=true visibilityRefused=true " +
                "winnerDriftRefused=true runtimeSetterRefused=true inputUnchanged=true nativeAudio=unexecuted");
        }
        finally { Directory.Delete(root.FullName, recursive: true); }
    }

    private static void SharedSelector(string root, FalloutArchiveFileManagerSource source, FalloutArchiveStartupModes modes,
        FalloutArchiveInvalidationInput absent, FalloutBsaArchive first, FalloutBsaArchive second,
        Func<string, string, FalloutLooseSoundDirectory> noLoose, Func<string, string?> resolve)
    {
        var file = Path.Combine(root, "Authored-Selection.esm");
        var sound = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian(sound.AsSpan(4), (uint)FalloutSoundFlags.MenuSound);
        for (var index = 0; index < 5; ++index) BinaryPrimitives.WriteInt16LittleEndian(sound.AsSpan(12 + index * 2), 100);
        var original = Join(Record("TES4", 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8]))),
            Record("SOUN", 0x810, Field("EDID", Encoding.ASCII.GetBytes("AuthoredDirectory\0")),
                Field("FNAM", Encoding.ASCII.GetBytes("data\\sound\\a\\\0")), Field("SNDD", sound)));
        File.WriteAllBytes(file, original);
        using var records = FalloutPluginStack.Load(root, ["Authored-Selection.esm"]);
        var descriptor = FalloutSoundRecordReader.Read(records, new("Authored-Selection.esm", 0x810));
        var declaration = FalloutMenuSoundSelectionSource.Read(source.EngineSha256, source.RuntimeSha256);
        var call = new FalloutMenuSoundSelectionCall("authored-full-reader", 1, 0, descriptor.FormKey);
        var files = new FalloutArchiveFileManager(source, modes, [first], absent, noLoose, resolve);
        var selector = new FalloutMenuSoundSelection(records, declaration, files.ReadDirectory, authoredTickProducer: () => 1,
            requireSourceDirectory: files.RequireColdDirectory, captureFileManager: files.Capture);
        _ = selector.Prepare(descriptor, call);
        var saved = selector.Capture();
        Require(saved.FileManager?.Attempts.Count == 1 && saved.Attempts.Single().Directories.Count == 1,
            "Actual shared directory receipt disappeared before selection capture.");
        var coldFiles = new FalloutArchiveFileManager(source, modes, [first], absent, noLoose, resolve, saved.FileManager);
        var cold = new FalloutMenuSoundSelection(records, declaration, coldFiles.ReadDirectory, saved,
            () => throw new IOException("Cold selection reseeded RNG."), coldFiles.RequireColdDirectory, coldFiles.Capture);
        Require(cold.Capture().FileManager?.Attempts.Count == 1 && cold.Capture().Random.Draws == saved.Random.Draws,
            "Cold shared source verification replayed a directory call or RNG draw.");
        var prefix = saved.FileManager ?? throw new InvalidDataException("Authored shared source lost its file-manager capture.");
        var forged = prefix with { Attempts = prefix.Attempts.Append(prefix.Attempts[0] with { Ordinal = 2 }).ToArray() };
        var forgedFiles = new FalloutArchiveFileManager(source, modes, [first], absent, noLoose, resolve, forged);
        Reject<InvalidDataException>(() => new FalloutMenuSoundSelection(records, declaration, forgedFiles.ReadDirectory,
            saved with { FileManager = forged }, requireSourceDirectory: forgedFiles.RequireColdDirectory,
            captureFileManager: forgedFiles.Capture));
        var refusedFiles = new FalloutArchiveFileManager(source, modes, [first, second], absent, noLoose, resolve);
        var refused = new FalloutMenuSoundSelection(records, declaration, refusedFiles.ReadDirectory,
            authoredTickProducer: () => throw new IOException("Refused source directory reached RNG."),
            requireSourceDirectory: refusedFiles.RequireColdDirectory, captureFileManager: refusedFiles.Capture);
        Reject<NotSupportedException>(() => refused.Prepare(descriptor, call));
        var failed = refused.Capture();
        Require(failed.FileManager?.Attempts.Single().FailureType == failed.Attempts.Single().FailureType &&
            !failed.Random.Constructed, "Actual file-manager refusal became a correlation failure or consumed RNG.");
        var refusedColdFiles = new FalloutArchiveFileManager(source, modes, [first, second], absent, noLoose, resolve, failed.FileManager);
        var refusedCold = new FalloutMenuSoundSelection(records, declaration, refusedColdFiles.ReadDirectory, failed,
            requireSourceDirectory: refusedColdFiles.RequireColdDirectory, captureFileManager: refusedColdFiles.Capture);
        Reject<InvalidOperationException>(() => refusedCold.Prepare(descriptor, call with { Occurrence = 2 }));
        Require(File.ReadAllBytes(file).SequenceEqual(original), "Shared sound owners mutated their original authored source.");

        static byte[] Field(string signature, byte[] bytes) => Join(Encoding.ASCII.GetBytes(signature), BitConverter.GetBytes(checked((ushort)bytes.Length)), bytes);
        static byte[] Record(string signature, uint id, params byte[][] fields)
        {
            var bytes = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id);
            return Join(header, bytes);
        }
        static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
    }

    private static FalloutArchiveStartupModes Startup(FalloutArchiveFileManagerSource source, int? use = null, int? invalidate = null)
    {
        FalloutIniDeclaration[] declarations = [new("bUseArchives:Archive", FalloutIniCollection.Main, 1),
            new("bInvalidateOlderFiles:Archive", FalloutIniCollection.Main, 1),
            new("sArchiveList:Archive", FalloutIniCollection.Main, 100), new("sInvalidationFile:Archive", FalloutIniCollection.Main, 200),
            new("sLocalMasterPath:General", FalloutIniCollection.Main, 300)];
        List<FalloutInstallationSetting> profile = [];
        if (use is { } archives) profile.Add(new("Archive", "bUseArchives", archives.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (invalidate is { } old) profile.Add(new("Archive", "bInvalidateOlderFiles", old.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var settings = FalloutInstallationSettings.ReadIniLayers(() => declarations,
            profile.Count == 0 ? [] : [new("authored-profile", null, profile)], "authored-executable");
        return source.ReadStartup(settings, [new(declarations[2], "Authored-One.bsa"),
            new(declarations[3], "Authored-Invalidation.txt"), new(declarations[4], "Data\\")]);
    }

    private static void Write(string path, uint types, bool corruptHash = false)
    {
        var folder = Encoding.ASCII.GetBytes("sound\\a\0");
        var names = Encoding.ASCII.GetBytes(string.Join('\0', Members.Select(row => row.Name)) + "\0");
        var blockEnd = 36 + 16 + 1 + folder.Length + Members.Length * 16;
        var data = new byte[blockEnd + names.Length + Members.Length];
        U32(0, 0x00415342); U32(4, 104); U32(8, 36); U32(12, 3); U32(16, 1); U32(20, (uint)Members.Length);
        U32(24, (uint)folder.Length); U32(28, (uint)names.Length); U32(32, types);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(36), FolderHash);
        U32(44, (uint)Members.Length); U32(48, (uint)(52 + names.Length));
        data[52] = (byte)folder.Length; folder.CopyTo(data, 53);
        var at = 53 + folder.Length;
        for (var index = 0; index < Members.Length; ++index)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at), Members[index].Hash ^ (corruptHash && index == 0 ? 1UL : 0UL));
            U32(at + 8, 1); U32(at + 12, (uint)(blockEnd + names.Length + index)); at += 16;
            data[blockEnd + names.Length + index] = (byte)(index + 1);
        }
        names.CopyTo(data, blockEnd); File.WriteAllBytes(path, data);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidDataException("Authored source file-manager boundary was accepted: " + typeof(T).Name);
    }
}
