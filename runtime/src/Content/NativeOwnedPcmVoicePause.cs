using Godot;

namespace OpenNV.Runtime.Content;

// Follow the actual native voice, including independent finite attachments and
// their temporary save-drain process mode. The sound manager is not its clock.
internal sealed partial class NativeOwnedPcmVoicePause : Node
{
    private readonly Node _voice;
    private readonly NativeOwnedPcmStream _pcm;

    internal NativeOwnedPcmVoicePause(Node voice, NativeOwnedPcmStream pcm)
    {
        _voice = voice; _pcm = pcm;
        Name = "OwnedPcmPause";
        ProcessMode = ProcessModeEnum.Inherit;
        _pcm.SetSuspended(!voice.CanProcess());
    }

    public override void _Notification(int what)
    {
        if (what == NotificationExitTree) _pcm.SetSuspended(true);
        else if (what == NotificationEnterTree || what == NotificationPaused || what == NotificationUnpaused)
            _pcm.SetSuspended(!_voice.CanProcess());
    }
}
