using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void BindSourceSessionMenu(FalloutReferenceWorld world)
    {
        if (!world.HasStandaloneInterface) return;
        var input = _nativeDirectInput ?? throw new InvalidOperationException("Source session menu has no actual input adapter.");
        var driver = _nativeOpeningStageDriver ?? throw new InvalidOperationException("Source session menu has no actual driver.");
        var scene = world.CampaignStandalonePlayerScene;
        var manager = input.PlayerManager();
        input.BindSourceMenuMouseFlush();
        driver.BindStandaloneSessionMenuSource(_nativeImageSpaceState, manager, scene.ThreadContext, RequireOwners,
            world.ReadSourceInterfaceMenuFrame, _nativePluginStack!.OwnedSource!.FalloutExecutablePath);

        void RequireOwners()
        {
            if (!ReferenceEquals(_nativeReferences, world) || !ReferenceEquals(_nativeDirectInput, input) ||
                !ReferenceEquals(_nativeOpeningStageDriver, driver) || !GodotObject.IsInstanceValid(input) ||
                !input.IsInsideTree() || input.IsQueuedForDeletion() || !GodotObject.IsInstanceValid(driver) ||
                !driver.IsInsideTree() || driver.IsQueuedForDeletion() || _retiringNativeSession ||
                !ReferenceEquals(input.PlayerManager(), manager) || !ReferenceEquals(world.CampaignStandalonePlayerScene, scene) ||
                scene.ThreadContext.Process != scene.Process || !_nativeImageSpaceState.HasSourceMainImages)
                throw new InvalidOperationException("Source session menu lost its actual World/input/image/scene/native lifetime.");
        }
    }
}
