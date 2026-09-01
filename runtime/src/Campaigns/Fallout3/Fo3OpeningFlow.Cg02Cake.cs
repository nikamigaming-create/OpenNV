using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private CellActorLoader.PlacedActor EnsureCg02CakeAndy(
        Fo3Cg02CakeRuntime cake)
    {
        if (_cg02IntroActors.TryGetValue(cake.AndyReferenceFormId, out var existing))
            return existing;
        using var stream = File.OpenRead(cake.AndyActorScenePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(cake.AndyActorSceneSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 CG02 Andy actor scene hash differs.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG02 cake world is absent.");
        var actor = CellActorLoader.Load(
                cake.AndyActorScenePath,
                new HashSet<string>([coverage.Contract.CellFormId],
                    StringComparer.OrdinalIgnoreCase),
                coverage.CellRoot,
                coverage.Contract.EntryPositionGameUnits,
                _runtimeConfiguration,
                proofEnableInitiallyDisabled: false,
                materializeInitiallyDisabled: true)
            ?? throw new InvalidOperationException("Fallout 3 CG02 Andy is absent.");
        if (actor.ReferenceFormId != cake.AndyReferenceFormId ||
            actor.BaseFormId != cake.AndyBaseFormId)
            throw new InvalidOperationException(
                "Fallout 3 CG02 Andy actor identity differs.");
        _cg02IntroActors.Add(actor.ReferenceFormId, actor);
        return actor;
    }

    private void StartCg02CakeRuntime(
        Fo3Cg02CakeRuntime cake,
        Fo3Cg01ToddlerPlayer player,
        Action<int, string?> stageChanged,
        Action<Fo3Cg02CakeCue> cueCompleted,
        IReadOnlyCollection<string> appliedInfoFormIds,
        bool packageCompleted)
    {
        if (_cg02CakePackageTick is not null)
            return;
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG02 cake world is absent.");
        var andy = EnsureCg02CakeAndy(cake);
        if (packageCompleted)
        {
            PlayCue(0);
            return;
        }
        var target = GamebryoPackagePlacement.FromPlanarGameReferenceMarker(
            cake.PackageTargetMarkerFormId,
            new Vector3((float)cake.PackageTargetTransform.PositionGameUnits.X,
                (float)cake.PackageTargetTransform.PositionGameUnits.Y,
                (float)cake.PackageTargetTransform.PositionGameUnits.Z),
            new Vector3((float)cake.PackageTargetTransform.RotationRadians.X,
                (float)cake.PackageTargetTransform.RotationRadians.Y,
                (float)cake.PackageTargetTransform.RotationRadians.Z),
            (float)cake.PackageTargetTransform.Scale,
            coverage.Contract.EntryPositionGameUnits);
        var travel = GamebryoPackageTravel.Start(
            cake.PackageFormId,
            target,
            andy.Placement.Transform,
            [target.SourceTransform.Origin],
            cake.PackageLocomotionSpeedGameUnitsPerSecond,
            cake.PackageRadiusGameUnits);
        var locomotion = andy.Actor.LoadedAnimations.Single(value =>
            ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                ActorModelSlice.NormalizeAnimationPath(
                    cake.PackageLocomotionLogicalPath),
                StringComparison.OrdinalIgnoreCase) &&
            value.SourceSha256.Equals(cake.PackageLocomotionSha256,
                StringComparison.OrdinalIgnoreCase));
        locomotion.Player.Play(locomotion.RuntimeName);
        travel.Publish(andy.Placement);
        stageChanged(cake.TriggerStage, null);
        PlayCue(0);
        _cg02CakePackageTick = delta =>
        {
            var arrived = travel.Advance(delta);
            travel.Publish(andy.Placement);
            if (!arrived)
                return;
            _cg02CakePackageTick = null;
            var idle = andy.Actor.LoadedAnimations.Single(value =>
                ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                    ActorModelSlice.NormalizeAnimationPath(cake.PackageIdleLogicalPath),
                    StringComparison.OrdinalIgnoreCase));
            idle.Player.Play(idle.RuntimeName);
            Cg01WorldReference(cake.CakeReferenceFormId)
                .SetMeta("opennv_animation_group", "forward");
            player.SetMeta("opennv_cg02_timer", cake.FailsafeSeconds);
            player.SetMeta("opennv_cg02_run_timer", 1);
            stageChanged(cake.TargetStage, cake.PackageFormId);
        };

        void PlayCue(int index)
        {
            if (index == cake.Cues.Count)
                return;
            var cue = cake.Cues[index];
            if (appliedInfoFormIds.Contains(
                    cue.InfoFormId, StringComparer.OrdinalIgnoreCase))
            {
                PlayCue(index + 1);
                return;
            }
            var speaker = cue.SpeakerBaseFormId.Equals(
                    cake.AndyBaseFormId, StringComparison.OrdinalIgnoreCase)
                ? andy
                : _cg02IntroActors.Values.Single(value =>
                    value.BaseFormId.Equals(cue.SpeakerBaseFormId,
                        StringComparison.OrdinalIgnoreCase));
            var voice = new AudioStreamPlayer
            {
                Name = $"Fallout3Cg02CakeVoice{cue.Sequence}",
            };
            AddChild(voice);
            var dialogue = new GamebryoDialoguePlayback(
                voice, _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
            _cg02IntroDialogue.Add(dialogue);
            dialogue.Start(
                new SourceDialogueLine(
                    cue.InfoFormId, cue.Response.Index, cue.SpeakerBaseFormId,
                    cue.Response.Text,
                    new SourceDialogueAsset(cue.Response.Voice.LogicalPath,
                        cue.Response.Voice.SourcePath, cue.Response.Voice.Sha256),
                    new SourceDialogueAsset(cue.Response.Lip.LogicalPath,
                        cue.Response.Lip.SourcePath, cue.Response.Lip.Sha256)),
                new FaceGenMorphController(speaker.Actor,
                    _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip),
                () =>
                {
                    cueCompleted(cue);
                    PlayCue(index + 1);
                });
        }
    }
}
