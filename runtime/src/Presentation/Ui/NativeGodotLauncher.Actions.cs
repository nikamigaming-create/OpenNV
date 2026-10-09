using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal enum NativeGodotLauncherEntry { Menu, NewGame, Continue }

internal sealed partial class NativeGodotLauncher
{
    private Button _newGame = null!;
    private Button _continue = null!;

    private void ReportLauncherReady() => GD.Print("OPENNV_LAUNCHER_READY " + JsonSerializer.Serialize(new
    {
        phase = "launcher-construction",
        refreshed = true,
        buildVersion = _buildIdentity.Version ?? "development",
        sourceCommit = _buildIdentity.Commit ?? "unavailable",
        sourceModified = _buildIdentity.Dirty,
        buildMetadata = _buildIdentity.Text,
        runtimeManifestSha256 = _launcherManifestSha256,
        selectedProfile = _selectedGameId,
        selectedPresentation = _selectedPresentation,
        artworkLoaded = _heroTexture is not null,
        artworkPath = HeroArtworkPath,
        nativePixels = "unchecked",
        gameplayReady = false,
    }));

    private void RefreshLaunchActions(NativeGodotLauncherCampaign campaign, bool launchable, bool setupReady)
    {
        var nativeCampaign = campaign.EngineCampaign is "fallout-new-vegas" or "fallout-3";
        var route = campaign.Presentations.GetValueOrDefault(_selectedPresentation);
        _newGame.Visible = _continue.Visible = nativeCampaign && route?.PreviewOnly != true;
        _newGame.Disabled = !launchable || _launching;
        _continue.Disabled = true;
        _newGame.TooltipText = "Start through the game's original New Game confirmation. Existing saves remain until you save new progress.";
        _continue.TooltipText = "No OpenNV save found for this game and mod combination.";
        _saveStatus.Text = string.Empty;
        _saveStatus.TooltipText = string.Empty;
        if (nativeCampaign && setupReady)
        {
            try
            {
                var path = _profiles.LaunchSavePath(campaign.Id);
                var present = System.IO.File.Exists(path);
                _continue.Disabled = !present || !launchable || _launching;
                _saveStatus.Text = present ? "OpenNV save found · checked against your selected files when opened" : "No OpenNV save yet · begin with New Game";
                _saveStatus.TooltipText = path;
                _continue.TooltipText = present ? "Open this profile's save after the selected data and complete saved state are checked." : _continue.TooltipText;
            }
            catch (Exception error) when (error is System.IO.IOException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                _saveStatus.Text = "Save location unavailable: " + error.Message;
                _continue.TooltipText = _saveStatus.Text;
            }
        }
        else if (nativeCampaign) _saveStatus.Text = "Connect your game folder to use OpenNV saves.";
        _saveStatus.Visible = _saveStatus.Text.Length != 0;
        _toast.Visible = _toast.Text.Length != 0;
        if (!launchable)
        {
            var reason = _stackStatus.Text.Length == 0 ? route?.Status ?? campaign.Status : _stackStatus.Text;
            _newGame.TooltipText = reason;
            _continue.TooltipText = reason;
        }
    }

    private void RequireLauncherEntry(NativeGodotLauncherCampaign campaign, NativeGodotLauncherEntry entry)
    {
        if (!Enum.IsDefined(entry)) throw new InvalidDataException("Unknown launcher entry request.");
        if (entry == NativeGodotLauncherEntry.Menu) return;
        if (campaign.EngineCampaign is not ("fallout-new-vegas" or "fallout-3") ||
            campaign.Presentations.GetValueOrDefault(_selectedPresentation)?.PreviewOnly == true)
            throw new NotSupportedException("This profile opens its own menu through Play.");
        if (entry == NativeGodotLauncherEntry.Continue && !System.IO.File.Exists(_profiles.LaunchSavePath(campaign.Id)))
            throw new FileNotFoundException("No OpenNV save exists for the selected game and mod combination. Choose New Game.");
    }
}
