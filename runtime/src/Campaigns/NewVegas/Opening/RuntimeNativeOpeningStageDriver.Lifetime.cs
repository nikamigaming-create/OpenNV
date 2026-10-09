namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    public override void _ExitTree()
    {
        try { DisposeSourcePlayerAdvancement(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireInterfaceActivationFrames(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireExperienceNotifications(); }
        catch (Exception error) { RetainDriverFailure(error); }
        try { RetireCombatGroupInputs(); }
        catch (Exception error) { RetainDriverFailure(error); }
    }
}
