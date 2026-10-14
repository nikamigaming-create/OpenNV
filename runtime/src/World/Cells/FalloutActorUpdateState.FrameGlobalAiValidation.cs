namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorUpdateState
{
    internal static void ValidateFrameGlobalAiParent(FalloutActorUpdateSnapshot saved, string source, string stack)
    {
        if (saved.Schema != Schema || saved.Stack != stack || saved.PlayerFrameGlobalAi is not { } global || global.Source != source)
            throw new InvalidDataException("Actor-time lost its actual captured source Actor-update/shared-AI parent.");
        ValidatePlayerFrameGlobalAi(global, saved.CapturedProcess, saved.Sequence);
    }
}
