using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutIdlePackageRead ReadSourcePackageForIdle(FalloutProcessSceneQuery caller, FalloutProcessPackageSlot slot)
    {
        var selected = ReadSourcePackageSlotForIdle(caller, slot);
        var registry = _packageRegistry ?? throw new NotSupportedException("source-idle-winning-package-registry-unbound");
        return registry.ReadIdlePackage(selected);
    }
}
