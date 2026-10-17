using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerPhysicalPlacement(FalloutPhysicalVector3 Position, FalloutPhysicalVector3 Rotation, uint Scale);

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutPlayerPhysicalPlacement ReadStandalonePlayerPhysicalPlacement()
    {
        RequireStandaloneControlPublication();
        return WithActualReferencePosition(FalloutReferencePositionIngress.CopyOrLoad, invocation =>
        {
            var owner = _sourcePlayerReferencePosition ?? throw new NotSupportedException("source-physical-current-reference-position-owner-unbound");
            var position = owner.ReadCurrentPosition(invocation).Position;
            var scale = owner.ReadReferenceScale(invocation).Bits;
            var rotation = CampaignStandalonePlayerScene.RequirePlayerReferenceOrientation().ReadPhysicalReferenceRotation(invocation.ReferenceConstructor);
            RequireActualReferencePosition(invocation);
            return new FalloutPlayerPhysicalPlacement(new(position.X, position.Y, position.Z), rotation, scale);
        });
    }
    internal FalloutActorProcessFact<int> ReadStandalonePlayerPhysicalMode() => ProcessRuntime.Life(_enginePlayer);

    internal FalloutActorPhysicalRegistrationTrace ReadStandalonePlayerPhysicalAdmission(
        Func<FalloutSourceSceneNode?> readReferenceModel, Action requireCurrent,
        Action<FalloutActorPhysicalRegistrationTrace> observe)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue,
            new PlayerPhysicalRegistrationConsumers(this, readReferenceModel, requireCurrent), observe);
    }

    private sealed class PlayerPhysicalRegistrationConsumers(FalloutReferenceWorld world,
        Func<FalloutSourceSceneNode?> readReferenceModel, Action requireCurrent) : IFalloutActorPhysicalRegistrationConsumers
    {
        public FalloutActorPhysicalAdmissionSource Source { get; } = FalloutActorPhysicalAdmissionSource.Reviewed(world.CampaignStandalonePlayerScene.Source.Main);
        public FalloutFormKey Actor => world._enginePlayer;
        public string Owner => "actual-Player-source-model-physical-publication";
        public void RequireCurrent()
        {
            ObjectDisposedException.ThrowIf(world._disposed, world); requireCurrent();
            if (world.CampaignStandalonePlayerScene.Source.Main != Source.Main)
                throw new InvalidOperationException("Player physical publication changed its actual Main source.");
        }
        public FalloutActorProcessFact<int> ReadMode() => world.ProcessRuntime.Life(Actor);
        public FalloutSourceSceneNode? ReadReferenceModel() => readReferenceModel();
        public bool HasCurrentProcess()
        {
            var process = world.ActorProcesses.Read(Actor);
            if (process.Retired) throw new InvalidOperationException("Player physical admission reached a retired process.");
            return process.Level is not null;
        }
        public FalloutProcessPhysicalActivityQuery ReadCurrentProcessActivity() =>
            world.ProcessCommon.ReadPhysicalActivity(Actor, world.ActorProcesses.Read(Actor).Epoch);
    }
}
