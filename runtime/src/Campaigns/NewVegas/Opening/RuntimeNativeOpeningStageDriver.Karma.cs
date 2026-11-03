using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private IReadOnlyDictionary<string, FalloutKarmaNotice>? _karmaNotices;
    private void RewardPlayerKarma(double requested)
    {
        var reward = FalloutKarma.Reward(_pluginStack, _playerActorValues.KarmaInteger(), FalloutActorValueCommands.Integer(requested));
        var next = _playerActorValues.PrepareKarmaReward(reward.Amount);
        var descriptor = FalloutKarma.Notice(reward.Notice);
        _karmaNotices ??= FalloutExecutableStringTable.ReadKarmaNotices(
            (RuntimeLiveContentSource.Current ?? throw new NotSupportedException("Karma feedback has no owned executable.")).FalloutExecutablePath);
        _ = _karmaNotices[descriptor.Text];
        var notice = new FalloutHudEvent(reward.Notice, _playerActorValues.Source.Player, reward.Amount,
            Text: FalloutGameSettingStrings.Read(_pluginStack, descriptor.Text));
        // Source caption/icon and sound must be admitted before any queue or value publication.
        _ = FalloutGameSettingStrings.Read(_pluginStack, descriptor.Image);
        var sound = FalloutDialogueTopic.Find(_pluginStack, "SOUN", descriptor.Sound);
        using var audio = _scripts.Sounds.PrepareInventorySounds([new(_pluginStack.RuntimeFormKey(0x14), sound.FormKey, true)]);
        _inventory.Notifications.RequirePublish([notice]);
        var published = false;
        try
        {
            published = true;
            _inventory.Notifications.Publish([notice]);
            audio.Commit();
            audio.Complete();
            _playerActorValues.CommitKarmaReward(next);
        }
        catch (Exception error)
        {
            // Native feedback precedes the value write. A failed committed prefix is never compensated into a fake success.
            if (published) _playerActorValues.FailKarmaReward(error);
            throw;
        }
    }
}
