using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private RuntimeNativePlayerPackage? _standaloneIdlePackageOwner;
    private Func<bool>? _standaloneIdlePackageLiving;

    internal void BindStandalonePlayerIdlePackageOwner(RuntimeNativePlayerPackage owner, Func<bool> living)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(living);
        if (!HasStandalonePlayerScene) return;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_standaloneIdlePackageOwner is not null)
            throw new InvalidOperationException("Source current Player package already has its actual driver owner.");
        owner.RequireIdlePackageWorld(this);
        _standaloneIdlePackageOwner = owner; _standaloneIdlePackageLiving = living;
    }

    private FalloutIdlePackageRead ReadStandalonePlayerIdlePackage(FalloutProcessSceneQuery before)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var owner = _standaloneIdlePackageOwner ?? throw new NotSupportedException(
            "source-current-process-idle-current-package-driver-factory-unbound");
        if (_standaloneIdlePackageLiving?.Invoke() != true || before.Actor.Reference != _enginePlayer ||
            !before.Actor.EnginePlayer || before.Level != FalloutDetectionProcessLevel.High ||
            before.Stack != CampaignStandalonePlayerScene.Stack || before.ExecutableSha256 != CampaignMainScriptSource.EngineSha256)
            throw new NotSupportedException("Source idle package lost its living Player driver or actual current process.");
        return owner.ReadIdlePackage(this, before);
    }
}
