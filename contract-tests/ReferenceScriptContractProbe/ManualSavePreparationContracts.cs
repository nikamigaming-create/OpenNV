using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ManualSavePreparationContracts
{
    private const string Schema = "opennv-reference-save-preparation-contract/v1";

    internal static void Run(FalloutPluginStack records)
    {
        var directory = Path.Combine(".audit-artifacts", "manual-save-preparation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var session = Guid.NewGuid(); const string source = "synthetic-source-stack";
            var reference = new FalloutFormKey("Pose.esm", 0x901);
            var sound = new FalloutFormKey("Pose.esm", 0x68);
            using var world = new FalloutReferenceWorld(records);
            var state = world.Get(reference);
            var canonical = Path.Combine(directory, "continue.json");
            File.WriteAllText(canonical, Encode(source, world.Capture()));
            var previous = File.ReadAllBytes(canonical);
            var catalog = new RuntimeSaveSlotCatalog(canonical, root => Validate(root, source));
            var descriptor = FalloutSoundRecordReader.Read(records, sound);
            var selected = FalloutAnimationSound.Select(descriptor, [descriptor.LogicalPath], state.SoundRandom, true, true);
            var generation = state.AnimationSoundEvents.Begin(records, selected, "Sound: FixturePartial", true, [descriptor.LogicalPath]);
            state.AnimationSoundEvents.BindMedia(generation, new string('a', 64));
            var entry = state.AnimationSoundEvents.Events.Single();
            var voice = new FalloutFiniteSoundVoice(124, reference, generation, sound, entry.SoundSha256, entry.Path!, entry.MediaSha256!);
            var random = state.SoundRandom.State;
            var queue = new RuntimeManualSaveRequests();
            ulong phase = 10;
            world.ScriptManualSaves.BindSelection(new(source, Path.GetFullPath(canonical),
                id => Path.Combine(Path.GetFullPath(canonical) + RuntimeSaveSlotCatalog.SlotDirectorySuffix, id.ToString("N") + ".json")), () => phase);
            queue.Bind(world.ScriptManualSaves, session, source);
            var request = queue.Request(session, source, phase, RuntimeManualSaveOrigin.SessionMenu,
                new(session, 1, records.RuntimeFormKey(0x14), state.Cell));
            using var sourceOrder = new RuntimeManualSaveSourceOrder(world.ScriptManualSaves, request, phase);
            queue.Prepare(10, 1000, Finite(voice), [voice]);
            Require(queue.Source == world.ScriptManualSaves && queue.Receipt!.Origin == RuntimeManualSaveOrigin.SessionMenu,
                "Prepared menu input lost its actual common source queue.");
            var writes = 0;
            RuntimeSaveSlotMetadata Write(Guid id)
            {
                writes++;
                sourceOrder.DrainBeforeManual(queue.Receipt!, phase, () => null);
                return sourceOrder.WriteManual(queue.Receipt!, phase,
                    slot => catalog.Create(slot, () => File.WriteAllText(canonical, Encode(source, world.Capture()))));
            }
            Require(!queue.DrainPrepared(session, source, 10, 1000,
                () => throw new InvalidDataException("Input-phase native inspection ran."),
                () => throw new InvalidDataException("Input-phase capture admission ran."), Write) && writes == 0,
                "Save preparation wrote within its input phase.");
            phase = 11;
            Require(!queue.DrainPrepared(session, source, phase, 1010, () => [voice], () => Finite(voice), Write) &&
                queue.Pending && writes == 0 && File.ReadAllBytes(canonical).SequenceEqual(previous) &&
                !RuntimeManualSaveFeedback.Describe(queue.Receipt!).Contains("committed.", StringComparison.Ordinal),
                "Finite save preparation reported creation, changed Continue or wrote active audio.");
            Reject(() => world.Capture());
            state.AnimationSoundEvents.Complete(generation, FalloutAnimationSoundEnd.NativeFinished);
            phase = 12;
            Require(queue.DrainPrepared(session, source, phase, 1020, () => [], () => new(RuntimeManualSaveAdmissionKind.Ready), Write) &&
                writes == 1 && queue.Receipt!.Preparation?.Phase == RuntimeManualSavePreparationPhase.Completed &&
                queue.Receipt.AwaitedVoices?.Single() == voice && catalog.ReadSlots().Single().Id == request.Slot.ToString("N") &&
                RuntimeManualSaveFeedback.Describe(queue.Receipt).StartsWith("New save committed.", StringComparison.Ordinal),
                "Genuine completion did not commit exactly one shared complete slot and receipt.");
            phase = 13;
            Require(!queue.DrainPrepared(session, source, phase, 1030, () => throw new InvalidDataException("Native lease replayed."),
                () => throw new InvalidDataException("Admission replayed."), Write) && writes == 1,
                "Completed prepared save replayed its writer.");
            using (var cold = new FalloutReferenceWorld(records))
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(queue.Receipt!.CommittedSlot!.Path));
                cold.Restore(document.RootElement.GetProperty("References").Deserialize<FalloutReferenceSnapshot[]>()!);
                Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(world.Capture()) &&
                    state.SoundRandom.State == random && state.AnimationSoundEvents.Events.Count == 1,
                    "Complete cold capture replayed finite selection, producer generations or source RNG.");
            }
            QueueFailures(voice);
            RegistryLeases(records, voice);
            CompletionPauseBoundary(voice);
            PausedSourceInput();
            ManualSaveSourceOrderingContracts.Run(directory);
            CatalogRollback(directory, canonical, catalog, source);
            var fabricated = queue.History.First(receipt => receipt.Disposition == "completed") with
            { CommittedSlot = new(request.Slot.ToString("N"), Path.Combine(directory, "missing.json"), Schema, null, null, null, DateTime.UtcNow) };
            Reject(() => RuntimeManualSaveFeedback.Describe(fabricated));
            Require(queue.History is [{ Disposition: "completed" }] && writes == 1,
                "Independent failure cases rewrote the complete source request or repeated its writer.");
            Console.WriteLine("OPENNV_MANUAL_SAVE_PREPARATION_CONTRACT_PASS f5MenuOneOwner=true inputPhase=false fixedFiniteSet=true nativeFinishedOnly=true boundedFailure=true sourceNativeGenerationCancel=true unknownLoopRefused=true producerRngRetained=true writerOnce=true cold=true priorContinueRollback=true visibleReceipt=true campaignAndParity=unverified");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void QueueFailures(FalloutFiniteSoundVoice voice)
    {
        var writes = 0;
        RuntimeSaveSlotMetadata NoWrite(Guid id)
        {
            ++writes;
            throw new InvalidDataException("Refused prepared state reached its writer.");
        }
        static RuntimeManualSaveRequests Request(CompiledManualSaveFixture fixture)
        {
            var queue = new RuntimeManualSaveRequests();
            fixture.BindManual(queue);
            queue.Request(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, site: fixture.Site(1));
            return queue;
        }
        using (var fixture = new CompiledManualSaveFixture([], phase: 20))
        {
            var queue = Request(fixture); queue.Prepare(fixture.Phase, 2000, Finite(voice), [voice], 20);
            fixture.AdvancePhase();
            Require(!queue.DrainPrepared(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, 2021,
                () => [voice], () => Finite(voice), NoWrite) &&
                queue.Receipt!.Disposition == "failed" && queue.Receipt.Error!.Contains("bounded", StringComparison.Ordinal),
                "A finite voice waited forever or elapsed time supplied Finished.");
        }
        foreach (var blocker in new[] { "unknown-audio", "looping-audio", "cancelled-audio", "opaque-audio",
            "actor-procedure", "movement", "conversation", "source-continuation", "missing-native-Finished" })
        {
            using var fixture = new CompiledManualSaveFixture([], phase: 30);
            var queue = Request(fixture); queue.Prepare(fixture.Phase, 3000, Finite(voice), [voice]);
            fixture.AdvancePhase();
            Require(!queue.DrainPrepared(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, 3010, () => [voice],
                () => new(RuntimeManualSaveAdmissionKind.Refused, blocker), NoWrite) &&
                queue.Receipt!.Disposition == "failed" && queue.Receipt.Error == blocker &&
                queue.Receipt.AwaitedVoices?.Single() == voice, "An independent save owner was waived: " + blocker);
        }
        foreach (var changed in new[] { voice with { Generation = voice.Generation + 1 },
            voice with { NativeOwner = voice.NativeOwner + 1 }, voice with { MediaSha256 = new string('b', 64) },
            voice with { SoundSha256 = new string('c', 64) } })
        {
            using var fixture = new CompiledManualSaveFixture([], phase: 40);
            var queue = Request(fixture); queue.Prepare(fixture.Phase, 4000, Finite(voice), [voice]);
            fixture.AdvancePhase();
            Require(!queue.DrainPrepared(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, 4010,
                () => [changed], () => Finite(changed), NoWrite) &&
                queue.Receipt!.Disposition == "cancelled", "Save preparation migrated to changed source/native/media/generation.");
        }
        using (var fixture = new CompiledManualSaveFixture([], phase: 50))
        {
            var queue = Request(fixture); queue.Prepare(fixture.Phase, 5000, Finite(voice), [voice]);
            fixture.AdvancePhase();
            Require(!queue.DrainPrepared(Guid.NewGuid(), fixture.Binding.SourceCompatibilityId, fixture.Phase, 5010,
                () => throw new InvalidDataException("Retired native lease queried."), () => Finite(voice), NoWrite) &&
                queue.Receipt!.Disposition == "cancelled", "Save preparation migrated across reload/session generation.");
        }
        using (var fixture = new CompiledManualSaveFixture([], phase: 60))
        {
            var queue = Request(fixture); queue.Prepare(fixture.Phase, 6000, Finite(voice), [voice]);
            queue.Cancel("Actual user cancellation.");
            Require(queue.Receipt!.Disposition == "cancelled" && queue.Receipt.Preparation?.Phase == RuntimeManualSavePreparationPhase.Cancelled,
                "Cancellation lost the actual prepared request.");
        }
        using (var fixture = new CompiledManualSaveFixture([], phase: 70))
        {
            var queue = Request(fixture); queue.Prepare(fixture.Phase, 7000, Finite(voice), [voice]);
            fixture.AdvancePhase();
            Require(!queue.DrainPrepared(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, 7010,
                () => [], () => Finite(voice), NoWrite) &&
                queue.Receipt!.Disposition == "failed", "A missing native generation was assumed finished from admission alone.");
        }
        Reject(() => new RuntimeManualSavePreparation(1, 1, Finite(voice), [voice, voice]));
        Reject(() => new RuntimeManualSavePreparation(1, 1, new(RuntimeManualSaveAdmissionKind.Ready), [voice]));
        Require(writes == 0, "A refused prepared request invoked the writer.");
    }

    private static void RegistryLeases(FalloutPluginStack records, FalloutFiniteSoundVoice voice)
    {
        var registry = new FalloutSoundVoices(records);
        var active = true; var leases = new List<Lease>();
        using var registration = registry.Register(voice.Sound, voice.Reference, "contract-finite", () => active,
            () => throw new InvalidDataException("Save preparation supplied source Stop."),
            sourceReference: voice.Reference, finiteWait: () => active ? voice : null, prepareSaveDrain: () =>
            {
                var lease = new Lease(voice, () => !active); leases.Add(lease); return lease;
            });
        using (var drain = registry.PrepareFiniteSaveDrain())
        {
            Reject(() => registry.PrepareFiniteSaveDrain());
            drain.Activate();
            Require(drain.ObservePending().Single() == voice && leases.Single().Active, "Save preparation failed its original finite registry binding.");
            active = false; registration.Dispose();
            Require(drain.ObservePending().Count == 0, "Genuine completion retained a live registry lease.");
        }
        Require(leases.Single().Disposed && !leases.Single().Active, "Completed drain failed exact lease cleanup.");
        active = true;
        using var rebound = registry.Register(voice.Sound, voice.Reference, "contract-finite-retry", () => active, () => { },
            sourceReference: voice.Reference, finiteWait: () => voice, prepareSaveDrain: () => new Lease(voice, () => !active));
        using (var drain = registry.PrepareFiniteSaveDrain())
        {
            drain.Activate();
            using var changed = registry.Register(voice.Sound, voice.Reference, "new-generation", () => true, () => { });
            Reject(() => drain.ObservePending());
        }
        var partial = new Lease(voice, () => false);
        var failed = new FalloutSoundVoices(records);
        using var first = failed.Register(voice.Sound, voice.Reference, "proven-first", () => true, () => { },
            sourceReference: voice.Reference, finiteWait: () => voice, prepareSaveDrain: () => partial);
        using var unknown = failed.Register(voice.Sound, voice.Reference, "opaque-next", () => true, () => { });
        Reject(() => failed.PrepareFiniteSaveDrain());
        Require(partial.Disposed && !partial.Active, "Partial audio preparation leaked or activated a lease after an opaque refusal.");
        var loop = new FalloutFormKey("Pose.esm", 0x6a);
        var loopProof = voice with
        {
            Sound = loop, SoundSha256 = Convert.ToHexString(SHA256.HashData(records.GetEffective(loop).ReadData())),
            Path = "sound\\fixture\\loop.wav"
        };
        var looping = new FalloutSoundVoices(records);
        using var loopRegistration = looping.Register(loop, voice.Reference, "source-loop", () => true, () => { },
            sourceReference: voice.Reference, finiteWait: () => loopProof, prepareSaveDrain: () => new Lease(loopProof, () => false));
        Reject(() => looping.PrepareFiniteSaveDrain());
    }

    private static void CompletionPauseBoundary(FalloutFiniteSoundVoice voice)
    {
        var wait = new FalloutFiniteSoundCompletionWait(voice, 10, 20, 30);
        Require(wait.ObserveSavePreparation(voice, 10, 20, 30, false, true, 1, 100) is null,
            "Unobserved native startup became a finite save lease.");
        Require(wait.Observe(voice, 10, 20, 30, true, 2, 110) == voice &&
            wait.ObserveSavePreparation(voice, 10, 20, 30, false, true, 30, 5000) == voice &&
            wait.Phase == "native-playing", "A tree-pause notification invented a mixer completion window.");
        Require(wait.ObserveSavePreparation(voice, 10, 20, 30, false, false, 31, 5010) == voice &&
            wait.ObserveSavePreparation(voice, 10, 20, 30, false, false, 34, 5011) is null && wait.Error is not null,
            "Missing native Finished was not refused after its genuine bounded dispatch window.");
    }

    private static void CatalogRollback(string directory, string canonical, RuntimeSaveSlotCatalog catalog, string source)
    {
        var before = File.ReadAllBytes(canonical); var slots = catalog.ReadSlots().Count;
        var writtenUtc = File.GetLastWriteTimeUtc(canonical);
        Reject(() => catalog.Create(Guid.NewGuid(), () => { File.WriteAllText(canonical, "invalid"); throw new IOException("Actual writer failure."); }));
        Require(File.ReadAllBytes(canonical).SequenceEqual(before) && catalog.ReadSlots().Count == slots,
            "Writer failure replaced the prior complete Continue save or published a slot.");
        Reject(() => catalog.Create(Guid.NewGuid(), () => File.WriteAllText(canonical, "{}")));
        Require(File.ReadAllBytes(canonical).SequenceEqual(before) && catalog.ReadSlots().Count == slots,
            "Invalid candidate replaced Continue before its complete slot committed.");
        var validations = 0;
        var readFailure = new RuntimeSaveSlotCatalog(canonical, root =>
        {
            Validate(root, source);
            if (++validations == 2) throw new InvalidDataException("Actual committed-metadata validation failure.");
        });
        Reject(() => readFailure.Create(Guid.NewGuid(), () => File.WriteAllText(canonical, Encode(source, []))));
        Require(File.ReadAllBytes(canonical).SequenceEqual(before) && File.GetLastWriteTimeUtc(canonical) == writtenUtc && catalog.ReadSlots().Count == slots &&
            !Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories).Any(),
            "Post-write metadata failure left a new slot, changed Continue or leaked write scratch files.");
    }

    private static void PausedSourceInput()
    {
        var events = new FalloutScriptEvents();
        var script = new FalloutFormKey("Synthetic.esm", 0x500); var player = new FalloutFormKey("Synthetic.esm", 0x14);
        var calls = 0;
        double Invoke(FalloutFormKey source, FalloutFormKey? caller, IReadOnlyList<double> arguments, double seconds) { calls++; return 0; }
        events.SetKey(script, player, true, true, 63); events.SetKey(script, player, true, false, 63);
        events.Key(63, true, Invoke);
        var before = JsonSerializer.Serialize(events.State);
        events.ObserveSavePausedKey(63, false);
        events.ObserveSavePausedKey(63, true);
        events.ObserveSavePausedKey(63, false);
        Require(!events.IsKeyPressed(63) && calls == 1 && JsonSerializer.Serialize(events.State) == before,
            "Save pause lost genuine key releases, executed a source callback or replayed input effects.");
        events.Key(63, true, Invoke);
        Require(calls == 2 && events.IsKeyPressed(63), "Save pause did not preserve independent later source input.");
    }

    private sealed class Lease(FalloutFiniteSoundVoice voice, Func<bool> finished) : IFalloutFiniteSoundSaveDrainLease
    {
        public FalloutFiniteSoundVoice Voice { get; } = voice;
        internal bool Active, Disposed;
        public bool ObserveFinished() => finished();
        public void Activate() => Active = true;
        public void Dispose() { Active = false; Disposed = true; }
    }

    private static RuntimeManualSaveAdmission Finite(FalloutFiniteSoundVoice voice) => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: [voice]);
    private static string Encode(string source, IReadOnlyList<FalloutReferenceSnapshot> references) =>
        JsonSerializer.Serialize(new { Schema, SaveCompatibilityId = source, References = references });
    private static void Validate(JsonElement root, string source)
    {
        Require(root.GetProperty("Schema").GetString() == Schema && root.GetProperty("SaveCompatibilityId").GetString() == source &&
            root.GetProperty("References").ValueKind == JsonValueKind.Array, "Component capture lacks its strict source/world identity.");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or JsonException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Invalid save preparation/capture was admitted.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
