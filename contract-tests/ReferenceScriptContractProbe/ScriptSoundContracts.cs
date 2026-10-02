using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ScriptSoundContracts
{
    private sealed class Voice
    {
        internal int Starts, Releases;
        internal bool Paused;
        internal FalloutScriptSoundPlayback Prepare(FalloutScriptSoundRequest request) => new(
            new(request.Selection.Path!, "synthetic-owned-stream", new string('a', 64), 1),
            () => Starts++, value => Paused = value, () => Releases++);
    }

    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-script-sounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string source = "short done\nref sound\nbegin GameMode\nif done == 0\nlet sound := SourceSound\n" +
                "PlaySound sound -1\nStopSound sound\nset done to 1\nendif\nend";
            var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2); header[16] = 1;
            File.WriteAllBytes(Path.Combine(directory, "Sounds.esm"), Join(Record("TES4", 0),
                Sound(0x100, "SourceSound", "fx/base.wav", 0x40), Sound(0x101, "LoopSound", "fx/loop.wav", 0x10),
                Sound(0x102, "TimedSound", "fx/timed.wav", 0, start: 1, stop: 2),
                Sound(0x103, "CompressedSound", "fx/ambient.mp3", 0), Sound(0x104, "EnvironmentSound", "fx/dry.wav", 0xc0, reverb: 0),
                Sound(0x105, "FolderSound", "fx/variants/", 1, frequency: 10),
                Sound(0x106, "BadMediaSound", "fx/bad.wav", 1, frequency: 10),
                Sound(0x107, "AliasSound", "fx/winner.wav", 0),
                Record("REFR", 0x200, Field("EDID", Text("EmitterA"))),
                Record("REFR", 0x201, Field("EDID", Text("EmitterB"))),
                Record("SCPT", 0x601, Field("SCHR", header), Local(1, "done"), Local(2, "sound"),
                    Join(Enumerable.Range(0x100, 8).Concat([0x200, 0x201]).Select(id => Field("SCRO", BitConverter.GetBytes((uint)id))).ToArray()), Field("SCTX", Text(source))),
                Record("QUST", 0x600, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x601u)))));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Record("TES4", 0, Field("MAST", Text("Sounds.esm"))),
                Sound(0x100, "SourceSound", "fx/winner.wav", 0, attenuation: 200, frequency: 10)));
            using var records = FalloutPluginStack.Load(directory, ["Sounds.esm", "Patch.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var sounds = world.Sounds;
            var voices = new Dictionary<long, Voice>();
            using var binding = sounds.Bind(Variants, request =>
            {
                if (request.Selection.Path!.EndsWith("bad.wav", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Synthetic decode failure.");
                var voice = new Voice(); voices.Add(request.Id, voice); return voice.Prepare(request);
            }, stereo: true);
            var caller = Key(0x600);
            sounds.Play(caller, Key(0x100)); sounds.Play(caller, Key(0x100));
            Require(sounds.LastRequest is { Source.LogicalPath: "sound\\fx\\winner.wav", Source.StaticAttenuationHundredthsDb: 200 } &&
                Math.Abs(sounds.LastRequest.Selection.PitchScale - 1.1f) < .00001 && voices.Values.All(voice => voice.Starts == 1),
                "PlaySound ignored the winner, source gain/pitch or non-locational routing.");
            world.Menus.Publish(false, [1013]); sounds.Update(false);
            sounds.Play(caller, Key(0x100)); var queued = voices[sounds.LastRequest!.Id];
            sounds.Play(caller, Key(0x100), true); var system = voices[sounds.LastRequest!.Id]; sounds.Update(false);
            Require(queued.Starts == 0 && system.Starts == 1 && !system.Paused && voices[1].Paused && voices[2].Paused,
                "Menu queuing or normal/system voice pause policy diverged.");
            world.Menus.Publish(true); sounds.Update(true);
            Require(queued.Starts == 1 && !voices[1].Paused && voices[1].Starts == 1 && system.Starts == 1,
                "GameMode did not resume queued voices once or restarted existing audio.");
            sounds.Complete(1); sounds.Complete(1);
            Require(voices[1].Releases == 1 && sounds.ActiveVoices == 3, "Duplicate completion lost concurrent voice ownership.");
            foreach (var invalid in new[] { 0x102u, 0x103u, 0x600u }) Reject(() => sounds.Play(caller, Key(invalid)));
            var before = sounds.LastRequest;
            Reject(() => sounds.Play(caller, Key(0x106)));
            Require(ReferenceEquals(before, sounds.LastRequest) && sounds.ActiveVoices == 3, "Decode failure committed a request or lost existing audio.");
            sounds.Play(caller, Key(0x105));
            Require(sounds.LastRequest!.Selection.RandomBefore == before!.Selection.RandomAfter &&
                sounds.LastRequest.Selection.Path is "sound\\fx\\variants\\a.wav" or "sound\\fx\\variants\\b.wav",
                "Failed preparation consumed random state or selected a foreign/nested variant.");
            sounds.Play(caller, Key(0x104));
            Require(sounds.LastRequest!.Source.Flags == (FalloutSoundFlags)0xc0 &&
                sounds.LastRequest.Selection.Unbound.Contains("source-environment-reverb-send") &&
                sounds.LastRequest.Selection.Unbound.Contains("source-lfe-send-not-rendered-on-stereo-output"),
                "Partial audio presentation hid source reverb or LFE divergence.");

            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new InvalidDataException("Sound command invented a presentation effect.")));
            var quest = records.GetEffective(caller); var script = records.GetEffective(Key(0x601));
            void Execute(string body) => executor.ExecuteProgram(quest, script, FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            Execute("PlaySound SourceSound\nset done to 7");
            Require(quests.Variable(caller, 1) == 7 && sounds.LastRequest!.Caller == caller, "Reference/result execution lost its sound prefix or suffix.");
            Reject(() => Execute("PlaySound TimedSound\nset done to 99"));
            Require(quests.Variable(caller, 1) == 7, "Unsupported scheduling executed the source suffix.");
            sounds.Play(caller, Key(0x101)); var loopId = sounds.LastRequest!.Id;
            Execute("StopSound LoopSound\nset done to 8");
            Require(!sounds.IsActive(loopId) && voices[loopId].Releases == 1 && quests.Variable(caller, 1) == 8,
                "Source loop did not stop or its command suffix failed.");
            var stoppedA = 0; var stoppedB = 0; var stoppedFlat = 0; var stoppedAlias = 0;
            using var a = records.SoundVoices.Register(Key(0x100), Key(0x200), "animation", () => true, () => ++stoppedA);
            using var b = records.SoundVoices.Register(Key(0x100), Key(0x201), "reference-node", () => true, () => ++stoppedB);
            using var flat = records.SoundVoices.Register(Key(0x100), null, "paused-menu", () => false, () => ++stoppedFlat);
            using var alias = records.SoundVoices.Register(Key(0x107), Key(0x200), "same-path-other-SOUN", () => true, () => ++stoppedAlias);
            using var ended = records.SoundVoices.Register(Key(0x100), Key(0x200), "completed", () => false,
                () => throw new InvalidDataException("Completed instance was stopped."));
            ended.Dispose();
            using var retired = records.SoundVoices.Register(Key(0x100), Key(0x200), "retired", () => true,
                () => throw new InvalidDataException("Retired instance was stopped."));
            retired.Dispose();
            Execute("StopSound SourceSound EmitterA");
            Require(stoppedA == 1 && stoppedB == 0 && stoppedFlat == 0 && stoppedAlias == 0 && sounds.IsActive(2),
                "Reference filter confused the caller, a 2D instance, a completed instance or equal media paths.");
            Execute("set done to 1 + (StopSound SourceSound 0)");
            Require(stoppedA == 1 && stoppedB == 1 && stoppedFlat == 1 && stoppedAlias == 0 &&
                !sounds.IsActive(2) && quests.Variable(caller, 1) == 1,
                "Global stop missed another owner, replayed an instance or returned a count instead of zero.");
            Reject(() => Execute("StopSound SourceSound EmitterA EmitterB\nset done to 99"));
            Reject(() => Execute("StopSound SourceSound SourceSound\nset done to 99"));
            Reject(() => Execute("StopSound EmitterA\nset done to 99"));
            Require(quests.Variable(caller, 1) == 1 && stoppedAlias == 0, "Invalid stop arguments ran the suffix or stopped another SOUN.");
            records.SoundVoices.Stop(new("SOUNDS.ESM", 0x107));
            Require(stoppedAlias == 1, "StopSound lost canonical case-insensitive plugin identity.");
            ended.Dispose();
            quests.SetVariable(caller, 1, 0);
            var fallback = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0, references: world);
            fallback.Advance(0);
            Require(ReferenceEquals(fallback.Sounds, sounds) && fallback.Capture().Instances.Single().Error is null &&
                quests.Variable(caller, 1) == 1 && sounds.LastRequest!.SystemSound, "Fallback typed form/flag execution bypassed shared sounds.");
            var saved = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(fallback.Capture()))!;
            sounds.Play(caller, Key(0x100)); var afterSave = sounds.LastRequest!.Id;
            using var coldWorld = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture());
            var cold = new FalloutQuestScripts(records, coldQuests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(), defaultProcessingDelay: 0, references: coldWorld);
            cold.Restore(saved); cold.Advance(0);
            Require(cold.Sounds.LastRequest is null && cold.Sounds.ActiveVoices == 0 && cold.Capture().Instances.Single().Error is null && sounds.IsActive(afterSave),
                "Cold restoration replayed a completed command prefix or inherited transient voices.");
            binding.Dispose();
            Require(sounds.ActiveVoices == 0 && voices.Values.All(voice => voice.Releases == 1), "World/presentation retirement leaked owned voices.");
            Reject(() => sounds.Play(caller, Key(0x100)));
            Reject(() => FalloutScriptSounds.SystemFlag(.5));
            using var surround = sounds.Bind(Variants, request => new Voice().Prepare(request), stereo: false);
            Reject(() => sounds.Play(caller, Key(0x104)));
            Require(FalloutSoundRecordReader.Read(records, Key(0x100)).Flags == 0, "Playback mutated the owned sound declaration.");
            StopFailure(records);
            Console.WriteLine("OPENNV_SCRIPT_SOUND_CONTRACT_PASS winning=true typedSourceCommands=true fallback=true queue=true systemSound=true concurrent=true prefixFailure=true randomAtomic=true completion=true retirement=true coldNoReplay=true audioDivergenceVisible=true loops=true stopSound=true referenceFilter=true crossOwner=true zeroResult=true parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static void StopFailure(FalloutPluginStack records)
    {
        var registry = new FalloutSoundVoices(records); var first = 0; var failing = 0; var suffix = 0;
        using var a = registry.Register(Key(0x100), null, "prefix", () => true, () => ++first);
        using var b = registry.Register(Key(0x100), null, "fault", () => true, () => { ++failing; throw new InvalidDataException("Synthetic stop failure."); });
        using var c = registry.Register(Key(0x100), null, "suffix", () => true, () => ++suffix);
        Reject(() => registry.Stop(Key(0x100)));
        try { registry.Stop(Key(0x100)); } catch (InvalidOperationException) { }
        Require(first == 1 && failing == 1 && suffix == 0 && registry.Error == "Synthetic stop failure." && registry.ActiveVoices == 2,
            "Failed stop lost its applied prefix, replayed a callback or hid the unresolved suffix.");
    }

    private static IReadOnlyList<string> Variants(FalloutSoundRecord descriptor) => FalloutAnimationSound.Variants(descriptor,
        ["sound/fx/variants/b.wav", "sound/fx/variants/a.wav", "sound/fx/variants/sub/foreign.wav", "sound/fx/variants/readme.txt"]);
    private static FalloutFormKey Key(uint id) => new("Sounds.esm", id);
    private static byte[] Sound(uint id, string name, string path, uint flags, short attenuation = 0,
        sbyte frequency = 0, byte start = 0, byte stop = 0, short reverb = 100)
    {
        var data = new byte[36]; data[2] = unchecked((byte)frequency);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), flags); BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), attenuation);
        data[10] = stop; data[11] = start; BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(22), reverb);
        return Record("SOUN", id, Field("EDID", Text(name)), Field("FNAM", Text(path)), Field("SNDD", data));
    }
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported script sound behavior was accepted.");
    }
}
