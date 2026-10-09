namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal Task StopAndDrainSourceMainScriptCaller() => _sourceMainScriptCaller?.StopAndDrain() ?? Task.CompletedTask;
}
