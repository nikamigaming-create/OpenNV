using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutAdvancementRuntimeSource? _advancementRuntimeSource;
    internal object? PlayerAdvancementRuntimeState => _advancementRuntimeSource?.State;

    // Root joins this after the actual player pools/ranks/vitals exist. The
    // observer must publish living activity facts; no facts have a default.
    internal void ConfigureSourcePlayerAdvancement(
        Func<FalloutAdvancementActivityFact, FalloutAdvancementActivityObservation> observeActivity,
        Func<int, int> admitSkillBudget, Action<FalloutFormKey, int> acquirePerkRank)
    {
        if (_advancementRuntimeSource is not null) throw new InvalidOperationException("Selected advancement source is already configured.");
        var source = FalloutAdvancementRuntimeSource.Open(_pluginStack);
        try
        {
            var activity = source.Activity(observeActivity);
            ConfigureNativePlayerAdvancement(source.Player(_playerActorValues.Source), activity.Read,
                admitSkillBudget, acquirePerkRank);
            _advancementRuntimeSource = source;
        }
        catch { source.Dispose(); throw; }
    }

    // Join actual driver/world retirement, including rejected construction.
    private void DisposeSourcePlayerAdvancement()
    {
        _advancementRuntimeSource?.Dispose(); _advancementRuntimeSource = null;
    }
}
