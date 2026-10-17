using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutRootAnimationControllerHead(Guid Manager, string Type, Guid Model, Guid Lifetime,
    Guid? NoteSource, bool PoseOwnerBound);
internal sealed record FalloutAnimationNoteSourceRegistration(FalloutReferenceModelDataInvocation Load,
    Guid Model, Guid Lifetime, Guid Manager, Guid Source, Guid Receiver, bool Result, long Revision);

internal sealed partial class FalloutLoadedAnimationManagerState
{
    private FalloutAnimationNoteSourceState? _animationNotes;
    internal FalloutRootAnimationControllerHead CaptureRootControllerHead(bool poseOwnerBound) =>
        new(Factory, "NiControllerManager", _scene.Factory, _scene.Lifetime, _animationNotes?.Capture().Identity, poseOwnerBound);
    internal FalloutAnimationNoteSourceRegistration RegisterActorAnimationNotes(FalloutReferenceModelDataInvocation load,
        FalloutActorAnimationNotes receiver, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        Require(); requireActual(load);
        var notes = receiver.Capture();
        if (load.Actor != notes.Actor || load.ReferenceProcess != notes.Process || load.ReferenceConstructor != notes.Constructor ||
            !_scene.IsCurrentRootAnimationController(this) || _animationNotes is null)
            throw new InvalidOperationException("Animation-note registration left its same current root controller and Actor receiver.");
        var result = _animationNotes.Register(1, receiver); Next();
        var registration = new FalloutAnimationNoteSourceRegistration(load, _scene.Factory, _scene.Lifetime, Factory,
            _animationNotes.Capture().Identity, notes.Identity, result, _sequence);
        Require(); requireActual(load); return registration;
    }
}
