using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutSourceTaskManager? _standaloneTaskManager;

    private void ConstructStandaloneTaskManager(FalloutMainPlayerCellSnapshot? saved)
    {
        if (!FalloutSourceMainFamily.IsFallout3(CampaignMainScriptSource.EngineSha256)) return;
        if (_standaloneTaskManager is not null)
            throw new InvalidOperationException("Original world task-manager singleton already has a current constructor.");
        var manager = new FalloutSourceTaskManager(CampaignMainPlayerCellSource, _processRuntimeStack!,
            ActualSourceProcessIdentity, ReadStandaloneSourceTaskCohort, saved?.PendingConsumers.TaskManager);
        // Retain the real constructor before binding its consumer. A failed
        // binding cannot forget an allocated manager or create a replacement.
        _standaloneTaskManager = manager;
        CampaignPlayerPendingConsumers.BindStandaloneTaskManager(manager, RequireCampaignMainNativeDeliveryClosed);
    }

    internal object? SourceTaskManagerState => _standaloneTaskManager?.State;

    private FalloutSourceTaskCohort ReadStandaloneSourceTaskCohort()
    {
        // World/Main constructs before the later real process-queue provider.
        // Deferring this exact provider read preserves that order; an early
        // reset/capture cannot certify a made-up empty queue.
        if (!SourceProcessQueuesConfigured)
            throw new NotSupportedException("Actual standalone task cohort has no constructed current source queue provider.");
        return QueuedReferences.ReadSourceTaskManagerCohort();
    }

    private void RetireStandaloneTaskManager() =>
        _standaloneTaskManager?.Retire(RequireCampaignMainNativeDeliveryClosed);

    private void RequireStandaloneTaskManagerRetiredBeforeQueueProviders() =>
        _standaloneTaskManager?.RequireRetiredBeforeProviders();
}
