using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private NativeOwnedAnimationSoundPlayer? _sourceAudioPlayer;
    private Transform3D _sourceFirstPersonAudioFrame;
    private bool _sourceAudioFramePublished;

    internal void BindSoundPoseOwner(NativeOwnedAnimationSoundPlayer owner)
    {
        if (_sourceAudioPlayer is not null && _sourceAudioPlayer != owner)
            throw new InvalidOperationException("Player source audio already has its canonical pose-boundary owner.");
        _sourceAudioPlayer = owner;
    }

    internal NativeOwnedSoundSourceScene ResolveSoundSourceScene(string? identity)
    {
        identity ??= (_xr is not null || !_thirdPersonMode) && _firstPerson is not null
            ? "player-first-person" : "player-reference";
        if (identity == "player-first-person")
        {
            var actor = _firstPerson ?? throw new NotSupportedException("Player sound has no actual first-person source scene.");
            return new(identity, actor.Actor, this, () =>
            {
                if (!GodotObject.IsInstanceValid(actor) || !actor.IsInsideTree())
                    throw new NotSupportedException("Player sound lost its original first-person source scene.");
                if (actor.GetViewport() == GetViewport()) return Transform3D.Identity;
                if (!_sourceAudioFramePublished || _firstPersonCamera is not { } camera ||
                    !GodotObject.IsInstanceValid(camera) || !camera.IsInsideTree() ||
                    camera.GetViewport() != actor.GetViewport())
                    throw new NotSupportedException("Player sound has no actual published first-person audio-space frame.");
                // This exact source Camera1st pose also drives the real view
                // camera. Read the captured actor's bone, not a camera reused by
                // a newer weapon model during old-emitter retirement.
                var sourceCamera = actor.SourceCamera;
                var determinant = sourceCamera.Basis.Determinant();
                if (!sourceCamera.Origin.IsFinite() || !sourceCamera.Basis.X.IsFinite() ||
                    !sourceCamera.Basis.Y.IsFinite() || !sourceCamera.Basis.Z.IsFinite() ||
                    !float.IsFinite(determinant) || determinant == 0)
                    throw new InvalidDataException("Player sound source camera frame is nonfinite or singular.");
                return _sourceFirstPersonAudioFrame * sourceCamera.AffineInverse();
            }, () => RequireSoundSourcePose(actor));
        }
        if (identity == "player-reference")
        {
            var actor = _thirdPerson ?? throw new NotSupportedException("Player sound has no actual reference source scene.");
            return new(identity, actor.Actor, this, RequirePublishedPose: () => RequireSoundSourcePose(actor));
        }
        throw new NotSupportedException("Saved player sound has an unknown source-scene owner.");
    }

    private static void RequireSoundSourcePose(RuntimeNativePlayerActor actor)
    {
        if (!GodotObject.IsInstanceValid(actor) || !actor.IsInsideTree() ||
            !GodotObject.IsInstanceValid(actor.Actor) || !actor.Actor.IsInsideTree())
            throw new NotSupportedException("Player timed sound has no resident source pose owner.");
        if (actor.Error is { } error)
            throw new NotSupportedException("Player timed sound source pose is unbound: " + error);
    }

    private void PublishSoundPoseBoundary() => _sourceAudioPlayer?.PublishSourcePose();
}
