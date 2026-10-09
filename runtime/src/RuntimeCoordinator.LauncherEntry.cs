using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private NativeGodotLauncherEntry _launcherEntry;

    private void CancelNativeLauncherEntry() => _launcherEntry = NativeGodotLauncherEntry.Menu;

    private void ConfigureLauncherEntry(NativeGodotLauncherLaunchRequest request)
    {
        if (!Enum.IsDefined(request.Entry) || request.Entry != NativeGodotLauncherEntry.Menu &&
            request.EngineCampaign is not ("fallout-new-vegas" or "fallout-3"))
            throw new NotSupportedException("The selected runtime has no New Game/Continue launcher owner.");
        _launcherEntry = request.Entry;
    }

    private void ConsumeNativeLauncherEntry(NativeGamebryoStartMenu menu)
    {
        var entry = _launcherEntry;
        _launcherEntry = NativeGodotLauncherEntry.Menu;
        if (entry == NativeGodotLauncherEntry.Menu) return;
        try { menu.RequestLauncherEntry(entry); }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException)
        {
            menu.ShowLoadFailure(error.Message, canRetry: true);
        }
    }
}
