using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollFinalFields(bool BodyPartFeatureAvailable, bool BodyPartFeatureEnabled,
    long FeatureStores, Guid? CurrentBody, long BodyStores, byte? PlayerArmControl, long PlayerArmStores,
    byte? PlayerFlag, long PlayerFlagStores)
{
    internal static FalloutRagdollFinalFields Construct() => new(false, false, 0, null, 0, null, 0, null, 0);
    internal void Validate()
    {
        if (BodyPartFeatureAvailable || BodyPartFeatureEnabled || CurrentBody is not null ||
            FeatureStores is not (0 or 1) || BodyStores is not (0 or 1) || PlayerArmStores is not (0 or 1) || PlayerFlagStores is not (0 or 1) ||
            BodyStores > FeatureStores || PlayerArmStores > FeatureStores || PlayerFlagStores > PlayerArmStores ||
            PlayerArmControl != (PlayerArmStores == 0 ? (byte?)null : (byte)0) || PlayerFlag != (PlayerFlagStores == 0 ? (byte?)null : (byte)1))
            throw new InvalidDataException("Ragdoll final fields lost their constructor, independent source stores or Player policy prefix.");
    }
}

internal sealed partial class FalloutLivingRagdollController
{
    private FalloutRagdollFinalFields _finalFields = FalloutRagdollFinalFields.Construct();
    internal void PublishFinalFeature(FalloutReferenceModelDataInvocation load)
    {
        RequireReturned();
        if (load != _load || _collisionGroupStores != 1 || _finalFields.FeatureStores != 0 ||
            _deathAssetPath is not null && _model.CaptureDeathInitialization() is not { Returned: true })
            throw new InvalidOperationException("Ragdoll final feature copy preceded its actual configuration children or replayed.");
        _finalFields = _finalFields with { BodyPartFeatureEnabled = _finalFields.BodyPartFeatureAvailable, FeatureStores = 1 };
    }
    internal void PublishFinalBody(FalloutReferenceModelDataInvocation load, Guid? body)
    {
        RequireReturned();
        if (load != _load || _finalFields.FeatureStores != 1 || _finalFields.BodyStores != 0)
            throw new InvalidOperationException("Ragdoll current-body publication left its actual final configuration scope.");
        if (body is not null) throw new NotSupportedException("source-ragdoll-nonnull-current-body-native-pointer-binding-unowned");
        _finalFields = _finalFields with { CurrentBody = body, BodyStores = 1 };
    }
    internal void PublishPlayerArmControl(FalloutReferenceModelDataInvocation load)
    {
        RequireReturned();
        if (load != _load || !load.Actor.EnginePlayer || _finalFields.FeatureStores != 1 || _finalFields.PlayerArmStores != 0)
            throw new InvalidOperationException("Ragdoll Player arm policy left its same actual source scope.");
        _finalFields = _finalFields with { PlayerArmControl = 0, PlayerArmStores = 1 };
    }
    internal void PublishPlayerFlag(FalloutReferenceModelDataInvocation load)
    {
        RequireReturned();
        if (load != _load || _finalFields.PlayerArmStores != 1 || _finalFields.PlayerFlagStores != 0)
            throw new InvalidOperationException("Ragdoll Player flag preceded its actual independent arm-control store.");
        _finalFields = _finalFields with { PlayerFlag = 1, PlayerFlagStores = 1 };
    }
}
