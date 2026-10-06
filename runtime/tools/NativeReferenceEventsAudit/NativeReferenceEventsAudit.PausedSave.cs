using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task<object> ProvePausedFiniteSave(FalloutPluginStack records, FalloutReferenceWorld world,
        IReadOnlyList<Node> voices, string compatibility, bool alreadyPaused, bool inspectSessionMenu = false,
        string? expectedDisposition = null, Action<RuntimeNativeManualSavePreparation>? afterBegin = null,
        Func<string?>? invalidation = null, string? independentBlocker = null,
        ulong maximumWaitMilliseconds = RuntimeManualSavePreparation.MaximumWaitMilliseconds,
        bool orderedSource = false, bool failSourceWriter = false)
    {
        var directory = Path.Combine(".audit-artifacts", "native-save-preparation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var tree = GetTree(); var originalPause = tree.Paused; var originalMouse = Input.MouseMode;
        var requests = new RuntimeManualSaveRequests(); var session = Guid.NewGuid();
        RuntimeNativeManualSavePreparation? transaction = null;
        RuntimeNativePlayer? player = null;
        NativeSaveProducerAuditClock? producer = null;
        NativeGameSessionMenu? menu = null;
        NativeManualSaveStatus? status = null;
        var writes = 0; var sourceWrites = 0; var finishedWhilePaused = 0; var settled = false;
        var writerOrder = new List<string>();
        var failureReports = new List<string>();
        (long Process, long Physics, double Elapsed, long ChildProcess, long ChildPhysics, int SourceInvocations)? settledClocks = null;
        var observedFinished = new List<(Node Voice, Action Handler)>();
        try
        {
            tree.Paused = false;
            var startup = Stopwatch.StartNew();
            IReadOnlyList<FalloutFiniteSoundVoice>? expected = null;
            while (startup.Elapsed.TotalSeconds < 5)
            {
                expected = world.PendingAnimationSoundFiniteVoiceWait();
                if (expected?.Count == voices.Count && voices.All(NativeFinitePlaying)) break;
                await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            Require(expected is { Count: > 0 } && expected.Count == voices.Count && voices.All(NativeFinitePlaying),
                "Paused-save fixture lacks original observed playing source/native generations.");
            var proven = expected ?? throw new InvalidDataException("Missing proven native finite set.");
            var history = proven.Select(proof => world.Get(proof.Reference).AnimationSoundEvents.Events
                .Single(entry => entry.Generation == proof.Generation).Copy()).ToArray();
            var random = proven.DistinctBy(proof => proof.Reference).ToDictionary(proof => proof.Reference,
                proof => world.Get(proof.Reference).SoundRandom.State);
            var clock = new NativeSaveProducerAuditClock { ProcessMode = ProcessModeEnum.Always }; producer = clock; AddChild(clock);
            var childClock = new NativeSaveProducerAuditClock { ProcessMode = ProcessModeEnum.Always }; clock.AddChild(childClock);
            var sourceEvents = new FalloutScriptEvents();
            var sourceInvocations = 0;
            double SourceInvoke(FalloutFormKey script, FalloutFormKey? caller, IReadOnlyList<double> arguments, double seconds)
            { sourceInvocations++; return 0; }
            var sourceScript = new FalloutFormKey("Synthetic.esm", 0x500);
            var sourcePlayer = new FalloutFormKey("Synthetic.esm", 0x14);
            sourceEvents.SetMainLoop(sourceScript, sourcePlayer, true);
            sourceEvents.SetKey(sourceScript, sourcePlayer, true, true, 63);
            sourceEvents.SetKey(sourceScript, sourcePlayer, true, false, 63);
            var sourceInput = new RuntimeNativeScriptEvents(sourceEvents, () => SourceInvoke) { Active = true }; clock.AddChild(sourceInput);
            var nativePlayer = new RuntimeNativePlayer(); player = nativePlayer;
            nativePlayer.SetProcess(false); nativePlayer.SetPhysicsProcess(false);
            nativePlayer.SetProcessInput(false); nativePlayer.SetProcessUnhandledInput(false);
            AddChild(nativePlayer);
            nativePlayer.SetProcess(false); nativePlayer.SetPhysicsProcess(false);
            nativePlayer.SetProcessInput(false); nativePlayer.SetProcessUnhandledInput(false);
            nativePlayer.SetModalInput(alreadyPaused);
            nativePlayer.Velocity = new(1, 2, 3);
            var velocity = nativePlayer.Velocity;
            var priorModal = nativePlayer.ModalInput;
            await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Require(clock.ProcessTicks > 0 && clock.PhysicsTicks > 0 && clock.Elapsed > 0 &&
                childClock.ProcessTicks > 0 && childClock.PhysicsTicks > 0,
                "Native source/producer fixture never advanced before explicit save preparation.");
            tree.Paused = alreadyPaused; Input.MouseMode = alreadyPaused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Hidden;
            var priorMouse = Input.MouseMode;
            var flags = voices.Select(voice => (Voice: voice, Mode: voice.ProcessMode,
                Paused: voice is AudioStreamPlayer3D spatial ? spatial.StreamPaused : ((AudioStreamPlayer)voice).StreamPaused)).ToArray();
            var priorProcess = clock.ProcessTicks; var priorPhysics = clock.PhysicsTicks; var priorClock = clock.Elapsed;
            var priorChildProcess = childClock.ProcessTicks; var priorChildPhysics = childClock.PhysicsTicks;
            sourceEvents.Key(63, true, SourceInvoke);
            var priorSourceInvocations = sourceInvocations;
            var priorSourceState = JsonSerializer.Serialize(sourceEvents.State);
            foreach (var voice in voices)
            {
                void Finished()
                {
                    Require(tree.Paused && nativePlayer.ModalInput, "Finite mixer Finished escaped the explicit gameplay/input save pause.");
                    finishedWhilePaused++;
                }
                if (voice is AudioStreamPlayer3D spatial) spatial.Finished += Finished;
                else ((AudioStreamPlayer)voice).Finished += Finished;
                observedFinished.Add((voice, Finished));
            }
            const string schema = "opennv-native-reference-save-preparation-audit/v1";
            var canonical = Path.Combine(directory, "continue.json");
            File.WriteAllText(canonical, JsonSerializer.Serialize(new
            { Schema = schema, SaveCompatibilityId = compatibility, References = Array.Empty<FalloutReferenceSnapshot>(), priorContinue = true }));
            var previous = File.ReadAllBytes(canonical);
            var catalog = new RuntimeSaveSlotCatalog(canonical, root =>
            {
                Require(root.GetProperty("Schema").GetString() == schema &&
                    root.GetProperty("SaveCompatibilityId").GetString() == compatibility &&
                    root.GetProperty("References").ValueKind == JsonValueKind.Array,
                    "Native component slot lacks its complete reference-world source identity.");
            });
            RuntimeManualSaveAdmission Admission()
            {
                if (independentBlocker is not null) return new(RuntimeManualSaveAdmissionKind.Refused, independentBlocker);
                if (orderedSource)
                {
                    var sourceBoundary = transaction?.SourceOrder is { } order
                        ? order.ObserveAdmission(world.ScriptManualSaves, requests.Receipt!, Engine.GetProcessFrames())
                        : RuntimeManualSaveSourceBoundary.Observe(world.ScriptManualSaves);
                    if (sourceBoundary.Kind == RuntimeManualSaveAdmissionKind.Refused) return sourceBoundary;
                }
                if (world.PendingAnimationSoundCaptureCount == 0) return new(RuntimeManualSaveAdmissionKind.Ready);
                var pending = world.PendingAnimationSoundFiniteVoiceWait();
                return pending is { Count: > 0 } ? new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: pending) :
                    new(RuntimeManualSaveAdmissionKind.Refused, "animation-sound-continuation");
            }
            RuntimeSaveSlotMetadata CaptureComplete(Guid id)
            {
                Require(tree.Paused && nativePlayer.ModalInput && clock.ProcessTicks == priorProcess &&
                    clock.PhysicsTicks == priorPhysics && clock.Elapsed == priorClock &&
                    childClock.ProcessTicks == priorChildProcess && childClock.PhysicsTicks == priorChildPhysics &&
                    sourceInvocations == priorSourceInvocations && JsonSerializer.Serialize(sourceEvents.State) == priorSourceState &&
                    world.PendingAnimationSoundCaptureCount == 0 && proven.All(proof =>
                        world.Get(proof.Reference).AnimationSoundEvents.Events.Single(entry => entry.Generation == proof.Generation)
                            .End == FalloutAnimationSoundEnd.NativeFinished),
                    "Writer ran before genuine Finished or replayed paused source/world producers.");
                return catalog.Create(id, () => File.WriteAllText(canonical, JsonSerializer.Serialize(new
                { Schema = schema, SaveCompatibilityId = compatibility, References = world.Capture() })));
            }
            RuntimeSaveSlotMetadata Write(Guid id)
            {
                writes++; writerOrder.Add("ordinary-manual");
                return CaptureComplete(id);
            }
            FalloutScriptManualSaveReceipt? originalSource = null;
            if (orderedSource)
            {
                world.ScriptManualSaves.Bind(id =>
                {
                    Require(transaction?.PermitsOriginalSourceDrain() == true && world.ScriptManualSaves.WritingRequestedSlot &&
                        writes == 0 && sourceWrites == 0 && finishedWhilePaused == voices.Count,
                        "Native original ForceSave ran outside its exact quiescent lease or before real Finished.");
                    world.ScriptManualSaves.RequireCapture();
                    sourceWrites++; writerOrder.Add("original-source");
                    if (failSourceWriter) throw new IOException("Actual native original ForceSave writer failure.");
                    return CaptureComplete(id);
                }, _ => { }, Engine.GetProcessFrames);
                var program = FalloutGameModeProgram.Read("begin GameMode\nForceSave\nend");
                foreach (var step in world.ScriptManualSaves.Execute(Key(0x901), records.GetEffective(Key(0x500)), program,
                    program.Steps(_ => 0, (_, _) => { }, (_, _) => world.ScriptManualSaves.Request(program.LastStatement)))) { }
                originalSource = world.ScriptManualSaves.Receipt!;
                Require(originalSource is { Disposition: "pending", Invocations: [{ Ended: true, SourceError: null }] },
                    "Native ordered fixture has no genuinely retired original requesting invocation.");
            }
            void Publish(RuntimeManualSaveReceipt receipt)
            {
                menu?.ShowManualSaveReceipt(receipt); status?.ShowReceipt(receipt);
            }
            RuntimeManualSaveReceipt Start()
            {
                requests.Request(session, compatibility, Engine.GetProcessFrames(),
                    alreadyPaused ? RuntimeManualSaveOrigin.SessionMenu : RuntimeManualSaveOrigin.PlayerInput);
                transaction = new(requests, session, compatibility, nativePlayer, records.SoundVoices,
                    invalidation ?? (() => null), Admission, Write, Publish, () =>
                    {
                        settledClocks = (clock.ProcessTicks, clock.PhysicsTicks, clock.Elapsed,
                            childClock.ProcessTicks, childClock.PhysicsTicks, sourceInvocations);
                        settled = true;
                    },
                    maximumWaitMilliseconds, sourceProducers: [clock], reportFailure: error => failureReports.Add(error.Message),
                    sourceRequests: orderedSource ? world.ScriptManualSaves : null,
                    originalSourceBlocker: () => transaction?.PermitsOriginalSourceDrain() == true ? null : "paused");
                AddChild(transaction); transaction.Begin();
                return requests.Receipt!;
            }
            void Cancel() => transaction?.Cancel("Actual audit user cancellation; prior Continue retained.");
            if (inspectSessionMenu)
            {
                Require(alreadyPaused && RuntimeLiveContentSource.Current is not null, "Actual save menu audit needs its owned font and paused native context.");
                menu = new(catalog, true, true, false, () => { }, Start, Cancel, _ => { }, () => { }, () => { }, slot => slot.Id);
                AddChild(menu);
                var create = menu.FindChildren("CreateSave", "", true, false).OfType<Button>().Single();
                create.EmitSignal(BaseButton.SignalName.Pressed);
                Require(requests.Pending && menu.FindChildren("*", "", true, false).OfType<Button>()
                    .Where(button => button.Name != "CancelSave").All(button => button.Disabled) &&
                    menu.FindChildren("CancelSave", "", true, false).OfType<Button>().Single() is { Visible: true, Disabled: false },
                    "Actual create-save callback did not expose saving/cancel/busy while its finite native set drains.");
            }
            else
            {
                status = new(Cancel, () => { }); AddChild(status);
                _ = Start();
            }
            if (requests.Pending)
            {
                Require(tree.Paused && nativePlayer.ModalInput && nativePlayer.Velocity == velocity && writes == 0 &&
                    File.ReadAllBytes(canonical).SequenceEqual(previous) &&
                    requests.Receipt!.Preparation is { } prepared && prepared.Voices.Count == proven.Count &&
                    prepared.Voices.ToHashSet().SetEquals(proven) &&
                    !RuntimeManualSaveFeedback.Describe(requests.Receipt).StartsWith("New save committed.", StringComparison.Ordinal),
                    "Native save preparation changed gameplay/Continue or reported creation before a complete slot: " +
                    JsonSerializer.Serialize(new
                    {
                        tree.Paused,
                        nativePlayer.ModalInput,
                        velocity = nativePlayer.Velocity,
                        expectedVelocity = velocity,
                        writes,
                        sourceWrites,
                        priorContinueUnchanged = File.ReadAllBytes(canonical).SequenceEqual(previous),
                        prepared = requests.Receipt!.Preparation?.Voices,
                        expected = proven,
                        feedback = RuntimeManualSaveFeedback.Describe(requests.Receipt)
                    }));
                Reject(() => world.Capture());
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Godot.Key.F5, Keycode = Godot.Key.F5, Pressed = false });
                var releaseLimit = Time.GetTicksMsec() + 2000;
                while (sourceEvents.IsKeyPressed(63) && Time.GetTicksMsec() < releaseLimit)
                    await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                Require(!sourceEvents.IsKeyPressed(63) && sourceInvocations == priorSourceInvocations,
                    "Actual save-pause input lost a real key release or invoked a source callback.");
                afterBegin?.Invoke(transaction!);
            }
            var timer = Stopwatch.StartNew();
            while (requests.Pending && timer.Elapsed.TotalSeconds < 65)
                await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Require(!requests.Pending && settled && requests.Receipt!.Disposition == (expectedDisposition ?? "completed"),
                "Native save preparation did not settle with its actual expected receipt: " + JsonSerializer.Serialize(requests.Receipt));
            Require(tree.Paused == alreadyPaused && nativePlayer.ModalInput == priorModal && nativePlayer.Velocity == velocity &&
                Input.MouseMode == priorMouse && settledClocks is { } frozen &&
                frozen.Process == priorProcess && frozen.Physics == priorPhysics && frozen.Elapsed == priorClock &&
                frozen.ChildProcess == priorChildProcess && frozen.ChildPhysics == priorChildPhysics &&
                clock.ProcessMode == ProcessModeEnum.Always && childClock.ProcessMode == ProcessModeEnum.Always &&
                sourceInput.ProcessMode == ProcessModeEnum.Always && !sourceInput.SavePreparationPaused &&
                frozen.SourceInvocations == priorSourceInvocations &&
                flags.All(flag => !GodotObject.IsInstanceValid(flag.Voice) ||
                    flag.Voice.ProcessMode == flag.Mode &&
                    (flag.Voice is AudioStreamPlayer3D spatial ? spatial.StreamPaused : ((AudioStreamPlayer)flag.Voice).StreamPaused) == flag.Paused),
                "Save completion/failure/cancellation failed exact restoration: " + JsonSerializer.Serialize(new
                {
                    tree.Paused,
                    alreadyPaused,
                    nativePlayer.ModalInput,
                    priorModal,
                    velocity = nativePlayer.Velocity,
                    priorVelocity = velocity,
                    mouse = Input.MouseMode,
                    priorMouse,
                    process = clock.ProcessTicks,
                    priorProcess,
                    physics = clock.PhysicsTicks,
                    priorPhysics,
                    elapsed = clock.Elapsed,
                    priorClock,
                    childProcess = childClock.ProcessTicks,
                    priorChildProcess,
                    childPhysics = childClock.PhysicsTicks,
                    priorChildPhysics,
                    mode = clock.ProcessMode,
                    childMode = childClock.ProcessMode,
                    inputMode = sourceInput.ProcessMode,
                    sourceInput.SavePreparationPaused,
                    sourceInvocations,
                    priorSourceInvocations,
                    sourceStateUnchanged = JsonSerializer.Serialize(sourceEvents.State) == priorSourceState
                }));
            if (expectedDisposition is null)
            {
                Require(writes == 1 && finishedWhilePaused == voices.Count &&
                    catalog.ReadSlots().Count == (orderedSource ? 2 : 1) &&
                    catalog.ReadSlots().Any(slot => slot.Id == requests.Receipt!.Slot.ToString("N")) &&
                    random.All(pair => world.Get(pair.Key).SoundRandom.State == pair.Value) &&
                    records.SoundVoices.ActiveVoices == 0, "Paused native save did not commit exactly one cold-capable slot from its genuine Finished set.");
                for (var index = 0; index < proven.Count; index++)
                {
                    var proof = proven[index];
                    var ended = world.Get(proof.Reference).AnimationSoundEvents.Events.Single(entry => entry.Generation == proof.Generation);
                    Require(JsonSerializer.Serialize(ended with { End = FalloutAnimationSoundEnd.Active }) == JsonSerializer.Serialize(history[index]),
                        "Save preparation changed original source/media/hash/generation/random history.");
                }
                using var cold = new FalloutReferenceWorld(records);
                using var saved = JsonDocument.Parse(File.ReadAllBytes(requests.Receipt!.CommittedSlot!.Path));
                cold.Restore(saved.RootElement.GetProperty("References").Deserialize<FalloutReferenceSnapshot[]>()!);
                Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(world.Capture()) &&
                    cold.PendingAnimationSoundFiniteVoiceWait() is null && requests.Receipt!.AwaitedVoices is { } awaited &&
                    awaited.Count == proven.Count && awaited.ToHashSet().SetEquals(proven),
                    "Cold completed native reference capture recreated audio, producer work or different source receipts.");
                if (orderedSource)
                {
                    var completed = world.ScriptManualSaves.Receipt!;
                    Require(sourceWrites == 1 && writerOrder.SequenceEqual(["original-source", "ordinary-manual"]) &&
                        completed.Disposition == "completed" && completed.Slot == originalSource!.Slot &&
                        requests.Receipt!.OrderedSourceSave?.Slot == completed.Slot &&
                        requests.Receipt.OrderedSourceSave!.Disposition == "completed" &&
                        requests.Receipt.Slot != completed.Slot && File.Exists(completed.SlotPath) &&
                        JsonSerializer.Serialize(completed with { Disposition = "pending", SlotPath = null }) == JsonSerializer.Serialize(originalSource),
                        "Native ordered preparation changed source GUID/hash/sites/ended history or failed two once-in-order slots.");
                    using var sourceSaved = JsonDocument.Parse(File.ReadAllBytes(completed.SlotPath!));
                    using var sourceCold = new FalloutReferenceWorld(records);
                    sourceCold.Restore(sourceSaved.RootElement.GetProperty("References").Deserialize<FalloutReferenceSnapshot[]>()!);
                    Require(JsonSerializer.Serialize(sourceCold.Capture()) == JsonSerializer.Serialize(cold.Capture()) &&
                        !world.ScriptManualSaves.Drain(() => throw new InvalidDataException("Completed native source replayed.")),
                        "Original native source slot failed cold capture or replayed its writer.");
                }
                if (menu is not null)
                    Require(menu.FindChildren("CreateSave", "", true, false).OfType<Button>().Single().Disabled == false &&
                        menu.FindChildren("*", "", true, false).OfType<Label>().Any(label => label.Text.StartsWith("New save committed.", StringComparison.Ordinal)),
                        "Actual save browser retained busy or failed to refresh committed-slot feedback.");
                Require(!requests.DrainPrepared(session, compatibility, Engine.GetProcessFrames() + 1, Time.GetTicksMsec(),
                    () => throw new InvalidDataException("Completed native lease replayed."), Admission, Write) && writes == 1,
                    "Completed native request invoked its writer twice.");
            }
            else Require(writes == 0 && catalog.ReadSlots().Count == 0 && File.ReadAllBytes(canonical).SequenceEqual(previous) &&
                requests.Receipt!.CommittedSlot is null && requests.Receipt.Error is not null,
                "Refused/cancelled native save changed the prior Continue save, hid its cause or published a slot.");
            if (orderedSource && expectedDisposition is not null)
            {
                var actual = world.ScriptManualSaves.Receipt!;
                Require(actual.Slot == originalSource!.Slot && actual.Sites.SequenceEqual(originalSource.Sites) &&
                    (actual.Invocations ?? []).SequenceEqual(originalSource.Invocations ?? []) &&
                    sourceWrites == (failSourceWriter ? 1 : 0) &&
                    actual.Disposition == (failSourceWriter ? "failed" : "pending") &&
                    requests.Receipt!.OrderedSourceSave?.Disposition == actual.Disposition,
                    "Native ordered source failure/cancellation changed original pending/failed source history.");
                if (failSourceWriter)
                    Require(!world.ScriptManualSaves.Drain(() => throw new InvalidDataException("Failed native source writer retried.")),
                        "Native source failure replayed its original writer.");
            }
            return new
            {
                alreadyPaused,
                actualMenuCallback = inspectSessionMenu,
                receipt = requests.Receipt,
                writes,
                sourceWrites,
                writerOrder,
                orderedSource,
                finishedWhilePaused,
                producerReplay = false,
                modesRestored = true,
                coldNoReplay = expectedDisposition is null,
                priorContinueRetainedOnFailure = expectedDisposition is not null,
                recording = false,
                boundary = "shared-manual-save-transaction-and-complete-reference-world-component;ordinary-campaign-save-and-retail-independent"
            };
        }
        finally
        {
            try { if (requests.Pending) transaction?.Cancel("Native component cleanup before commit."); }
            finally
            {
                tree.Paused = originalPause; Input.MouseMode = originalMouse;
                foreach (var (voice, handler) in observedFinished)
                {
                    if (!GodotObject.IsInstanceValid(voice)) continue;
                    if (voice is AudioStreamPlayer3D spatial) spatial.Finished -= handler;
                    else ((AudioStreamPlayer)voice).Finished -= handler;
                }
                if (transaction is not null && GodotObject.IsInstanceValid(transaction)) transaction.Free();
                if (menu is not null && GodotObject.IsInstanceValid(menu)) menu.Free();
                if (status is not null && GodotObject.IsInstanceValid(status)) status.Free();
                if (producer is not null && GodotObject.IsInstanceValid(producer)) producer.Free();
                if (player is not null && GodotObject.IsInstanceValid(player)) player.Free();
                Directory.Delete(directory, true);
            }
        }
    }
}

internal sealed partial class NativeSaveProducerAuditClock : Node
{
    internal long ProcessTicks, PhysicsTicks;
    internal double Elapsed;
    public override void _Process(double delta) { ProcessTicks++; Elapsed += delta; }
    public override void _PhysicsProcess(double delta) => PhysicsTicks++;
}
