using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal void BindStandaloneSessionMenuSource(FalloutImageSpaceState images, FalloutDirectInputState input,
        FalloutPlayerThreadContextState thread, Action requireActualOwners, Func<FalloutPlayerControlHudFrame> readActualFrame,
        string executable)
    {
        if (!_scripts.References!.HasStandaloneInterface) return;
        (_standaloneNativeInterface ?? throw new InvalidOperationException("Session menu must follow the actual Interface publication."))
            .BindSessionMenuLifecycles(images, input, thread, requireActualOwners, readActualFrame, executable);
    }
    internal void OpenSourceSessionMenu(Control actualMenu, FalloutUiComponentStore ui)
    {
        if (!_scripts.References!.HasStandaloneInterface) return;
        (_standaloneNativeInterface ?? throw new InvalidOperationException("Session menu lost its actual Interface projection."))
            .OpenSourceSessionMenu(actualMenu, ui);
    }
    internal bool BeginCloseSourceSessionMenu(Control actualMenu, Action actualNativeClosed)
    {
        if (!_scripts.References!.HasStandaloneInterface) return false;
        (_standaloneNativeInterface ?? throw new InvalidOperationException("Session menu close lost its actual Interface projection."))
            .CloseSourceSessionMenu(actualMenu, actualNativeClosed);
        return true;
    }
}
