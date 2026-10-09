namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    public override void _ExitTree()
    {
        try { RetireSourceIndexedInterfacePlayback(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireCurrentCampaignRest(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireSourceIndexedInterfaceState(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { DisposeSourcePlayerAdvancement(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireInterfaceActivationFrames(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireExperienceNotifications(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireCurrentNativeCellCapture(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireSourceActorPerceptionInputs(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireCombatGroupInputs(); }
        catch (Exception error) { RetainDriverFailure(error); }
    }
}
