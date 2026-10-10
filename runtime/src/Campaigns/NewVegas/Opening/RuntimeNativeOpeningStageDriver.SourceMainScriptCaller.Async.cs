namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal Task StopAndDrainSourceMainScriptCaller()
    {
        if (_sourceMainScriptCaller is not { } native) return Task.CompletedTask;
        if (!Godot.GodotObject.IsInstanceValid(native))
        {
            _scripts.References?.RequireCampaignMainNativeDeliveryClosed();
            return Task.CompletedTask;
        }
        return native.StopAndDrain();
    }
}
