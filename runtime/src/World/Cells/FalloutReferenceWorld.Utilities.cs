using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal object? MainUtilityState => CampaignSharedScriptRuntimeConfigured ? ProcessRuntime.MainUtilityState : null;
    internal string? MainUtilitySaveBlocker => CampaignSharedScriptRuntimeConfigured ? ProcessRuntime.MainUtilitySaveBlocker : null;
    internal void AddSourceAchievement(int actualSignedId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ProcessRuntime.AddSourceAchievement(actualSignedId);
    }
    internal IDisposable BindMainUtilityPlatform(IFalloutMainUtilityPlatform actualProvider) => ProcessRuntime.BindMainUtilityPlatform(actualProvider);
    internal IDisposable BindMainUtilityCommandHost(IFalloutMainUtilityCommandHost actualCommands) => ProcessRuntime.BindMainUtilityCommandHost(actualCommands);
    internal void RegisterSourceLoginCallback(string actualOwner, Action<int, bool>? actualCallback) =>
        ProcessRuntime.RegisterSourceLoginCallback(actualOwner, actualCallback);
}
