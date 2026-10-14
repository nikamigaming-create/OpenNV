namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal OpenNV.Runtime.World.Cells.RuntimeNativeSourceMainScriptCaller.SavePause PauseMainForSave() =>
        (_sourceMainScriptCaller ?? throw new InvalidOperationException("Save preparation has no actual Main delivery owner."))
            .AcquireSavePause();

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
