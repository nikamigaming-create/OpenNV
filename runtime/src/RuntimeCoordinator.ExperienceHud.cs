using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private NativeOwnedExperienceHud? _nativeExperienceHud;

    private void AttachNativeExperienceHud(RuntimeNativeOpeningStageDriver driver)
    {
        if (GodotObject.IsInstanceValid(_nativeExperienceHud))
            throw new InvalidOperationException("The current native session already owns its experience HUD.");
        var layer = new CanvasLayer
        {
            Name = "NativeExperienceLayer",
            Layer = NativeMenuCanvasLayer - 1,
            ProcessMode = ProcessModeEnum.Always,
        };
        try
        {
            // The driver owns the HUD's tree lifetime. Its notification and
            // clock owners outlive the child's native retirement callbacks.
            driver.AddChild(layer);
            _nativeExperienceHud = NativeOwnedExperienceHud.Attach(layer, _nativePluginStack!,
                driver.ExperienceHudSource, driver.ExperienceNotifications,
                () => driver.ExperienceHudAdmission(_nativeXr is null,
                    _nativeDoorLoading || _nativeSessionTransitioning || _retiringNativeSession),
                () => driver.ExperiencePendingLevel, () => driver.ExperienceCharacterGenerationEnded,
                driver.ReadExperienceUiClock, _nativeUi);
        }
        catch
        {
            if (GodotObject.IsInstanceValid(layer)) layer.Free();
            throw;
        }
    }

    private void RetireNativeExperienceHud()
    {
        if (GodotObject.IsInstanceValid(_nativeExperienceHud)) _nativeExperienceHud!.RetireForSession();
        _nativeExperienceHud = null;
    }
}
