using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task OwnedActorFiniteRetirement(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutReferenceWorld world, RuntimeNativeNpc actor, NativeOwnedAnimationSoundPlayer sounds,
        FalloutAnimationSoundEvents events, FalloutCellScene cell, FalloutFormKey reference, FalloutFormKey sound,
        byte[] checkpointBytes, int level, FalloutGlobalState globals, Func<FalloutReferenceWorld, RuntimeNativeNpc> assemble)
    {
        var state = world.Get(reference);
        if (state.SelectionFailure is null || !state.SelectionFailureCaptureReady || actor.Combat is null)
            throw new InvalidDataException("Owned actor retirement requires its genuine stopped source selection and resident native pose.");
        var oldFailure = JsonSerializer.Serialize(state.SelectionFailure);
        var nativeOwner = actor.GetInstanceId();
        // This disposable component connects the real already-created source
        // sound owner to the existing combat readiness consumer. It starts no
        // engagement, attack, quest, campaign input or fabricated actor.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var enemyOwner = typeof(RuntimeNativeActorCombat).GetField("_enemySounds", flags)!;
        if (enemyOwner.GetValue(actor.Combat) is not null)
            throw new InvalidDataException("Owned fixture would replace an existing native combat sound owner.");
        enemyOwner.SetValue(actor.Combat, sounds);
        var part = world.BodyParts(reference).Parts.First().Type;
        var hit = world.DamageActor(reference, reference, part, .25f, 1, level, globals);
        typeof(RuntimeNativeActorCombat).GetMethod("RequestHitReaction", flags)!.Invoke(actor.Combat, [hit, (int)part]);
        if (state.HitReaction is not null || state.CurrentHitReactionError is null || state.HitReactionFaults.Faults.Count == 0)
            throw new InvalidDataException("Original receiver did not retain its stopped pure read without an active physical reaction.");
        var bones = Enumerable.Range(0, actor.Skeleton.Node.GetBoneCount()).Select(actor.Skeleton.Node.GetBonePose).ToArray();
        var names = Enumerable.Range(0, bones.Length).Select(index => actor.Skeleton.Node.GetBoneName(index).ToString()).ToArray();
        var completions = 0;
        for (var ordinal = 0; ordinal < 2; ordinal++) sounds.DispatchSound(sound, completed: () => ++completions);
        var pending = events.Events.Where(entry => entry.End == FalloutAnimationSoundEnd.Active).ToArray();
        var audioNodes = sounds.ActiveNativeVoices.ToArray();
        var random = state.SoundRandom.State; var hitHistory = JsonSerializer.Serialize(state.HitReactionFaults.Capture());
        var animation = JsonSerializer.Serialize(state.Animation.Capture());
        var diagnostic = actor.Combat.StoppedAiPoseCaptureDiagnostic;
        if (pending.Length != 2 || audioNodes.Length != 2 || state.SelectionFailureCaptureReady ||
            diagnostic.Ready || diagnostic.Blockers.Count != 1 || diagnostic.Blockers[0].Predicate != "enemy-sounds")
            throw new InvalidDataException("Actual native finite voices were not the sole stopped-pose refusal.");
        actor.GetParent().RemoveChild(actor); actor.Free(); world.UnloadCell(cell.Cell.FormKey);
        if (state.StoppedRetirement is not { } candidate || candidate.NativeOwner != nativeOwner ||
            state.SelectionFailureCaptureReady || world.PendingProcedureFiniteVoiceWait() is not { Count: 2 } voices ||
            voices.Any(voice => voice.NativeOwner != nativeOwner) || audioNodes.Any(node => !GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) ||
            JsonSerializer.Serialize(state.SelectionFailure) != oldFailure || completions != 0)
            throw new InvalidDataException("Child-first source model teardown lost its immutable nonaudio copy or exact unfinished native binding.");
        var refused = false;
        try { world.Capture(); } catch (NotSupportedException) { refused = true; }
        if (!refused) throw new InvalidDataException("Active original native audio admitted an actor checkpoint.");
        var timer = Stopwatch.StartNew();
        while (events.Events.Any(entry => entry.End == FalloutAnimationSoundEnd.Active) && timer.Elapsed.TotalSeconds < 10)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (pending.Any(entry => events.Events.Single(current => current.Generation == entry.Generation).End != FalloutAnimationSoundEnd.NativeFinished) ||
            completions != 2 || !state.SelectionFailureCaptureReady || state.SoundRandom.State != random ||
            JsonSerializer.Serialize(state.HitReactionFaults.Capture()) != hitHistory || JsonSerializer.Serialize(state.Animation.Capture()) != animation)
            throw new InvalidDataException("Actual native Finished lost generation, source fault, clock or RNG during model retirement.");
        var captured = world.Capture(); var saved = JsonSerializer.Serialize(captured);
        if (state.SelectionFailure is null || state.SelectionFailure.RandomState != captured.Single(value => value.Reference == reference).SelectionFailure!.RandomState)
            throw new InvalidDataException("Settled capture lost its stopped source selection.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(saved)!); cold.LoadCell(cell);
        var resumed = assemble(cold); using var resumedLifetime = new PackageFixtureLifetime(resumed);
        if (cold.Get(reference).StoppedRetirement is not null || records.SoundVoices.ActiveVoices != 0 ||
            resumed.Skeleton.Node.GetBoneCount() != bones.Length || JsonSerializer.Serialize(cold.Capture()) != saved)
            throw new InvalidDataException("Cold actor replayed audio or a source prefix, persisted the native lease or changed reached state.");
        for (var index = 0; index < bones.Length; index++)
            if (resumed.Skeleton.Node.GetBoneName(index).ToString() != names[index] || resumed.Skeleton.Node.GetBonePose(index) != bones[index])
                throw new InvalidDataException($"Cold retired actor changed original source bone {index}:{names[index]}.");
        cold.Get(reference).AnimationSoundEvents.ValidateMedia(content);
        GD.Print("OPENNV_OWNED_ACTOR_FINITE_RETIREMENT_PASS " + JsonSerializer.Serialize(new
        {
            runtimeBuild = typeof(RuntimeNativeActorCombat).Assembly.ManifestModule.ModuleVersionId,
            reference = reference.ToString(),
            referenceSha256 = Convert.ToHexString(SHA256.HashData(records.GetEffective(reference).ReadData())),
            attachedProgram = state.Script?.Record.FormKey.ToString(),
            attachedProgramSha256 = state.Script?.Sha256,
            sourceSelection = state.SelectionFailure.Candidate.ToString(),
            state.SelectionFailure.Sha256,
            sound = sound.ToString(),
            sourceSha256 = Convert.ToHexString(SHA256.HashData(records.GetEffective(sound).ReadData())),
            mediaSha256 = pending[0].MediaSha256,
            actualPath = pending[0].Path,
            nativeOwner,
            generations = pending.Select(entry => entry.Generation).ToArray(),
            nativeFinished = completions,
            modelFreedFirst = true,
            activeRefused = true,
            sameBinding = true,
            partialRetained = true,
            sourceRandom = random,
            hitFaultRetained = true,
            sourceClock = state.Animation.Capture(),
            allBoneCount = bones.Length,
            coldNoReplay = true,
            checkpointSha256 = Convert.ToHexString(SHA256.HashData(checkpointBytes)),
            recording = false,
            boundary = "disposable-original-NPC/SOUN/component-binding;ordinary-combat-cell-policy-and-parity-independent"
        }));
    }
}
