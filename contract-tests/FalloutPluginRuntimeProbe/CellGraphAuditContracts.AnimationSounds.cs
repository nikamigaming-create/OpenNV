using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private const string AnimationSoundOverride = "SoundOverride.esp";

    private static void NifAnimationSoundDeclarations()
    {
        AnimationSoundRawGrammar();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-nif-sound-dependencies-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(directory, "Game");
        var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);
        try
        {
            File.WriteAllBytes(Path.Combine(data, Plugin), AnimationSoundBasePlugin());
            File.WriteAllBytes(Path.Combine(data, AnimationSoundOverride), AnimationSoundOverridePlugin());
            var wavA = AnimationSoundWav(31); var wavB = AnimationSoundWav(43); var wavC = AnimationSoundWav(59);
            AnimationSoundInput(data, "sound/fx/variants/a.wav", wavA);
            AnimationSoundInput(data, "sound/fx/variants/c.WAV", wavC);
            AnimationSoundInput(data, "sound/fx/variants/nested/excluded.wav", wavA);
            AnimationSoundInput(data, "sound/fx/variants/excluded.mp3", [1, 3, 5, 7]);
            AnimationSoundInput(data, "sound/fx/variantsElsewhere/decoy.wav", wavB);
            AnimationSoundInput(data, "sound/fx/base-directory/obsolete.wav", wavA);
            AnimationSoundInput(data, "sound/fx/exact.wav", wavA);
            AnimationSoundInput(data, "sound/fx/exact.mp3", [11, 13, 17, 19]);
            AnimationSoundInput(data, "sound/fx/obsolete.wav", wavA);
            AnimationSoundInput(data, "sound/fx/out.wav", wavB);
            AnimationSoundInput(data, "sound/fx/changed.wav", wavC);
            AnimationSoundInput(data, "sound/fx/missing.wav/decoy.wav", wavA);
            var archive = Path.Combine(data, "SoundSource.bsa");
            AnimationSoundArchive(archive, wavB);
            var archiveIni = Path.Combine(directory, "archives.ini");
            File.WriteAllText(archiveIni, "[Archive]\nsArchiveList=SoundSource.bsa\n");
            var keys = AnimationSoundKeys();
            var nifBytes = AnimationSoundNif(keys);
            AnimationSoundInput(data, "meshes/authored-sounds.kf", nifBytes);
            var before = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
                path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.OrdinalIgnoreCase);

            using (var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                [], [Plugin, AnimationSoundOverride], archiveIni))
            using (var records = FalloutPluginStack.Load(source.PluginSources))
            {
                source.ArchiveWarmup.GetAwaiter().GetResult();
                var resources = new CellGraphAudit.CellAuditResources(source, .1f, records);
                // Regression first: complete NIF block decoding alone does not
                // account for event -> winning SOUN -> all variant edges.
                var model = resources.Read("meshes/authored-sounds.kf", "animation");
                var declared = model.DependencyDeclarations.OfType<CellGraphAudit.AnimationTextKeyRow>().ToArray();
                Require(declared.Length == keys.Length,
                    "Decoded NiTextKeyExtraData lost its raw source-key dependency denominator.");
                var nif = FalloutNifFile.Read(nifBytes);
                var sequence = nif.ReadControllerSequence(1);
                var textKeys = (FalloutNifTextKeyExtraData)nif.ReadObject(sequence.TextKeys);
                Require(sequence.TextKeys == 0 && sequence.StartTime == 0 && sequence.StopTime == 1 &&
                    model.DecodedBlocks == 2 && model.Blocks == 2,
                    "The authored fixture did not reach full NIF text-key/sequence decoding.");
                var closure = new CellGraphAudit.SourceAnimationSoundDependencies(source, records).Read(textKeys);
                Require(closure.Keys.Count == keys.Length && declared.Select(row => row.RawText).SequenceEqual(keys.Select(key => key.Value)) &&
                    declared.Select(row => row.SourceOrdinal).SequenceEqual(Enumerable.Range(0, keys.Length)) &&
                    declared[0].SourceTimeBits == 0x80000000 && declared[1].SourceTimeBits == 0 &&
                    declared.All(row => row.Block == 0 && row.Offset == nif.Blocks[0].Offset && row.Bytes == nif.Blocks[0].Size &&
                        row.Type == "NiTextKeyExtraData" && row.Name == "authored-text-keys" && row.NativeAdmission == "unverified"),
                    "Raw keys, equal-time duplicates, signed zero, source ordinals or original block extent changed.");
                Require(closure.Keys[14].SourceSeconds == 2 && closure.Keys[14].Segments.Any(segment => segment.Variants.Count == 1) &&
                    declared[14].SourceSeconds == 2,
                    "The source dependency audit dropped a key outside the selected sequence interval.");
                var shared = declared.SelectMany(row => row.Segments)
                    .Where(segment => segment.Declaration.Kind == FalloutNifTextKeyDeclarationKind.Sound &&
                        segment.Declaration.EditorId == "SharedNoise").ToArray();
                Require(shared.Length == 4 && shared.All(segment => segment.Variants.Select(variant => variant.Path)
                    .SequenceEqual(["sound/fx/variants/a.wav", "sound/fx/variants/b.wav", "sound/fx/variants/c.wav"])),
                    "Duplicate sound events or unselected immediate WAV variants disappeared.");
                var winning = records.GetEffective(Key(0x101));
                Require(shared.All(segment => segment.Sound == Key(0x101).ToString() && segment.DeclaringMaster == Plugin &&
                    segment.WinningPlugin == AnimationSoundOverride && segment.WinningLoadOrder == 1 &&
                    segment.DeclaredMasters.SequenceEqual([Plugin]) && segment.RawFormId == 0x101 &&
                    segment.RecordSha256 == Convert.ToHexString(SHA256.HashData(winning.ReadData())) &&
                    segment.DeclaredFile == "fx/variants" && segment.CurrentFile == "fx/variants" && segment.PathRevision == 0 &&
                    segment.Descriptor?.Flags == (FalloutSoundFlags.Loop | FalloutSoundFlags.RandomLocation) &&
                    segment.EmitterAdmission.StartsWith("unverified", StringComparison.Ordinal) &&
                    segment.NativeAdmission == "unverified"),
                    "A sound edge lost its exact winning override/master/path provenance or pretended to admit unsupported runtime flags.");
                Require(shared[0].Declaration.Emitter == "Bone Alpha" && shared[1].Declaration.Emitter == "Bone\tAlpha" &&
                    shared[2].Declaration.Emitter == "" && shared[3].Declaration.Emitter == "",
                    "The exact first-space/tab emitter split acquired a guessed bone or lost its suffix.");
                Require(closure.DependencyEdges.All(edge => edge.Kind == "audio") &&
                    closure.DependencyEdges.Select(edge => edge.Path).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(
                        ["sound/fx/variants/a.wav", "sound/fx/variants/b.wav", "sound/fx/variants/c.wav",
                         "sound/fx/exact.wav", "sound/fx/exact.mp3", "sound/fx/out.wav", "sound/fx/missing.wav"]) &&
                    model.Dependencies.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(closure.DependencyEdges.Select(edge => edge.Path)),
                    "The actual variant owner was replaced by a prefix, random selection, codec substitution or texture role.");
                var missing = declared[2].Segments.Single(segment => segment.Declaration.EditorId == "MissingNoise");
                Require(missing.Error is not null && missing.ReaderOperation == "FalloutSoundRecordReader.Find" &&
                    declared[2].Segments.Single(segment => segment.Declaration.EditorId == "ExactNoise").Variants.Count == 1,
                    "A refused sound hid a later valid sibling in the same raw key.");
                foreach (var ordinal in new[] { 3, 4, 5, 6, 11 })
                    Require(declared[ordinal].Segments.Single().SourceDisposition == "sound-source-declaration-refused" &&
                        declared[ordinal].Segments.Single().Variants.Count == 0 && declared[ordinal].Segments.Single().Error is not null,
                        "A deleted/ambiguous/invalid/no-variant/compound owner was accepted or dropped.");
                Require(closure.Failures.Count == 7 && model.Failures.Count == closure.Failures.Count &&
                    declared[8].Segments.Single().Declaration.Kind == FalloutNifTextKeyDeclarationKind.StopSounds &&
                    declared[8].Segments.Single().Variants.Count == 0 &&
                    declared[9].Segments.Single().Declaration.Kind == FalloutNifTextKeyDeclarationKind.Unbound &&
                    declared[12].Segments.Single().Declaration.DispatchOrdinal == 0 &&
                    declared[13].Segments.Single().Declaration.DispatchOrdinal is null,
                    "A stop/unbound/empty declaration became a media request or a source refusal was concealed.");
                Require(!records.TryGetEffective(Key(0x104), out _) &&
                    !model.Dependencies.Contains("sound/fx/obsolete.wav") && !model.Dependencies.Contains("sound/fx/base-directory/obsolete.wav"),
                    "A deleted winner or losing FNAM source was resurrected.");
                foreach (var edge in closure.DependencyEdges)
                {
                    if (edge.Path == "sound/fx/missing.wav")
                    {
                        Require(resources.Resources.TryGetValue(edge.Path, out var refused) && refused.Source is null &&
                            refused.Sha256 is null && refused.Failures.Count != 0 &&
                            !source.TryRead(edge.Path, null, out _, out _) &&
                            declared[15].Segments[0].Variants.Single().Path == edge.Path &&
                            declared[15].Segments[1].Variants.Single().Path == "sound/fx/exact.wav",
                            "An exact missing file was substituted with its valid prefix decoy or hid a later sibling.");
                        continue;
                    }
                    Require(resources.Resources.TryGetValue(edge.Path, out var media) && media.Kind == "audio" &&
                        media.Source is not null && media.Sha256 is not null && media.Failures.Count != 0,
                        "A declared variant lacked an actual complete source read or fabricated audio format acceptance.");
                    var retainedMedia = resources.Resources[edge.Path];
                    Require(source.TryRead(edge.Path, null, out var bytes, out var identity) && retainedMedia.Source == identity &&
                        retainedMedia.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)),
                        "The sound declaration/resource join lost exact original byte or winning source identity.");
                }
                Require(source.TryRead("sound/fx/variants/b.wav", null, out var bBytes, out var bSource) &&
                    bBytes.SequenceEqual(wavB) && bSource.Contains("SoundSource.bsa", StringComparison.Ordinal),
                    "The archive-only variant was substituted with a loose or prefixed decoy.");
                var absent = new CellGraphAudit.SourceAnimationSoundDependencies(source, null).Read(textKeys);
                Require(absent.Keys.Count == keys.Length && absent.DependencyEdges.Count == 0 &&
                    absent.Keys.SelectMany(row => row.Segments).Where(segment => segment.Declaration.Kind == FalloutNifTextKeyDeclarationKind.Sound)
                        .All(segment => segment.ReaderOperation == "exact-owned-source" && segment.Error is not null),
                    "Missing records were reopened from Current or lost raw declarations.");
                using var unrelated = FalloutPluginStack.Load(data, [Plugin, AnimationSoundOverride]);
                var mismatch = new CellGraphAudit.SourceAnimationSoundDependencies(source, unrelated).Read(textKeys);
                Require(mismatch.Keys.Count == keys.Length && mismatch.DependencyEdges.Count == 0 && mismatch.Failures.Count == absent.Failures.Count,
                    "Unrelated record bytes were admitted as the exact owned source context.");
                Require(declared[16].Segments[0].Declaration.EditorId == "" &&
                    declared[16].Segments[0].ReaderOperation == "FalloutSoundRecordReader.Find" &&
                    declared[16].Segments[0].Error is not null && declared[16].Segments[1].Variants.Count == 1,
                    "An empty sound name acquired a guessed owner or hid another valid dispatch.");

                // A declared mutable path revision is a different descriptor.
                // No script execution, native event or voice is created here.
                records.SoundPaths.Set(Key(0x102), "fx/changed.wav");
                var revised = new CellGraphAudit.SourceAnimationSoundDependencies(source, records).Read(textKeys);
                var exact = revised.Keys[2].Segments.Single(segment => segment.Declaration.EditorId == "ExactNoise");
                Require(exact.PathRevision == 1 && exact.DeclaredFile == "fx/exact.wav" &&
                    exact.CurrentFile == "fx/changed.wav" && exact.Variants.Single().Path == "sound/fx/changed.wav" &&
                    exact.RecordSha256 == declared[2].Segments.Single(segment => segment.Declaration.EditorId == "ExactNoise").RecordSha256,
                    "A current path revision changed original record provenance or reused an obsolete media edge.");
            }
            Require(before.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key)))) &&
                before.Count == Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count(),
                "Animation sound declaration inspection changed authored original sources or exported a substitute.");
            AnimationSoundReaderRefusal([new(0, "Sound: ExactNoise"), new(-1, "Sound: ExactNoise")]);
            AnimationSoundReaderRefusal([new(float.NaN, "Sound: ExactNoise")]);
            Console.WriteLine("OPENNV_CELL_NIF_ANIMATION_SOUND_DEPENDENCIES_PASS rawKeys=true duplicateTimes=true exactDispatchGrammar=true winningSoun=true allUnselectedVariants=true sourceSiblings=true looseBsaIdentity=true outsideTimeline=true sourceBytes=unchanged audioDecode=uninspected emitterAndNative=unverified");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void AnimationSoundRawGrammar()
    {
        const string text = "\r\n start \rSound: A Bone One\n \t \r\nEnum: StopSounds\tBone\nEnum: StopSounds Bone\nend\r\n";
        var declarations = FalloutNifTextKeyDeclarations.Read(text);
        Require(declarations.Select(row => row.RawSegment).SequenceEqual(
            ["", "", " start ", "Sound: A Bone One", " \t ", "", "Enum: StopSounds\tBone", "Enum: StopSounds Bone", "end", "", ""]) &&
            declarations.Where(row => row.DispatchOrdinal is not null).Select(row => row.Text)
                .SequenceEqual(["start", "Sound: A Bone One", "", "Enum: StopSounds\tBone", "Enum: StopSounds Bone", "end"]),
            "CR/LF, whitespace-only, empty and structural dispatch declarations differ from the actual runtime grammar.");
        Require(declarations[3].EditorId == "A" && declarations[3].Emitter == "Bone One" &&
            declarations[6].Kind == FalloutNifTextKeyDeclarationKind.Unbound &&
            declarations[7].Kind == FalloutNifTextKeyDeclarationKind.StopSounds && declarations[7].Emitter == "Bone" &&
            declarations.Where(row => row.DispatchOrdinal is not null).Select(row => row.DispatchOrdinal!.Value)
                .SequenceEqual(Enumerable.Range(0, 6)) &&
            declarations.All(row => text.Substring(row.Offset, row.Length) == row.RawSegment),
            "Raw segment coordinates, exact literal stop prefix, emitter split or dispatch ordinals changed.");
        var empty = FalloutNifTextKeyDeclarations.Read("");
        Require(empty.Count == 1 && empty[0].DispatchOrdinal is null && empty[0].Kind == FalloutNifTextKeyDeclarationKind.EmptySegment,
            "An encoded empty raw key disappeared or fabricated a dispatch.");
    }

    private static FalloutNifTextKey[] AnimationSoundKeys() =>
    [
        new(BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), "\r\n start \r\n Sound: SharedNoise Bone Alpha \r\n\r\n"),
        new(0, "sOuNd:\tSharedNoise\tBone\tAlpha"),
        new(.25f, "Sound: MissingNoise\nSound: SharedNoise\r\nSound: ExactNoise"),
        new(.5f, "Sound: DeletedNoise"),
        new(.5f, "Sound: AmbiguousNoise"),
        new(.5f, "Sound: InvalidNoise"),
        new(.5f, "Sound: EmptyDir"),
        new(.75f, "Sound: ExactMp3"),
        new(1, "Enum: StopSounds Bone Alpha"),
        new(1, "Enum: StopSounds\tBone"),
        new(1, " Sound:\vSharedNoise\f "),
        new(1, "Sound: SharedNoise\u00a0Bone"),
        new(1, "   "),
        new(1, ""),
        new(2, "Sound: OutOfWindowNoise\nend"),
        new(2.1f, "Sound: ExactMissingNoise\nSound: ExactNoise"),
        new(2.2f, "Sound:\nSound: ExactNoise")
    ];

    private static byte[] AnimationSoundBasePlugin() => Join(AnimationSoundPluginHeader(),
        AnimationSoundRecord(0x101, "SharedNoise", "fx/base-directory"),
        AnimationSoundRecord(0x102, "ExactNoise", "fx/exact.wav"),
        AnimationSoundRecord(0x103, "ExactMp3", "fx/exact.mp3"),
        AnimationSoundRecord(0x104, "DeletedNoise", "fx/obsolete.wav"),
        AnimationSoundRecord(0x105, "InvalidNoise", "fx/invalid.wav", flags: 0x8000),
        AnimationSoundRecord(0x106, "EmptyDir", "fx/empty"),
        AnimationSoundRecord(0x107, "OutOfWindowNoise", "fx/out.wav"),
        AnimationSoundRecord(0x108, "ExactMissingNoise", "fx/missing.wav"),
        AnimationSoundRecord(0x140, "AmbiguousNoise", "fx/exact.wav"),
        AnimationSoundRecord(0x141, "AmbiguousNoise", "fx/exact.mp3"));

    private static byte[] AnimationSoundOverridePlugin() => Join(AnimationSoundPluginHeader(Plugin),
        AnimationSoundRecord(0x101, "SharedNoise", "fx/variants", flags: 0x18),
        AnimationSoundRecord(0x104, "DeletedNoise", "fx/obsolete.wav", deleted: true));

    private static byte[] AnimationSoundPluginHeader(string? master = null)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        return master is null ? Record("TES4", 0, 0, Field("HEDR", header)) :
            Record("TES4", 0, 0, Field("HEDR", header), Field("MAST", Text(master)), Field("DATA", new byte[8]));
    }

    private static byte[] AnimationSoundRecord(uint id, string editor, string file, uint flags = 0, bool deleted = false)
    {
        var data = new byte[36]; data[0] = 1; data[1] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), flags);
        for (var index = 0; index < 5; index++)
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12 + index * 2), (short)(100 - 25 * index));
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), 100);
        return Record("SOUN", id, deleted ? FalloutPluginRecord.DeletedFlag : 0,
            Field("EDID", Text(editor)), Field("FNAM", Text(file)), Field("SNDD", data));
    }

    private static byte[] AnimationSoundNif(FalloutNifTextKey[] keys)
    {
        var strings = new[] { "authored-text-keys", "authored-sequence" }.Concat(keys.Select(key => key.Value)).ToArray();
        var blocks = new[]
        {
            (Type: "NiTextKeyExtraData", Payload: Bytes(writer =>
            {
                writer.Write(0); writer.Write((uint)keys.Length);
                for (var index = 0; index < keys.Length; index++) { writer.Write(keys[index].Time); writer.Write(index + 2); }
            })),
            (Type: "NiControllerSequence", Payload: Bytes(writer =>
            {
                writer.Write(1); writer.Write(0u); writer.Write(0u); writer.Write(1f); writer.Write(0);
                writer.Write(2u); writer.Write(1f); writer.Write(0f); writer.Write(1f);
                writer.Write(-1); writer.Write(1); writer.Write((ushort)0);
            }))
        };
        return Bytes(writer =>
        {
            void Sized(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(34u); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Length); foreach (var block in blocks) Sized(block.Type);
            for (var index = 0; index < blocks.Length; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Payload.Length);
            writer.Write((uint)strings.Length); writer.Write((uint)strings.Max(Encoding.UTF8.GetByteCount));
            foreach (var value in strings) Sized(value); writer.Write(0u);
            foreach (var block in blocks) writer.Write(block.Payload);
            writer.Write(1u); writer.Write(1);
        });
    }

    private static void AnimationSoundReaderRefusal(FalloutNifTextKey[] keys)
    {
        var nif = FalloutNifFile.Read(AnimationSoundNif(keys));
        try { _ = nif.ReadObject(0); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Malformed source key time was accepted or bypassed by the metadata owner.");
    }

    private static void AnimationSoundInput(string root, string logical, byte[] bytes)
    {
        var path = Path.Combine(root, logical.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
    }

    private static byte[] AnimationSoundWav(short sample)
    {
        var bytes = new byte[52]; "RIFF"u8.CopyTo(bytes); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 44);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8)); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 1); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 22050); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 44100);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), 2); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34), 16);
        "data"u8.CopyTo(bytes.AsSpan(36)); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 8);
        for (var index = 0; index < 4; index++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + index * 2), sample);
        return bytes;
    }

    private static void AnimationSoundArchive(string path, byte[] payload)
    {
        var folder = Encoding.ASCII.GetBytes("sound\\fx\\variants\0"); var name = Encoding.ASCII.GetBytes("b.wav\0");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(0x00415342u); writer.Write(104u); writer.Write(36u); writer.Write(3u);
        writer.Write(1u); writer.Write(1u); writer.Write((uint)folder.Length); writer.Write((uint)name.Length); writer.Write(16u);
        writer.Write(0ul); writer.Write(1u); writer.Write((uint)(52 + name.Length));
        writer.Write((byte)folder.Length); writer.Write(folder);
        writer.Write(0ul); writer.Write((uint)payload.Length); writer.Write((uint)(52 + 1 + folder.Length + 16 + name.Length));
        writer.Write(name); writer.Write(payload);
    }
}
