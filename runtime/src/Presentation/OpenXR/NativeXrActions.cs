using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

// Native action identifiers are stable for the lifetime of the action map.
// Reuse them instead of allocating finalizable StringNames on every pose poll.
internal static class NativeXrActions
{
    internal static readonly StringName Head = "head";
    internal static readonly StringName DefaultPose = "default";
    internal static readonly StringName Fire = "fire";
    internal static readonly StringName Reload = "reload";
    internal static readonly StringName Grab = "grab";
    internal static readonly StringName Move = "move";
    internal static readonly StringName Sprint = "sprint";
    internal static readonly StringName Jump = "jump";
    internal static readonly StringName PipBoy = "pipboy";
    internal static readonly StringName Save = "save";
    internal static readonly StringName Activate = "activate";
    internal static readonly StringName Turn = "turn";
    internal static readonly StringName FingerTrigger = "finger_trigger";
    internal static readonly StringName ThumbTouch = "thumb_touch";
}
