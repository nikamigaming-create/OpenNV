using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class SourceMenuSoundSelectionContracts
{
    private const string FirstEngine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string SecondEngine = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    internal static void Run()
    {
        var first = FalloutMenuSoundSelectionSource.Read(FirstEngine, new('a', 64));
        var second = FalloutMenuSoundSelectionSource.Read(SecondEngine, new('b', 64));
        Random(first); Paths();
        var folder = Directory.CreateTempSubdirectory("opennv-source-menu-selection-");
        var file = Path.Combine(folder.FullName, "AuthoredSelection.esm");
        var bytes = Join(Record("TES4", 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8]))),
            Sound(0x810, "fx\\authored\\exact.wav"), Sound(0x811, "fx\\authored\\variants.wav\\"),
            Sound(0x812, "fx\\authored\\variants\\"), Sound(0x813, "fx\\authored\\other\\"),
            Sound(0x814, "fx\\authored\\pitch\\", FalloutSoundFlags.RandomFrequencyShift));
        File.WriteAllBytes(file, bytes); var hash = SHA256.HashData(bytes);
        try
        {
            using var records = FalloutPluginStack.Load(folder.FullName, ["AuthoredSelection.esm"]);
            FalloutSoundRecord Read(uint id) => FalloutSoundRecordReader.Read(records, new("AuthoredSelection.esm", id));
            FalloutMenuSoundSelectionCall Call(long ordinal, FalloutSoundRecord sound) => new("authored-real-reader", ordinal, 0, sound.FormKey);
            var exact = Read(0x810);
            var exactOwner = new FalloutMenuSoundSelection(records, first, (_, _) => throw new InvalidDataException("Exact file enumerated a directory."),
                authoredTickProducer: () => throw new InvalidDataException("Exact file acquired the seed import."));
            var prepared = exactOwner.Prepare(exact, Call(1, exact));
            Require(prepared.Descriptor.LogicalPath == "sound\\fx\\authored\\exact.wav" &&
                !exactOwner.Capture().Random.Constructed && exactOwner.Capture().Random.Draws == 0,
                "Exact source cue constructed the global generator or changed its prepared path.");
            Reject<InvalidOperationException>(() => exactOwner.Prepare(exact, Call(1, exact)));
            var dotted = Read(0x811); var directory = Read(0x812);
            var enumerated = new List<string>();
            FalloutMenuSoundDirectory Ordered(string raw, string extension)
            {
                enumerated.Add(extension);
                var prefix = FalloutMenuSoundSelectionSource.LogicalPath(raw) + "\\";
                // Authored order intentionally disagrees with namespace sorting.
                string[] paths = extension == ".ogg" ? [] : [prefix + "z.wav", prefix + "a.wav"];
                return Folder(raw, extension, paths);
            }
            var warm = new FalloutMenuSoundSelection(records, first, Ordered, authoredTickProducer: () => 1);
            Require(dotted.HasExactFile, "Fixture did not reach the real canonical trailing-separator loss.");
            var firstChoice = warm.Prepare(dotted, Call(1, dotted));
            Require(firstChoice.Descriptor.LogicalPath.EndsWith("z.wav", StringComparison.Ordinal) &&
                enumerated.SequenceEqual([".ogg", ".wav"]) && warm.Capture().Random.Draws == 1,
                "Original directory order or first full-width modulo draw was replaced.");
            var secondChoice = warm.Prepare(dotted, Call(2, dotted));
            var snapshot = warm.Capture();
            Require(secondChoice.Descriptor.LogicalPath.EndsWith("a.wav", StringComparison.Ordinal) && snapshot.Random.Draws == 228 &&
                snapshot.Attempts[1].Draws.Count == 227 && snapshot.HistoryCursor == 0 &&
                snapshot.HistoryIndices[0] == 1 && snapshot.HistoryHashes.Skip(1).All(value => value == 0),
                "Signed history lost its actual redraw prefix or invented a cursor increment.");
            var readCount = 0; var sourceChecks = 0;
            void VerifyDirectory(FalloutMenuSoundDirectory saved)
            {
                ++sourceChecks; var current = Ordered(saved.RawPath, saved.Extension);
                if (current.OrderSha256 != saved.OrderSha256 || current.ProducerSha256 != saved.ProducerSha256)
                    throw new InvalidDataException("Authored ordered source drifted.");
            }
            var cold = new FalloutMenuSoundSelection(records, first, (raw, extension) => { ++readCount; return Ordered(raw, extension); }, snapshot,
                () => throw new InvalidDataException("Cold RNG reseeded."), VerifyDirectory);
            Require(readCount == 0 && sourceChecks == 2, "Cold admission replayed selection or skipped distinct ordered-input validation.");
            Reject<InvalidDataException>(() => new FalloutMenuSoundSelection(records, first, Ordered, snapshot,
                requireSourceDirectory: _ => throw new InvalidDataException("Authored source order changed.")));
            var nextWarm = warm.Prepare(directory, Call(3, directory));
            var nextCold = cold.Prepare(directory, Call(3, directory));
            Require(nextWarm.Descriptor.LogicalPath == nextCold.Descriptor.LogicalPath &&
                warm.Capture().Random.Words.SequenceEqual(cold.Capture().Random.Words) && warm.Capture().Random.Draws == cold.Capture().Random.Draws,
                "Cold source continuation changed the next genuine choice/draw state.");
            Reject<InvalidDataException>(() => new FalloutMenuSoundSelection(records, first, Ordered, snapshot with
            {
                Attempts = [snapshot.Attempts[0], snapshot.Attempts[1] with { HistorySlot = null, DirectoryHash = null, ReplacedHash = null, ReplacedIndex = null }]
            }));
            Reject<InvalidDataException>(() => new FalloutMenuSoundSelection(records, first, Ordered, snapshot with
            {
                Attempts = [snapshot.Attempts[0] with { RawPath = "fx\\authored\\changed\\" }, snapshot.Attempts[1]]
            }));
            var noHistory = new FalloutMenuSoundSelection(records, second, Ordered, authoredTickProducer: () => 1);
            var repeatedFirst = noHistory.Prepare(directory, Call(1, directory));
            var repeatedSecond = noHistory.Prepare(directory, Call(2, directory));
            Require(repeatedFirst.Descriptor.LogicalPath == repeatedSecond.Descriptor.LogicalPath && noHistory.Capture().Random.Draws == 2 &&
                noHistory.Capture().HistoryHashes.All(value => value == 0), "No-history source invented an anti-repeat consumer.");
            var one = new FalloutMenuSoundSelection(records, first, (raw, extension) => Folder(raw, extension,
                [FalloutMenuSoundSelectionSource.LogicalPath(raw) + "\\only" + extension]), authoredTickProducer: () => 1);
            var oneChoice = one.Prepare(directory, Call(1, directory));
            Require(oneChoice.Descriptor.LogicalPath.EndsWith("only.ogg", StringComparison.Ordinal) && one.Capture().Random.Draws == 1 &&
                one.Capture().Attempts[0].HistorySlot is null, "One-node compressed directory skipped its genuine draw or changed history.");
            var pitch = Read(0x814);
            var pitchOwner = new FalloutMenuSoundSelection(records, first, Ordered, authoredTickProducer: () => 1);
            var pitchPrepared = pitchOwner.Prepare(pitch, Call(1, pitch));
            var playback = new FalloutMenuCuePlaybackSource(first.EngineSha256, first.RuntimeSha256, FalloutMenuCuePlaybackSource.CurrentContractSha256);
            Reject<NotSupportedException>(() => playback.PreparedFile(records, pitchPrepared, pitchOwner));
            Require(pitchOwner.Capture().Random.Draws == 1 && pitchOwner.Capture().Attempts[0].Phase == FalloutMenuSoundSelectionPhase.Selected,
                "Unowned later playback refunded an actual directory/RNG choice.");
            var failed = new FalloutMenuSoundSelection(records, first, (_, _) => throw new IOException("Authored real provider interruption."),
                authoredTickProducer: () => throw new InvalidDataException("Unavailable directory reached RNG."));
            Reject<IOException>(() => failed.Prepare(directory, Call(1, directory)));
            var failedSnapshot = failed.Capture(); var failedCold = new FalloutMenuSoundSelection(records, first, Ordered, failedSnapshot);
            Require(failedCold.Failure == failed.Failure && !failedSnapshot.Random.Constructed, "Cold failure lost the actual unconsumed prefix.");
            Reject<InvalidOperationException>(() => failedCold.Prepare(directory, Call(2, directory)));
            var interruptedSeed = new FalloutMenuSoundSelection(records, first, Ordered, authoredTickProducer: () => throw new IOException("Seed import failed."));
            Reject<IOException>(() => interruptedSeed.Prepare(directory, Call(1, directory)));
            var seedPrefix = interruptedSeed.Capture();
            Require(seedPrefix.Random.Constructed && seedPrefix.Random.Seed is null && seedPrefix.Random.Draws == 0,
                "Seed exception manufactured a value or lost entered constructor ownership.");
            _ = new FalloutMenuSoundSelection(records, first, Ordered, seedPrefix);
            var nonreturn = new FalloutMenuSoundSelection(records, first, Ordered, authoredTickProducer: () => 0);
            _ = nonreturn.Prepare(directory, Call(1, directory));
            Reject<NotSupportedException>(() => nonreturn.Prepare(directory, Call(2, directory)));
            var loopPrefix = nonreturn.Capture();
            Require(loopPrefix.Attempts[1].Draws.Count == FalloutMenuSoundSelection.MaximumRepeatDraws &&
                loopPrefix.HistoryIndices[0] == 0 && loopPrefix.Attempts[1].HistorySlot is null,
                "Bounded nonreturn manufactured a different node or completed history mutation.");
            _ = new FalloutMenuSoundSelection(records, first, Ordered, loopPrefix);
            var wide = new FalloutMenuSoundSelection(records, first, (raw, extension) => Folder(raw, extension,
                Enumerable.Range(0, 256).Select(index => FalloutMenuSoundSelectionSource.LogicalPath(raw) + "\\v" + index + extension).ToArray()),
                authoredTickProducer: () => 1);
            _ = wide.Prepare(directory, Call(1, directory));
            Require(wide.Capture().Attempts[0].SelectedIndex == 132 && wide.Capture().HistoryIndices[0] == -124,
                "History widened the genuine signed-byte remembered index.");
            records.SoundPaths.Set(exact.FormKey, "fx\\authored\\changed.wav");
            Reject<NotSupportedException>(() => exactOwner.Capture());
            Require(SHA256.HashData(File.ReadAllBytes(file)).AsSpan().SequenceEqual(hash), "Menu owner mutated its authored input.");
        }
        finally { Directory.Delete(folder.FullName, true); }
        Console.WriteLine("OPENNV_SOURCE_MENU_SOUND_SELECTION_PASS fullReader=true exactNoGetter=true directoryOrder=true sourcePolicies=true " +
            "oneNodeDraw=true signedHistory=true committedRefusalPrefix=true coldNoReplay=true sourceUnchanged=true nativeAudio=unexecuted archiveDirectory=unowned");
    }

    private static void Random(FalloutMenuSoundSelectionSource source)
    {
        var ticks = 0; var random = new FalloutSourceRandom(source, () => { ++ticks; return 1; });
        Require(random.Next(0) == 0 && ticks == 0 && random.Capture().Constructed && random.Capture().Words.Count == 0,
            "Zero bound invented a seed/state table.");
        Require(random.Next(uint.MaxValue) == 4285570180U && random.Next(32767) == (1294850450U & 32767U) &&
            random.Next(3) == 4001963888U % 3 && ticks == 1, "Original untempered seed/twist/bound arithmetic diverged.");
        for (var index = 0; index < 625; ++index) _ = random.Next(uint.MaxValue);
        var saved = random.Capture(); var cold = new FalloutSourceRandom(source, () => throw new InvalidDataException("Cold reseed."), saved);
        for (var index = 0; index < 12; ++index) Require(random.Next(97) == cold.Next(97), "Cold RNG changed the next unconsumed word.");
        Reject<InvalidDataException>(() => new FalloutSourceRandom(source, saved with { Cursor = 625 }));
        Reject<InvalidDataException>(() => new FalloutSourceRandom(source, saved with { Words = new uint[624] }));
    }
    private static void Paths()
    {
        Require(FalloutMenuSoundSelectionSource.PreparedPath("fx\\authored\\") == "data\\sound\\fx\\authored\\" &&
            FalloutMenuSoundSelectionSource.PreparedPath("FX\\authored\\") == "FX\\authored\\" &&
            FalloutMenuSoundSelectionSource.PreparedPath("song\\authored\\") == "data\\sound\\song\\authored\\",
            "Original case-sensitive format prefix was replaced by universal canonicalization.");
        Reject<NotSupportedException>(() => FalloutMenuSoundSelectionSource.LogicalPath("FX\\authored\\"));
        Reject<NotSupportedException>(() => FalloutMenuSoundSelectionSource.PreparedPath("fx" + new string('a', 250)));
        Reject<InvalidDataException>(() => FalloutMenuSoundSelectionSource.PathBytes("fx\0other"));
    }
    private static FalloutMenuSoundDirectory Folder(string raw, string extension, IReadOnlyList<string> paths) =>
        new(raw, extension, paths, FalloutMenuSoundSelection.OrderDigest(paths), FalloutAdvancementRuntimeReceipt.Hash("authored-ordered-provider"));
    private static byte[] Sound(uint id, string path, FalloutSoundFlags flags = FalloutSoundFlags.MenuSound)
    {
        var data = new byte[36]; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)flags);
        for (var index = 0; index < 5; ++index) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + index * 2), 100);
        return Record("SOUN", id, Field("EDID", Encoding.ASCII.GetBytes("Authored" + id + '\0')),
            Field("FNAM", Encoding.ASCII.GetBytes(path + '\0')), Field("SNDD", data));
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), data.Length); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), id);
        return Join(header, data);
    }
    private static byte[] Field(string signature, byte[] data) => Join(Encoding.ASCII.GetBytes(signature), BitConverter.GetBytes(checked((ushort)data.Length)), data);
    private static byte[] Join(params byte[][] pieces) => pieces.SelectMany(row => row).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidDataException("Expected genuine menu selection refusal " + typeof(T).Name + ".");
    }
}
