using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private NativeOwnedMusicPlayer? _scriptMusic;
    private NativeOwnedMusicPlayer MusicPlayer
    {
        get
        {
            if (_scriptMusic is null)
            {
                _scriptMusic = new(_pluginStack) { Name = "OwnedScriptMusic" };
                AddChild(_scriptMusic);
            }
            return _scriptMusic;
        }
    }
}
