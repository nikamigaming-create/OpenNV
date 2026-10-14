using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private RuntimeNativePlayerPackage? _standaloneIdlePackageOwner;

    internal void BindStandalonePlayerIdlePackageOwner(RuntimeNativePlayerPackage owner, Func<bool> living)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(living);
        if (!HasStandalonePlayerScene) return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_standaloneIdlePackageOwner is not null)
            throw new InvalidOperationException("Source current Player package already has its actual driver owner.");
        owner.RequireIdlePackageWorld(this);
        _standaloneIdlePackageOwner = owner;
    }

    private FalloutIdlePackageRead ReadStandalonePlayerIdlePackage(FalloutProcessSceneQuery before,
        FalloutProcessPackageSlot slot = FalloutProcessPackageSlot.Current)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (before.Actor.Reference != _enginePlayer ||
            !before.Actor.EnginePlayer || before.Level != FalloutDetectionProcessLevel.High ||
            before.Stack != CampaignStandalonePlayerScene.Stack || before.ExecutableSha256 != CampaignMainScriptSource.EngineSha256)
            throw new NotSupportedException("Source idle package lost its actual current Player process.");
        return ProcessCommon.ReadSourcePackageForIdle(before, slot);
    }
}
