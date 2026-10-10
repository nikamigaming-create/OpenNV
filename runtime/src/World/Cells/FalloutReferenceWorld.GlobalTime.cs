using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutGlobalTimeState SourceGlobalTime => ProcessRuntime.RequireSourceGlobalTime();
    internal FalloutGlobalTimeState? ScriptGlobalTime => _processRuntime?.ScriptGlobalTime;
    private void RetireCampaignSourceGlobalTime() => _processRuntime?.RetireSourceGlobalTime();
}
