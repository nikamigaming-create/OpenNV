using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutAdvancementRuntimeSource? _campaignPlayerRuntimeSource;
    private Exception? _campaignPlayerRuntimeFailure;
    internal string? CampaignPlayerRuntimeFailure => _campaignPlayerRuntimeFailure?.ToString();
    internal FalloutAdvancementRuntimeSource CampaignPlayerRuntimeSource
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_campaignPlayerRuntimeFailure is { } failure)
                throw new InvalidOperationException("Campaign source construction retains its actual failed prefix.", failure);
            return _campaignPlayerRuntimeSource ?? throw new NotSupportedException("The campaign player runtime source is absent.");
        }
    }

    internal void ConfigureCampaignPlayerRuntime(FalloutHudNotifications notifications,
        FalloutPlayerStatisticsSnapshot? restore = null, FalloutChallengesSnapshot? challenges = null,
        FalloutIndexedInterfaceSoundSnapshot? interfaceSounds = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_campaignPlayerRuntimeSource is not null || _campaignPlayerRuntimeFailure is not null)
            throw new InvalidOperationException("Campaign source lifetime cannot be replaced or retried.", _campaignPlayerRuntimeFailure);
        FalloutAdvancementRuntimeSource? source = null;
        try
        {
            source = FalloutAdvancementRuntimeSource.Open(records);
            var statistics = FalloutMiscellaneousStatisticSource.Read(records, source.Receipt);
            var challengeSource = statistics.ChallengeEvent == 11 ? FalloutChallengeEventSource.Read(statistics,
                FalloutInstallationSettings.Read(source.OwnedSource).NumericIni) : null;
            if (restore is not null && (challenges is null || interfaceSounds is null))
                throw new InvalidDataException("Current campaign has no challenge dispatch/indexed sound continuation.");
            ConfigureCampaignChallenges(notifications, challengeSource);
            ConfigurePlayerStatistics(source.Receipt, new(null, null, null), restore);
            if (challengeSource is not null) Challenges.BindStatistics(PlayerStatistics);
            ConfigureCampaignIndexedInterfaceSounds(source, interfaceSounds);
            if (challenges is not null) Challenges.Restore(challenges);
            _campaignPlayerRuntimeSource = source;
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            try { RetireCampaignIndexedInterfaceSounds(); } catch (Exception cleanup) { errors.Add(cleanup); }
            try { RetireCampaignChallenges(); } catch (Exception cleanup) { errors.Add(cleanup); }
            try { RetirePlayerStatistics(); } catch (Exception cleanup) { errors.Add(cleanup); }
            try { source?.Dispose(); } catch (Exception cleanup) { errors.Add(cleanup); }
            var retained = errors.Count == 1 ? failure : new AggregateException(errors);
            _campaignPlayerRuntimeFailure = retained;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(retained).Throw();
            throw;
        }
    }

    private void RetireCampaignPlayerRuntime() => _campaignPlayerRuntimeSource?.Dispose();
}
