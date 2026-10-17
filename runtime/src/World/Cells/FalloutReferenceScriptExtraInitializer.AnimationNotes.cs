namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceAnimationNoteExtra(Guid Identity, Guid List, byte Subtype,
    FalloutReferenceModelDataInvocation Writer, FalloutActorAnimationNotesSnapshot Receiver);

internal sealed partial class FalloutReferenceScriptExtraInitializer
{
    private sealed record AnimationNoteExtra(Guid Identity, FalloutReferenceModelDataInvocation Writer, FalloutActorAnimationNotes Receiver);
    private AnimationNoteExtra? _animationNoteExtra;
    internal FalloutReferenceAnimationNoteExtra? CaptureAnimationNoteExtra() => _animationNoteExtra is { } extra ?
        new(extra.Identity, Factory, 0x6d, extra.Writer, extra.Receiver.Capture()) : null;

    internal FalloutActorAnimationNotes GetActorAnimationNotes(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        _ = ReadControllerConstructorExtra(0x6d, load, requireActual);
        if (_animationNoteExtra is null)
        {
            var receiver = new FalloutActorAnimationNotes(load.ReferenceProcess, Constructor, Actor);
            _animationNoteExtra = new(Guid.NewGuid(), load, receiver);
        }
        requireActual(load); return _animationNoteExtra.Receiver;
    }
}
