using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

var source = new FalloutSoundRecord(new("Synthetic.esm", 0x123), "AuthoredNoise", "sound\\fx\\variants", 0,
    2, 4, 10, FalloutSoundFlags.RandomFrequencyShift | FalloutSoundFlags.EnvironmentIgnored,
    837, 0, 0, new short[] { 100, 50, 20, 5, 0 }, 100, 128, 0, 0);
Require(FalloutAnimationSound.EditorId(" sound: AuthoredNoise ") == "AuthoredNoise" &&
    FalloutAnimationSound.EditorId("StartLoop") is null, "Source sound text keys were not parsed independently of phase markers.");
Reject(() => FalloutAnimationSound.EditorId("Sound: "));
var variants = FalloutAnimationSound.Variants(source, new[] { "sound/fx/variants/b.wav", "sound/fx/variants/a.wav",
    "sound/fx/variants/A.wav", "sound/fx/variants/nested/skip.wav", "sound/fx/variants/skip.dds", "sound/fx/other/c.wav" });
Require(variants.SequenceEqual(new[] { "sound\\fx\\variants\\a.wav", "sound\\fx\\variants\\b.wav" }), "Winning immediate-directory variants were not canonical and stable.");
var first = new FalloutSoundRandomState(123); var second = new FalloutSoundRandomState(123);
var choices = Enumerable.Range(0, 64).Select(_ => FalloutAnimationSound.Select(source, variants, first)).ToArray();
var replay = Enumerable.Range(0, 64).Select(_ => FalloutAnimationSound.Select(source, variants, second)).ToArray();
Require(choices.Zip(replay).All(pair => pair.First.Path == pair.Second.Path && pair.First.PitchScale == pair.Second.PitchScale) && first.State == second.State,
    "Variants and pitch were not reproducible from authoritative random state.");
Require(choices.All(value => value.Play && value.GainDb == -8.37f && value.PitchScale is >= .9f and <= 1.1f && value.Unbound.Count == 0) &&
    choices.Any(value => value.PitchScale < 1) && choices.Any(value => value.PitchScale > 1), "Source variance, attenuation sign or bounds were lost.");
var fixedSound = source with { FrequencyAdjustment = -5, Flags = FalloutSoundFlags.EnvironmentIgnored };
var noRoll = first.State; var fixedSelection = FalloutAnimationSound.Select(fixedSound, variants.Take(1).ToArray(), first);
Require(first.State == noRoll && fixedSelection.PitchScale == .95f, "Fixed-pitch exact variants consumed random state.");
Require(FalloutAnimationSound.Select(source with { Flags = FalloutSoundFlags.MuteWhenSubmerged, ReverbAttenuation = 80 }, variants, first)
    .Unbound.SequenceEqual(new[] { "source-environment-reverb-send", "authoritative-listener-submersion" }), "Unsupported wet/submerged lanes were hidden.");
Reject(() => FalloutAnimationSound.Select(source with { Flags = FalloutSoundFlags.Loop }, variants, first));
Console.WriteLine("OPENNV_ANIMATION_SOUND_CONTRACT_PASS sourceEvent=true variants=true savedRandom=true signedPitchVariance=true attenuationIsLoss=true partialLanesVisible=true");
if (args.Length == 0) return;
if (args.Length != 1) throw new ArgumentException("Optional argument: owned FalloutNV installation.");
RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
using var content = RuntimeLiveContentSource.Current!;
using var records = FalloutPluginStack.Load(content.PluginSources);
const string clipPath = "meshes/characters/_male/idleanims/SmokingStanding.kf";
if (!content.TryRead(clipPath, null, out var bytes, out var identity)) throw new FileNotFoundException(clipPath);
var nif = FalloutNifFile.Read(bytes);
var sequence = nif.Blocks.Where(block => block.TypeName == "NiControllerSequence").Select(block => nif.ReadControllerSequence(block.Index)).Single();
var keys = ((FalloutNifTextKeyExtraData)nif.ReadObject(sequence.TextKeys)).Keys.Select(key => (key.Time, key.Value)).ToArray();
var events = new List<string>();
var phase = new FalloutIdleAnimationPlayback(sequence.StartTime, sequence.StopTime, sequence.Frequency, sequence.CycleType, keys, 1);
void Cross(FalloutIdleAnimationInterval interval)
{
    foreach (var key in keys.Where(key => key.Time <= interval.To && (key.Time > interval.From || interval.IncludeFrom && key.Time == interval.From)))
        foreach (var text in key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            if (FalloutAnimationSound.EditorId(text) is { } editorId)
            {
                var record = records.EffectiveRecords("SOUN").Single(record => record.ReadSubrecords().Any(row => row.Signature == "EDID" &&
                    Encoding.ASCII.GetString(row.Data.Span).TrimEnd('\0') == editorId));
                var descriptor = FalloutSoundRecordReader.Read(record);
                var files = FalloutAnimationSound.Variants(descriptor, content.ResourcePathsUnder(descriptor.LogicalPath));
                foreach (var file in files) Require(content.TryRead(file, null, out _, out _), "A source sound variant did not resolve.");
                var selected = FalloutAnimationSound.Select(descriptor, files, first);
                Require(selected.Play && selected.PitchScale is >= .98f and <= 1.02f && selected.GainDb == -11.01f, "Owned smoking sound selection fields differ.");
                events.Add(editorId);
            }
}
phase.Advance(100, Cross);
Require(events.SequenceEqual(new[] { "NPCIdleSmokingIn", "NPCIdleSmokingOut", "NPCIdleSmokingIn", "NPCIdleSmokingOut" }),
    "Intro/repeat/outro traversal duplicated, omitted or reordered source sound events.");
Console.WriteLine($"OPENNV_ANIMATION_SOUND_OWNED_PASS source={identity} orderedEvents={events.Count} originalWav=true sourceVariance=true reverb=unbound submersion=unbound visualSmoke=unproven");
static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
static void Reject(Action action) { try { action(); } catch (NotSupportedException) { return; } throw new InvalidDataException("Unsupported event behavior did not fail closed."); }
