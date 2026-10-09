using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class NativeOwnedGameplayHud
{
    private string _standaloneHudStack = "", _standaloneHudSha256 = "";
    internal string StandaloneHudStack => _standaloneHudStack;
    internal string StandaloneHudSha256 => _standaloneHudSha256;
    private void CaptureStandaloneHudInput(FalloutPluginStack records)
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Native HUD has no selected source.");
        if (records.OwnedSource is { } owner && !ReferenceEquals(owner, source) ||
            !source.TryRead("menus/main/hud_main_menu.xml", null, out var bytes, out _))
            throw new InvalidDataException("Native HUD construction changed its actual winning source XML owner.");
        _standaloneHudStack = source.StackId;
        _standaloneHudSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
