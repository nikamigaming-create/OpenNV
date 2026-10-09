using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// The independent original GameMode predicate is distinct from menu mode. A
// genuine live producer must validate its own current observation; an absent
// producer cannot be replaced by a constant, player proxy or another frame gate.
internal sealed record FalloutChallengeGameModeObservation(string EngineSha256, string ProducerSha256,
    long Ordinal, bool Allows);
internal interface IFalloutChallengeGameModeSource
{
    FalloutChallengeGameModeObservation Observe();
    void RequireCurrent(FalloutChallengeGameModeObservation observation);
}

internal sealed partial class FalloutReferenceWorld
{
    private FalloutChallenges? _campaignChallenges;
    private IFalloutChallengeGameModeSource? _challengeGameModeSource;
    internal bool CampaignChallengesConfigured => _campaignChallenges is not null;
    internal FalloutChallenges Challenges
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _campaignChallenges ?? throw new NotSupportedException("Campaign has no genuine shared challenge registry.");
        }
    }
    internal void ConfigureCampaignChallenges(FalloutHudNotifications notifications, FalloutChallengeEventSource? source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(notifications);
        if (_campaignChallenges is not null) throw new InvalidOperationException("Challenge source registry cannot be reconstructed inside its live campaign.");
        _campaignChallenges = new(records, notifications, source);
    }
    internal void BindChallengeGameModeSource(IFalloutChallengeGameModeSource producer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_challengeGameModeSource is not null) throw new InvalidOperationException("Challenge GameMode source lifetime is already bound.");
        _challengeGameModeSource = producer ?? throw new ArgumentNullException(nameof(producer));
    }
    internal bool RequireChallengeImmediateGameMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // The original menu test short circuits before its independent source
        // predicate. A filtered block owns no executed instruction/receipt.
        if (!Menus.GameMode) return false;
        var producer = _challengeGameModeSource ??
            throw new NotSupportedException("source-challenge-immediate-GameMode-independent-predicate-unbound");
        var observation = producer.Observe();
        if (observation is null || observation.Ordinal <= 0 ||
            observation.EngineSha256 != Challenges.Source?.EngineSha256 ||
            !FalloutAdvancementRuntimeReceipt.Digest(observation.ProducerSha256))
            throw new InvalidDataException("Challenge GameMode observation has no current selected original predicate owner.");
        producer.RequireCurrent(observation); return observation.Allows;
    }
    private void RetireCampaignChallenges() => _campaignChallenges?.Retire();
}
