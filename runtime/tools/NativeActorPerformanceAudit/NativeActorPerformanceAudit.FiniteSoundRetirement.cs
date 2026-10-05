using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task OwnedFiniteSoundRetirement(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutReferenceWorld world, RuntimeNativeNpc actor, NativeOwnedAnimationSoundPlayer sounds,
        FalloutAnimationSoundEvents events, FalloutCellScene cell, FalloutFormKey reference, FalloutFormKey sound,
        byte[] checkpointBytes)
    {
        var sourceHash = Convert.ToHexString(SHA256.HashData(records.GetEffective(sound).ReadData()));
        var referenceHash = Convert.ToHexString(SHA256.HashData(records.GetEffective(reference).ReadData()));
        var completed = 0;
        sounds.DispatchSound(sound, completed: () => ++completed);
        var active = events.Events.Last();
        var voice = sounds.ActiveNativeVoices.Single();
        var position = voice is AudioStreamPlayer3D spatial ? spatial.GlobalPosition : actor.GlobalPosition;
        var random = world.Get(reference).SoundRandom.State;
        if (!active.Played || active.End != FalloutAnimationSoundEnd.Active ||
            sounds.PendingFiniteVoices is not { Count: 1 } || sounds.CanCaptureSilent || voice.IsAncestorOf(actor) ||
            actor.IsAncestorOf(voice))
            throw new InvalidDataException("Owned finite voice lacks its independent actual source/native lifetime.");
        // Consume no additional source script, hit, package choice or KF key.
        // Retire the original model first, then let its actual audio complete.
        actor.GetParent().RemoveChild(actor); actor.Free(); world.UnloadCell(cell.Cell.FormKey);
        if (!GodotObject.IsInstanceValid(voice) || !voice.IsInsideTree() ||
            voice is AudioStreamPlayer3D positioned && positioned.GlobalPosition != position ||
            events.Events.Last().End != FalloutAnimationSoundEnd.Active || sounds.CanCaptureSilent || completed != 0)
            throw new InvalidDataException("Source model retirement cancelled or completed its finite native voice.");
        var refused = false;
        try { world.Capture(); } catch (NotSupportedException) { refused = true; }
        if (!refused) throw new InvalidDataException("Still-active owned voice entered a complete checkpoint.");
        var timer = Stopwatch.StartNew();
        while (events.Events.Last().End == FalloutAnimationSoundEnd.Active && timer.Elapsed.TotalSeconds < 10)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var ended = events.Events.Last();
        if (ended.End != FalloutAnimationSoundEnd.NativeFinished || !sounds.CanCaptureSilent || completed != 1 ||
            ended.Generation != active.Generation || ended.SoundSha256 != active.SoundSha256 ||
            ended.MediaSha256 != active.MediaSha256 || ended.Path != active.Path ||
            world.Get(reference).SoundRandom.State != random || !ended.PartialLanes.SequenceEqual(active.PartialLanes) ||
            sounds.CaptureDiagnostic is not { Retired: true, LostAtRetirement: false, Ready: true })
            throw new InvalidDataException("Actual native Finished lost retired source/media/RNG/history or repeated completion.");
        var saved = JsonSerializer.Serialize(world.Capture());
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(saved)!);
        cold.Get(reference).AnimationSoundEvents.ValidateMedia(content);
        if (JsonSerializer.Serialize(cold.Capture()) != saved || records.SoundVoices.ActiveVoices != 0)
            throw new InvalidDataException("Cold finished finite voice changed history, source failure or replayed audio.");
        GD.Print("OPENNV_OWNED_FINITE_SOUND_RETIREMENT_PASS " + JsonSerializer.Serialize(new
        {
            runtimeBuild = typeof(RuntimeNativeActorCombat).Assembly.ManifestModule.ModuleVersionId,
            reference = reference.ToString(),
            referenceSha256 = referenceHash,
            sound = sound.ToString(),
            sourceSha256 = sourceHash,
            mediaSha256 = active.MediaSha256,
            actualPath = active.Path,
            active.Generation,
            nativeFinished = true,
            modelFreedFirst = true,
            sourceEmitterPosition = new[] { position.X, position.Y, position.Z },
            sourceRandom = random,
            completionOnce = true,
            partialRetained = true,
            activeRefused = true,
            coldNoReplay = true,
            checkpointSha256 = Convert.ToHexString(SHA256.HashData(checkpointBytes)),
            recording = false,
            boundary = "disposable-original-finite-sound/model-retirement-component;combat-pose-retirement/cell-stop-policy/campaign/parity-independent"
        }));
    }
}
