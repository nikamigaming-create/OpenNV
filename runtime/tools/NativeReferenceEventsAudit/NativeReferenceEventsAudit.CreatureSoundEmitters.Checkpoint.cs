using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private sealed record CreatureEmitterCheckpoint(string Path, string Sha256, string SourceSchema,
        FalloutNativeCampaignState State);

    private async Task ExerciseOwnedCreatureSoundEmittersCheckpoint(string gameRoot, string mod, string modRoot,
        string checkpointPath, string selector, string sourceKf, string[] dependencies)
    {
        var path = Path.GetFullPath(checkpointPath);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        try
        {
            var assembly = typeof(NativeOwnedAnimationSoundPlayer).Assembly;
            GD.Print($"OPENNV_NATIVE_OWNED_CREATURE_CHECKPOINT_ASSEMBLY path={assembly.Location} mvid={assembly.ManifestModule.ModuleVersionId}");
            await ExerciseOwnedCreatureSoundEmitters(gameRoot, mod, modRoot, [selector], sourceKf,
                dependencies, path);
        }
        finally
        {
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash,
                "Creature emitter fixture changed its immutable campaign checkpoint.");
            GD.Print($"OPENNV_NATIVE_OWNED_CREATURE_CHECKPOINT_UNCHANGED sha256={hash}");
        }
    }

    private static CreatureEmitterCheckpoint ReadCreatureEmitterCheckpoint(string path,
        RuntimeLiveContentSource content, FalloutPluginStack records)
    {
        var original = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(original));
        using var json = JsonDocument.Parse(original);
        var sourceSchema = json.RootElement.GetProperty("Schema").GetString() ??
            throw new InvalidDataException("Creature checkpoint has no saved schema.");
        var state = FalloutNativeCampaignSave.Read(path, content.SaveCompatibilityId, records,
            null, null, null, null).State;
        if (state.References is null || state.Scripts is null)
            throw new InvalidDataException("Creature checkpoint has no complete saved reference/script value graph.");
        var checkpoint = new CreatureEmitterCheckpoint(path, hash, sourceSchema, state);
        RequireCreatureCheckpointUnchanged(checkpoint);
        return checkpoint;
    }

    private static FalloutReferenceWorld CreateCreatureEmitterWorld(FalloutPluginStack records,
        CreatureEmitterCheckpoint? checkpoint, IReadOnlyList<FalloutReferenceSnapshot>? references = null)
    {
        var values = new FalloutScriptValueStore();
        if (checkpoint is not null) values.Restore(checkpoint.State.Scripts!.Values);
        var world = new FalloutReferenceWorld(records, values);
        try
        {
            if (checkpoint is not null) world.RestoreEncounterZones(checkpoint.State.EncounterZones);
            if (references is not null || checkpoint is not null)
                world.Restore(references ?? checkpoint!.State.References!);
            if (checkpoint is not null)
            {
                world.RestoreActorOverrides(checkpoint.State.ActorOverrides);
                world.RestoreFactionRelations(checkpoint.State.FactionRelations);
                world.RestoreDetection(checkpoint.State.DetectionEvents);
                world.ValidateValueHandles();
            }
            return world;
        }
        catch { world.Dispose(); throw; }
    }

    private static void RequireCheckpointCreature(CreatureEmitterCheckpoint checkpoint,
        FalloutPluginStack records, FalloutReferenceWorld world, FalloutReferenceInstance instance)
    {
        if (checkpoint.State.References!.Count(saved => saved.Reference == instance.Reference) != 1)
            throw new InvalidDataException("Selected creature is absent from the authentic checkpoint.");
        if (world.IsDead(instance.Reference) ||
            FalloutActorHealthSource.StartsDead(records, instance.Base, instance.Templates))
        {
            RequireCreatureCheckpointUnchanged(checkpoint);
            GD.Print("OPENNV_NATIVE_OWNED_CREATURE_CHECKPOINT_REFUSED " + JsonSerializer.Serialize(new
            {
                reference = instance.Reference.ToString(),
                reason = "dead-source-creature",
                equipDispatched = false,
                checkpointSha256 = checkpoint.Sha256,
                checkpointUnchanged = true
            }));
            throw new NotSupportedException("Checkpoint source creature is dead; finite equip dispatch is refused.");
        }
        if (instance.Destroyed || instance.DeletePending || instance.Deleted || instance.Taken ||
            instance.Ragdoll is not null || instance.KnockedDown || instance.Templates?.Absent == true)
            throw new NotSupportedException("Checkpoint source creature has no admitted living primary skeleton.");
        instance.AnimationSoundEvents.RequireEnabledSourceEmitter();
        if (!instance.AnimationSoundEvents.CanCapture ||
            instance.AnimationSoundEvents.Events.Any(entry => entry.End == FalloutAnimationSoundEnd.Active) ||
            instance.AnimationSoundEvents.Capture().OpaqueError is not null)
            throw new NotSupportedException("Checkpoint creature requires its prior sound continuation before a new finite key.");
        GD.Print("OPENNV_NATIVE_OWNED_CREATURE_CHECKPOINT_ADMITTED " + JsonSerializer.Serialize(new
        {
            reference = instance.Reference.ToString(),
            checkpointSha256 = checkpoint.Sha256,
            sourceSchema = checkpoint.SourceSchema,
            effectiveEnabled = world.IsEnabled(instance.Reference),
            living = true,
            retainedReferences = checkpoint.State.References!.Count,
            originalGeneration = instance.AnimationSoundEvents.Capture().Generation,
            originalEvents = instance.AnimationSoundEvents.Events.Count,
            priorHistoryRetained = true
        }));
    }

    private static void RequireCreatureCheckpointUnchanged(CreatureEmitterCheckpoint checkpoint)
    {
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(checkpoint.Path))) != checkpoint.Sha256)
            throw new InvalidDataException("Creature emitter fixture changed its immutable campaign checkpoint.");
    }
}
