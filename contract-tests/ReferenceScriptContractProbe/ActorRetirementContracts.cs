using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ActorRetirementContracts
{
    private static FalloutFormKey Key(uint id) => new("Pose.esm", id);
    private static readonly float[] Pose = [1, 0, 0, 0, 1, 0, 0, 0, 1, 4, 5, 6];

    private sealed class Fixture : IDisposable
    {
        internal readonly FalloutReferenceWorld World;
        internal readonly FalloutReferenceInstance State;
        internal readonly List<IDisposable> Bindings = [];
        internal readonly List<FalloutFiniteSoundVoice> Voices = [];
        internal readonly FalloutActorSelectionFailure Selection;
        internal readonly FalloutActorPackageBindingFailure Binding;
        internal Fixture(FalloutPluginStack records, bool selection = true, int voices = 1, uint soundId = 0x68)
        {
            World = new(records); World.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800))); State = World.Get(Key(0x900));
            State.Animation.Change("meshes/fixture/mtidle.kf", new string('a', 64)); State.Animation.Advance(8.125);
            var retirement = new FalloutPackageRetirement(0, null, null, null);
            Selection = new(Key(0x25), FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(0x25))), 0,
                "Original stopped source predicate.", (float[])Pose.Clone(), 17, 2.125, new(4, 10, 2, 7.5f), true, retirement, null);
            Binding = new(Key(0x20), FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(0x20))),
                "Original stopped source package.", [4, 5, 6], [1, 0, 0, 0, 1, 0, 0, 0, 1], false, 17, 2.125, new(4, 10, 2, 7.5f), retirement);
            State.ProcedureCaptureBlocker = "Actual pre-retirement source blocker.";
            if (selection)
            {
                State.SelectionFailure = Selection with { Pose = [1, 0, 0, 0, 1, 0, 0, 0, 1, -7, -8, -9] };
                State.CanCaptureSelectionFailure = () => false;
            }
            else
            {
                State.PackageBindingFailure = Binding with { Position = [-7, -8, -9] };
                State.CanCapturePackageBindingFailure = () => false;
            }
            State.SoundRandom.Restore(19);
            var source = FalloutSoundRecordReader.Read(records, Key(soundId));
            for (var ordinal = 0; ordinal < voices; ordinal++)
            {
                var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], State.SoundRandom, true, true);
                var generation = State.AnimationSoundEvents.Begin(records, selected, "Sound: FixturePartial", true, [source.LogicalPath]);
                State.AnimationSoundEvents.BindMedia(generation, new string('b', 64));
                var entry = State.AnimationSoundEvents.Events.Last();
                var proof = new FalloutFiniteSoundVoice(73, State.Reference, generation, source.FormKey,
                    entry.SoundSha256, entry.Path!, entry.MediaSha256!); Voices.Add(proof);
                Bindings.Add(records.SoundVoices.Register(source.FormKey, State.Reference, "synthetic-retirement-finite",
                    () => State.AnimationSoundEvents.Events.Single(value => value.Generation == generation).End == FalloutAnimationSoundEnd.Active,
                    () => State.AnimationSoundEvents.Complete(generation, FalloutAnimationSoundEnd.SourceStopped),
                    () => State.AnimationSoundEvents.Cancel(generation, "Actual graph retired."), State.Reference,
                    () => State.AnimationSoundEvents.Events.Single(value => value.Generation == generation).End == FalloutAnimationSoundEnd.Active ? proof : null));
            }
        }
        internal FalloutActorRetirementCandidate Prepare(FalloutPluginStack records, bool selection = true) =>
            FalloutActorRetirementCandidate.Prepare(records, State, 73, selection ? Selection : null, selection ? null : Binding) ??
                throw new InvalidDataException("Fixture lost its exact finite native/source receipts.");
        internal void Finish(int index = 0)
        {
            State.AnimationSoundEvents.Complete(Voices[index].Generation, FalloutAnimationSoundEnd.NativeFinished);
            Bindings[index].Dispose();
        }
        public void Dispose() { foreach (var binding in Bindings) binding.Dispose(); World.Dispose(); }
    }

    internal static void Run(FalloutPluginStack records)
    {
        foreach (var selection in new[] { true, false })
        {
            using var fixture = new Fixture(records, selection, voices: 2);
            var state = fixture.State; var before = JsonSerializer.Serialize(selection ? (object)state.SelectionFailure! : state.PackageBindingFailure!);
            var sourceRandom = state.SoundRandom.State; var oldNativeReads = 0; var disposed = false;
            state.CaptureEngagement = () =>
            {
                if (disposed) throw new InvalidOperationException("Disposed model was read.");
                oldNativeReads++; return null;
            };
            var candidate = fixture.Prepare(records, selection);
            state.CaptureEngagement = null; disposed = true; candidate.Bind();
            fixture.Selection.Pose[9] = 900; fixture.Binding.Position[0] = 900;
            state.TalkedToPlayer = true;
            Require(fixture.World.PendingProcedureCaptureCount == 1 && fixture.World.PendingProcedureFiniteVoiceWait()?.Count == 2,
                "A retired finite wait admitted capture or lost exact native generations.");
            Reject(() => fixture.World.Capture());
            Require(JsonSerializer.Serialize(selection ? (object)state.SelectionFailure! : state.PackageBindingFailure!) == before,
                "Preparing a finite retirement prematurely published its nonaudio copy.");
            fixture.Finish(0);
            Require(fixture.World.PendingProcedureCaptureCount == 1 && fixture.World.PendingProcedureFiniteVoiceWait()?.Count == 1,
                "One Finished receipt waived another genuinely Active voice.");
            fixture.Finish(1);
            Require(fixture.World.PendingProcedureCaptureCount == 0 && state.StoppedRetirement == candidate,
                "Exact Finished receipts failed read-only capture admission or silently replaced the lease.");
            Require(JsonSerializer.Serialize(selection ? (object)state.SelectionFailure! : state.PackageBindingFailure!) == before,
                "A readiness observation committed a stopped source copy.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(fixture.World.Capture()))!;
            var actor = saved.Single(value => value.Reference == state.Reference);
            Require(oldNativeReads == 1 && actor.TalkedToPlayer && state.SoundRandom.State == sourceRandom &&
                (selection ? actor.SelectionFailure!.Pose[9] == 4 : actor.PackageBindingFailure!.Position[0] == 4),
                "Retired commit reread a disposed model, aliased a mutable copy or rewrote unrelated source state/RNG.");
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
            Require(cold.Get(state.Reference).StoppedRetirement is null && cold.Get(state.Reference).PendingPackageBindingFiniteVoices is null &&
                JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved) && records.SoundVoices.ActiveVoices == 0,
                "Cold retirement replayed playback/cursors or persisted a transient native lease.");
            Require(JsonSerializer.Serialize(fixture.World.Capture()) == JsonSerializer.Serialize(saved) && oldNativeReads == 1,
                "Repeated settled capture replayed the prefix or reread its disposed model.");
        }

        foreach (var end in new[] { FalloutAnimationSoundEnd.Cancelled, FalloutAnimationSoundEnd.SourceStopped, FalloutAnimationSoundEnd.Faulted })
        {
            using var fixture = new Fixture(records); fixture.Prepare(records).Bind();
            var id = fixture.Voices[0].Generation;
            if (end == FalloutAnimationSoundEnd.Cancelled) fixture.State.AnimationSoundEvents.Cancel(id, "Real retirement cancellation.");
            else if (end == FalloutAnimationSoundEnd.Faulted) fixture.State.AnimationSoundEvents.Fail(id, "Real source error.");
            else fixture.State.AnimationSoundEvents.Complete(id, end);
            fixture.Bindings[0].Dispose();
            Require(fixture.World.PendingProcedureCaptureCount == 1 && fixture.World.PendingProcedureFiniteVoiceWait() is null,
                "Cancelled, faulted or unrelated authored Stop became native Finished retirement.");
            Reject(() => fixture.World.Capture());
        }
        using (var fixture = new Fixture(records))
        {
            fixture.Prepare(records).Bind(); fixture.State.AnimationSoundEvents.Fail(null, "Opaque source emitter fault."); fixture.Finish();
            Require(fixture.World.PendingProcedureCaptureCount == 1, "Opaque source failure disappeared behind finite completion.");
            Reject(() => fixture.World.Capture());
        }
        foreach (var mutation in new Action<FalloutReferenceInstance>[]
        {
            state => state.Unconscious = true,
            state => state.Placement = new(state.Cell, [8, 9, 10], [0, 0, 0]),
            state => state.Animation.Advance(.25),
            state => state.SoundRandom.NextUnitFloat(),
            state => state.ProcedureCaptureBlocker = "Different source owner.",
            state => state.SelectionFailure = state.SelectionFailure! with { Pose = [1, 0, 0, 0, 1, 0, 0, 0, 1, 70, 80, 90] },
            state => state.StoppedRetirement = null,
            state => state.CanCaptureSelectionFailure = () => false,
            state => state.CaptureEngagement = () => throw new InvalidOperationException("New model callback must not be invoked."),
        })
        {
            using var fixture = new Fixture(records); fixture.Prepare(records).Bind(); mutation(fixture.State); fixture.Finish();
            Require(fixture.World.PendingProcedureCaptureCount == 1, "A changed state/native binding admitted the retired copy.");
            Reject(() => fixture.World.Capture());
        }
        using (var fixture = new Fixture(records, selection: false))
        {
            fixture.Prepare(records, selection: false).Bind();
            fixture.State.PackageBindingFailure = fixture.State.PackageBindingFailure! with { Position = [70, 80, 90] };
            fixture.Finish();
            Require(fixture.World.PendingProcedureCaptureCount == 1, "A changed stopped binding receipt admitted an old retirement copy.");
            Reject(() => fixture.World.Capture());
        }
        foreach (var blocker in new[] { "head", "hit", "physical", "moving", "loop" })
        {
            using var fixture = new Fixture(records, soundId: blocker == "loop" ? 0x6aU : 0x68U);
            if (blocker == "head") fixture.State.HeadTrackingCaptureBlocker = "Missing source head owner.";
            else if (blocker == "hit") fixture.State.HitReactionFaultCaptureBlocker = "Unowned hit suffix.";
            else if (blocker == "physical") fixture.State.KnockedDown = true;
            else if (blocker == "moving") fixture.State.Engagement = new(Key(0x902), "pursue");
            if (blocker == "loop")
                Require(FalloutActorRetirementCandidate.Prepare(records, fixture.State, 73, fixture.Selection, null) is null,
                    "A source loop became finite retirement admission.");
            else Reject(() => fixture.Prepare(records));
            Require(fixture.State.StoppedRetirement is null, "A refused independent owner partially published a retirement lease.");
        }
        using (var fixture = new Fixture(records))
        {
            Require(FalloutActorRetirementCandidate.Prepare(records, fixture.State, 74, fixture.Selection, null) is null,
                "A foreign native sound owner admitted retirement.");
            fixture.Bindings[0].Dispose();
            Require(FalloutActorRetirementCandidate.Prepare(records, fixture.State, 73, fixture.Selection, null) is null,
                "A missing native generation was guessed from its ledger.");
        }
        using (var fixture = new Fixture(records))
        {
            fixture.Prepare(records).Bind(); fixture.Finish();
            var source = FalloutSoundRecordReader.Read(records, Key(0x68));
            var selected = FalloutAnimationSound.Select(source, [source.LogicalPath], fixture.State.SoundRandom, true, true);
            fixture.State.AnimationSoundEvents.Begin(records, selected, "Sound: NewSourceGeneration", true, [source.LogicalPath]);
            Require(fixture.World.PendingProcedureCaptureCount == 1, "A new source generation was hidden by an old Finished receipt.");
            Reject(() => fixture.World.Capture());
        }
        Console.WriteLine("OPENNV_ACTOR_FINITE_RETIREMENT_CONTRACT_PASS nonaudioImmutable=true exactAllGenerations=true nativeFinishedOnly=true activeRefused=true noDisposedRead=true bindingReplacementRefused=true independentOwnersRefused=true coldNoLeaseOrReplay=true sourceFaultPreserved=true parity=unverified");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned or unsettled actor retirement was admitted.");
    }
}
