using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
const uint syntheticEnvironmentAreaMask = 1U;
var fixtureRoot = Path.Combine(Path.GetTempPath(), $"opennv-sound-runtime-{Guid.NewGuid():N}");
Directory.CreateDirectory(fixtureRoot);
try
{
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Master.esm"), Combine(
        Record("TES4", 0, []),
        Record("SOUN", 0x10, Sound("BaseSound", "fx\\base.wav", Data36(2, 3, 0x0040, -600))),
        Record("SOUN", 0x11, LegacySound()),
        Record("SOUN", 0x12, Sound("Exact3D", "fx\\exact3d.wav",
            Data36(2, 3, 0, -200, reverb: 100, loopStart: 0, loopEnd: 0))),
        Record("SOUN", 0x13, Sound("ExactEnvironment3D", "fx\\environment3d.wav",
            Data36(2, 3, 0, -200, reverb: 0, loopStart: 0, loopEnd: 0))),
        Record("SOUN", 0x14, Sound("PartialReverb3D", "fx\\partial3d.wav",
            Data36(2, 3, 0, -200, reverb: 50, loopStart: 0, loopEnd: 0))),
        Record("SOUN", 0x15, Sound("Chance3D", "fx\\chance3d.wav",
            Data36(2, 3, 0, -200, reverb: 100, loopStart: 0, loopEnd: 0), randomChance: 25)),
        Record("SOUN", 0x16, Sound("FixedFrequency3D", "fx\\fixed3d.wav",
            Data36(2, 3, 0, -200, reverb: 100, loopStart: 0, loopEnd: 0,
                frequencyAdjustment: 5))),
        Record("SNDR", 0x20, Subrecord("EDID", ZString("ForeignDescriptor"))),
        Record("SOUN", 0x30, Sound("BadReserved", "fx\\bad.wav", Data36(1, 2, 0, 0, badTail: true))),
        Record("SOUN", 0x31, Sound("BadFlags", "fx\\bad.wav", Data36(1, 2, 0x8000, 0))),
        Record("SOUN", 0x32, Sound("BadUpperFlags", "fx\\bad.wav", Data36(1, 2, 0x00010000, 0)))));
    File.WriteAllBytes(Path.Combine(fixtureRoot, "Patch.esp"), Combine(
        Record("TES4", 0, Subrecord("MAST", ZString("Master.esm"))),
        Record("SOUN", 0x10, Sound("WinningSound", "fx\\winner.ogg", Data36(4, 9, 0x0050, -125)))));

    using var stack = FalloutPluginStack.Load(fixtureRoot, ["Master.esm", "Patch.esp"]);
    var winner = FalloutSoundRecordReader.Read(stack, new FalloutFormKey("Master.esm", 0x10));
    Require(winner.EditorId == "WinningSound" && winner.LogicalPath == "sound\\fx\\winner.ogg",
        "Effective SOUN override/path resolution failed.");
    Require(winner.IsLooping && winner.IsTwoDimensional && winner.StaticAttenuationDb == -1.25f &&
        winner.MinimumDistanceGameUnits == 20.0f && winner.MaximumDistanceGameUnits == 900.0f &&
        winner.AttenuationCurve.SequenceEqual(new short[] { 100, 50, 20, 5, 0 }) && winner.Priority == 128 &&
        winner.LoopStartSample == 64 && winner.LoopEndSample == 128,
        "SNDD playback fields differ.");
    var legacy = FalloutSoundRecordReader.Read(stack, new FalloutFormKey("Master.esm", 0x11));
    Require(legacy.ReverbAttenuation == 80 && legacy.Priority == 7 &&
        legacy.AttenuationCurve.SequenceEqual(new short[] { 100, 50, 20, 5, 0 }),
        "Legacy SNDX/ANAM/GNAM/HNAM decoding failed.");
    var exact3D = FalloutSoundRecordReader.Read(stack, new FalloutFormKey("Master.esm", 0x12));
    FalloutSoundPlaybackContract.ValidateThreeDimensional(exact3D);
    var quarterDistance = exact3D.MinimumDistanceGameUnits +
        (exact3D.MaximumDistanceGameUnits - exact3D.MinimumDistanceGameUnits) * 0.25f;
    Require(MathF.Abs(exact3D.AttenuationDbAtDistanceGameUnits(quarterDistance) -
        20.0f * MathF.Log10(0.5f)) < 0.0001f &&
        float.IsNegativeInfinity(
            exact3D.AttenuationDbAtDistanceGameUnits(exact3D.MaximumDistanceGameUnits)),
        "3D five-point attenuation interpolation failed.");
    var exactEnvironment3D = FalloutSoundRecordReader.Read(
        stack, new FalloutFormKey("Master.esm", 0x13));
    Require(FalloutSoundPlaybackContract.RequiresEnvironmentReverb(exactEnvironment3D),
        "Zero-attenuation environment reverb was not admitted as a full source send.");
    FalloutSoundPlaybackContract.ValidateEnvironmentReverbAreaMask(
        exactEnvironment3D, syntheticEnvironmentAreaMask);
    ExpectFailure(
        () => FalloutSoundPlaybackContract.ValidateEnvironmentReverbAreaMask(exactEnvironment3D, 0U),
        "Area3D");
    ExpectFailure(
        () => FalloutSoundPlaybackContract.ValidateEnvironmentReverbAreaMask(
            exact3D, syntheticEnvironmentAreaMask),
        "dry or environment-ignored");
    var partialReverb3D = FalloutSoundRecordReader.Read(
        stack, new FalloutFormKey("Master.esm", 0x14));
    ExpectFailure(
        () => FalloutSoundPlaybackContract.ValidateThreeDimensional(partialReverb3D), "reverb");
    var chance3D = FalloutSoundRecordReader.Read(stack, new FalloutFormKey("Master.esm", 0x15));
    FalloutSoundPlaybackContract.ValidateThreeDimensional(chance3D);
    var randomA = new FalloutSoundRandomState(0x123456789abcdef0UL);
    var randomB = new FalloutSoundRandomState(0x123456789abcdef0UL);
    var chanceSequenceA = Enumerable.Range(0, 32)
        .Select(_ => FalloutSoundPlaybackContract.PassesRandomChance(chance3D, randomA)).ToArray();
    var chanceSequenceB = Enumerable.Range(0, 32)
        .Select(_ => FalloutSoundPlaybackContract.PassesRandomChance(chance3D, randomB)).ToArray();
    Require(chanceSequenceA.SequenceEqual(chanceSequenceB) && randomA.State == randomB.State &&
        chanceSequenceA.Any(value => value) && chanceSequenceA.Any(value => !value),
        "RNAM selection is not deterministic from saved gameplay RNG state.");
    var noChanceState = randomA.State;
    Require(FalloutSoundPlaybackContract.PassesRandomChance(exact3D, randomA) &&
        randomA.State == noChanceState,
        "A SOUN without RNAM chance consumed gameplay RNG state.");
    var fixedFrequency3D = FalloutSoundRecordReader.Read(
        stack, new FalloutFormKey("Master.esm", 0x16));
    FalloutSoundPlaybackContract.ValidateThreeDimensional(fixedFrequency3D);
    Require(MathF.Abs(fixedFrequency3D.FixedPitchScale - 1.05f) < 0.0001f,
        "Fixed SOUN frequency adjustment did not map to an exact pitch ratio.");
    ExpectFailure(() => FalloutSoundPlaybackContract.ValidateThreeDimensional(winner), "2D/menu");
    ExpectFailure(() => FalloutSoundRecordReader.Read(stack, new FalloutFormKey("Master.esm", 0x20)), "SNDR");
    Require(FalloutSoundRecordReader.Read(stack, new FalloutFormKey("Master.esm", 0x30)).LoopStartSample == 1,
        "SNDD loop start sample was not decoded.");
    ExpectFailure(() => FalloutSoundRecordReader.Read(stack, new FalloutFormKey("Master.esm", 0x31)), "unknown bits");
    ExpectFailure(() => FalloutSoundRecordReader.Read(stack, new FalloutFormKey("Master.esm", 0x32)), "unknown bits");
    Console.WriteLine(
        "OPENNV_FALLOUT_SOUND_SYNTHETIC_PASS effective=1 legacy=1 attenuation3D=1 environment3D=1 randomChance3D=1 fixedFrequency3D=1 loopPoints=1 failClosed=5");
}
finally
{
    Directory.Delete(fixtureRoot, recursive: true);
}

if (args.Length > 0)
{
    var root = args[0];
    var configured = false;
    FalloutPluginStack owned;
    if (File.Exists(root) && Path.GetExtension(root).Equals(".json", StringComparison.OrdinalIgnoreCase))
    {
        var manifestBytes = File.ReadAllBytes(root);
        using var document = JsonDocument.Parse(manifestBytes);
        var manifest = document.RootElement;
        RuntimeOwnedContentSource.Configure(
            manifest.GetProperty("roots")[0].GetProperty("root").GetString()!,
            Path.GetFullPath(root),
            Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
            manifest.GetProperty("stackId").GetString());
        configured = true;
        owned = FalloutPluginStack.Load(RuntimeOwnedContentSource.Current!.PluginSources);
    }
    else
    {
        var names = args.Length > 1 ? args[1..] : ["FalloutNV.esm"];
        owned = FalloutPluginStack.Load(root, names);
    }
    using (owned)
        try
        {
            var admitted = 0;
            var exactFiles = 0;
            var twoDimensional = 0;
            var boundedPlayback = 0;
            const FalloutSoundFlags boundedPlaybackFlags =
                FalloutSoundFlags.Loop | FalloutSoundFlags.MenuSound |
                FalloutSoundFlags.TwoDimensional | FalloutSoundFlags.DialogueSound;
            string? exactExample = null;
            var blockers = new Dictionary<string, int>(StringComparer.Ordinal);
            var blockerExamples = new List<string>();
            var exactThreeDimensional = 0;
            var boundedThreeDimensional = 0;
            var resolvedThreeDimensional = 0;
            var fullEnvironmentThreeDimensional = 0;
            var resolvedFullEnvironmentThreeDimensional = 0;
            var randomChanceThreeDimensional = 0;
            var resolvedRandomChanceThreeDimensional = 0;
            var randomChanceFieldDescriptors = 0;
            var resolvedRandomChanceFieldDescriptors = 0;
            var threeDimensionalFlagSets = new Dictionary<string, int>(StringComparer.Ordinal);
            var threeDimensionalCurves = new Dictionary<string, int>(StringComparer.Ordinal);
            var threeDimensionalReverb = new Dictionary<short, int>();
            var threeDimensionalRandomTraits = new Dictionary<string, int>(StringComparer.Ordinal);
            var threeDimensionalBehaviorTraits = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var record in owned.EffectiveRecords("SOUN"))
            {
                try
                {
                    var sound = FalloutSoundRecordReader.Read(record);
                    admitted++;
                    exactFiles += sound.HasExactFile ? 1 : 0;
                    twoDimensional += sound.IsTwoDimensional ? 1 : 0;
                    boundedPlayback += sound.HasExactFile && sound.IsTwoDimensional &&
                        (sound.Flags & ~boundedPlaybackFlags) == 0 && sound.RandomChancePercent == 0 &&
                        sound.FixedPitchScale > 0.0f && sound.StopTime == 0 && sound.StartTime == 0
                        ? 1 : 0;
                    if (sound.HasExactFile && exactExample is null)
                        exactExample = $"{sound.FormKey}:{sound.EditorId}:{sound.LogicalPath}";
                    if (sound.HasExactFile && !sound.IsTwoDimensional)
                    {
                        exactThreeDimensional++;
                        if (sound.RandomChancePercent != 0)
                        {
                            randomChanceFieldDescriptors++;
                            if (configured && RuntimeOwnedContentSource.Current!.TryRead(
                                    sound.LogicalPath, null, out _, out _))
                                resolvedRandomChanceFieldDescriptors++;
                        }
                        var flagsKey = $"0x{(ushort)sound.Flags:x4}";
                        threeDimensionalFlagSets[flagsKey] = threeDimensionalFlagSets.GetValueOrDefault(flagsKey) + 1;
                        var curveKey = string.Join('/', sound.AttenuationCurve);
                        threeDimensionalCurves[curveKey] = threeDimensionalCurves.GetValueOrDefault(curveKey) + 1;
                        threeDimensionalReverb[sound.ReverbAttenuation] =
                            threeDimensionalReverb.GetValueOrDefault(sound.ReverbAttenuation) + 1;
                        var behaviorTraits = new List<string>();
                        if (sound.FrequencyAdjustment != 0)
                            behaviorTraits.Add($"fixed-frequency-{sound.FrequencyAdjustment}");
                        if ((sound.Flags & FalloutSoundFlags.RandomFrequencyShift) != 0)
                            behaviorTraits.Add("random-frequency");
                        if ((sound.Flags & FalloutSoundFlags.EnvelopeFast) != 0)
                            behaviorTraits.Add("envelope-fast");
                        if ((sound.Flags & FalloutSoundFlags.EnvelopeSlow) != 0)
                            behaviorTraits.Add("envelope-slow");
                        if ((sound.Flags & FalloutSoundFlags.Lfe360) != 0)
                            behaviorTraits.Add("lfe-360");
                        if ((sound.Flags & FalloutSoundFlags.Radius2D) != 0)
                            behaviorTraits.Add("radius-2d");
                        if (behaviorTraits.Count != 0)
                        {
                            var behaviorKey = string.Join('+', behaviorTraits);
                            threeDimensionalBehaviorTraits[behaviorKey] =
                                threeDimensionalBehaviorTraits.GetValueOrDefault(behaviorKey) + 1;
                        }
                        try
                        {
                            FalloutSoundPlaybackContract.ValidateThreeDimensional(sound);
                            boundedThreeDimensional++;
                            var needsEnvironment =
                                FalloutSoundPlaybackContract.RequiresEnvironmentReverb(sound);
                            fullEnvironmentThreeDimensional += needsEnvironment ? 1 : 0;
                            var usesRandomChance = sound.RandomChancePercent != 0;
                            randomChanceThreeDimensional += usesRandomChance ? 1 : 0;
                            var resolved = configured && RuntimeOwnedContentSource.Current!.TryRead(
                                sound.LogicalPath, null, out _, out _);
                            if (resolved)
                            {
                                resolvedThreeDimensional++;
                                resolvedFullEnvironmentThreeDimensional += needsEnvironment ? 1 : 0;
                                resolvedRandomChanceThreeDimensional += usesRandomChance ? 1 : 0;
                            }
                        }
                        catch (NotSupportedException error)
                        {
                            var category = ThreeDimensionalBlocker(error.Message);
                            blockers[category] = blockers.GetValueOrDefault(category) + 1;
                            if (category == "3d-random")
                            {
                                var traits = new List<string>();
                                if (!sound.HasExactFile) traits.Add("folder");
                                if (sound.RandomChancePercent != 0) traits.Add($"chance-{sound.RandomChancePercent}");
                                if ((sound.Flags & FalloutSoundFlags.PlayAtRandom) != 0) traits.Add("play-at-random");
                                if ((sound.Flags & FalloutSoundFlags.RandomLocation) != 0) traits.Add("random-location");
                                if ((sound.Flags & FalloutSoundFlags.StartAtRandomPosition) != 0) traits.Add("random-start");
                                var traitKey = traits.Count == 0 ? "other" : string.Join('+', traits);
                                threeDimensionalRandomTraits[traitKey] =
                                    threeDimensionalRandomTraits.GetValueOrDefault(traitKey) + 1;
                            }
                        }
                    }
                }
                catch (IOException error)
                {
                    var category = error.Message.Contains("requires exactly one FNAM", StringComparison.Ordinal)
                        ? "missing-file"
                        : "other";
                    blockers[category] = blockers.GetValueOrDefault(category) + 1;
                    if (blockerExamples.Count < 40)
                    {
                        var rows = record.ReadSubrecords().ToArray();
                        var editor = rows.FirstOrDefault(row => row.Signature == "EDID").Data;
                        var data = rows.FirstOrDefault(row => row.Signature == "SNDD").Data;
                        blockerExamples.Add($"{record.FormKey}:{category}:" +
                            $"{(editor.IsEmpty ? "-" : Encoding.GetEncoding(1252).GetString(editor.Span).TrimEnd('\0'))}:" +
                            $"{(data.IsEmpty ? "-" : Convert.ToHexString(data.Span))}");
                    }
                }
            }
            var sndr = owned.EffectiveRecords("SNDR").Count;
            Console.WriteLine(
                $"OPENNV_FALLOUT_SOUND_OWNED_AUDIT_PASS plugins={owned.Plugins.Count} " +
                $"soun={owned.EffectiveRecords("SOUN").Count} admitted={admitted} exactFiles={exactFiles} " +
                $"twoDimensional={twoDimensional} boundedPlayback={boundedPlayback} sndr={sndr} " +
                $"blockers={string.Join(',', blockers.Select(row => $"{row.Key}:{row.Value}"))}");
            Console.WriteLine(
                $"OPENNV_FALLOUT_SOUND_3D_CORPUS exact={exactThreeDimensional} " +
                $"bounded={boundedThreeDimensional} resolved={resolvedThreeDimensional} " +
                $"fullEnvironment={fullEnvironmentThreeDimensional} " +
                $"fullEnvironmentResolved={resolvedFullEnvironmentThreeDimensional} " +
                $"randomChance={randomChanceThreeDimensional} " +
                $"randomChanceResolved={resolvedRandomChanceThreeDimensional} " +
                $"randomChanceFields={randomChanceFieldDescriptors} " +
                $"randomChanceFieldsResolved={resolvedRandomChanceFieldDescriptors} " +
                $"flags={string.Join(',', threeDimensionalFlagSets.OrderByDescending(row => row.Value).Select(row => $"{row.Key}:{row.Value}"))} " +
                $"curves={string.Join(',', threeDimensionalCurves.OrderByDescending(row => row.Value).Take(20).Select(row => $"{row.Key}:{row.Value}"))} " +
                $"reverb={string.Join(',', threeDimensionalReverb.OrderByDescending(row => row.Value).Select(row => $"{row.Key}:{row.Value}"))}");
            Console.WriteLine(
                $"OPENNV_FALLOUT_SOUND_3D_RANDOM traits={string.Join(',', threeDimensionalRandomTraits.OrderByDescending(row => row.Value).Select(row => $"{row.Key}:{row.Value}"))}");
            Console.WriteLine(
                $"OPENNV_FALLOUT_SOUND_3D_BEHAVIOR traits={string.Join(',', threeDimensionalBehaviorTraits.OrderByDescending(row => row.Value).Select(row => $"{row.Key}:{row.Value}"))}");
            Console.WriteLine($"OPENNV_FALLOUT_SOUND_EXACT_EXAMPLE {exactExample ?? "none"}");
            foreach (var example in blockerExamples)
                Console.WriteLine($"OPENNV_FALLOUT_SOUND_BLOCKER {example}");
        }
        finally
        {
            if (configured)
                RuntimeOwnedContentSource.Clear();
        }
}

static byte[] Sound(string editorId, string path, byte[] data, byte randomChance = 0)
{
    var rows = new List<byte[]>
    {
        Subrecord("EDID", ZString(editorId)),
        Subrecord("OBND", new byte[12]),
        Subrecord("FNAM", ZString(path)),
    };
    if (randomChance != 0)
        rows.Add(Subrecord("RNAM", [randomChance]));
    rows.Add(Subrecord("SNDD", data));
    return Combine(rows.ToArray());
}

static byte[] LegacySound()
{
    var data = new byte[12];
    data[0] = 2;
    data[1] = 3;
    var reverb = new byte[2];
    BinaryPrimitives.WriteInt16LittleEndian(reverb, 80);
    var priority = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(priority, 7);
    return Combine(
        Subrecord("EDID", ZString("LegacySound")),
        Subrecord("FNAM", ZString("fx\\legacy.wav")),
        Subrecord("SNDX", data),
        Subrecord("ANAM", Curve()),
        Subrecord("GNAM", reverb),
        Subrecord("HNAM", priority));
}

static byte[] Data36(
    byte minimum,
    byte maximum,
    uint flags,
    short attenuation,
    bool badTail = false,
    short reverb = 80,
    uint loopStart = 64,
    uint loopEnd = 128,
    sbyte frequencyAdjustment = 0)
{
    var data = new byte[36];
    data[0] = minimum;
    data[1] = maximum;
    data[2] = unchecked((byte)frequencyAdjustment);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), flags);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), attenuation);
    Curve().CopyTo(data.AsSpan(12));
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), reverb);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24), 128);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), badTail ? 1u : loopStart);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), loopEnd);
    return data;
}

static byte[] Curve()
{
    var data = new byte[10];
    var values = new short[] { 100, 50, 20, 5, 0 };
    for (var index = 0; index < values.Length; ++index)
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(index * 2), values[index]);
    return data;
}

static byte[] Record(string signature, uint formId, byte[] data)
{
    var header = new byte[24];
    Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)data.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), formId);
    return Combine(header, data);
}

static byte[] Subrecord(string signature, byte[] data)
{
    var header = new byte[6];
    Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)data.Length));
    return Combine(header, data);
}

static byte[] ZString(string value) => Encoding.GetEncoding(1252).GetBytes(value).Append((byte)0).ToArray();
static byte[] Combine(params byte[][] values) => values.SelectMany(value => value).ToArray();
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void ExpectFailure(Action action, string fragment)
{
    try { action(); }
    catch (Exception error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new InvalidOperationException($"Expected failure containing {fragment}.");
}

static string ThreeDimensionalBlocker(string message)
{
    if (message.Contains("reverb", StringComparison.OrdinalIgnoreCase)) return "3d-reverb";
    if (message.Contains("frequency", StringComparison.OrdinalIgnoreCase)) return "3d-frequency";
    if (message.Contains("random", StringComparison.OrdinalIgnoreCase)) return "3d-random";
    if (message.Contains("envelope", StringComparison.OrdinalIgnoreCase)) return "3d-envelope";
    if (message.Contains("loop", StringComparison.OrdinalIgnoreCase)) return "3d-loop";
    if (message.Contains("attenuation", StringComparison.OrdinalIgnoreCase)) return "3d-attenuation";
    return "3d-flags";
}
