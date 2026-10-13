using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private IFalloutPlayerIdleModelOwner? _standaloneIdleModelOwner;
    private Func<int, float>? _standaloneIdleActorValue;
    internal void BindStandaloneIdleActorValues(Func<int, float> read)
    {
        ArgumentNullException.ThrowIfNull(read); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_standaloneIdleActorValue is not null) throw new InvalidOperationException("Player idle actor values already have a gameplay owner.");
        _standaloneIdleActorValue = read;
    }
    internal void BindStandalonePlayerIdleModelOwner(IFalloutPlayerIdleModelOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner); ObjectDisposedException.ThrowIf(_disposed, this);
        var scene = CampaignStandalonePlayerScene;
        if (_standaloneIdleModelOwner is not null || owner.Process != scene.Process || owner.Stack != scene.Stack ||
            owner.Source != scene.Source.Identity)
            throw new InvalidOperationException("Player idle model changed its current source, stack or process factory.");
        owner.RequireCurrent(); _standaloneIdleModelOwner = owner;
    }
    private IFalloutPlayerIdleModelOwner RequireStandaloneIdleModelOwner()
    {
        var owner = _standaloneIdleModelOwner ?? throw new NotSupportedException("source-Player-idle-model-and-animation-factory-unbound");
        owner.RequireCurrent(); return owner;
    }
    internal FalloutProcessIdleAnimationUpdate? LastStandalonePlayerIdleAnimation => ProcessCommon.LastIdleAnimationUpdate;
    private float ReadIdleSetting(string name) => FalloutGameSettingFloats.ReadRetained(records, name, nameof(FalloutActorIdleAnimationUpdate));
    internal FalloutProcessIdleAnimationUpdate UpdateStandalonePlayerIdleAnimation(FalloutStandalonePlayerSceneState scene,
        Func<float> readScaledTimer)
    {
        var before = ReadStandaloneSceneProcessQueries();
        return ProcessCommon.UpdateIdleAnimation(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch,
            new PlayerIdleConsumers(this, scene, readScaledTimer, before));
    }
    private sealed class PlayerIdleConsumers(FalloutReferenceWorld world, FalloutStandalonePlayerSceneState scene,
        Func<float> readScaledTimer, FalloutProcessSceneQuery before) : IFalloutActorIdleAnimationConsumers
    {
        private FalloutIdlePackageRead? _packageRead;
        public FalloutActorIdleClassValue? ActorClassValue { get; private set; }
        public string Owner => "actual-current-Player-model-and-High-idle-owner";
        public FalloutIdlePackageRead? SelectedPackageRead => _packageRead;
        public FalloutSourceSceneNode? ReadModel() => scene.ReadSelectedIdleModel(() =>
        {
            var current = world.ReadActualSourceProcess3D(world._enginePlayer);
            if (!current.Source3D.Require()) return null;
            throw new NotSupportedException("source-Player-idle-TLS-or-reference-render-model-root-factory-unbound");
        });
        public FalloutIdleSelectedObject? ReadSelectedObject() => FalloutIdlePackageSelection.Read(
            () => before.IdlePrimaryPackage is null ? null :
                throw new NotSupportedException("source-current-process-idle-primary-package-writer-unbound"),
            () => (_packageRead = world.ReadStandalonePlayerIdlePackage(before)).Result);
        public FalloutIdleSelectedObject? ReadAlternateObject() =>
            throw new NotSupportedException("source-Actor-idle-alternate-selected-object-factory-unbound");
        public FalloutIdleAnimationContext? ReadAnimation() => scene.ReadSelectedIdleAnimation(
            world.RequireStandaloneIdleModelOwner().ReadFirstPersonAnimation,
            () => before.AnimationContext is null ? null :
                throw new NotSupportedException("source-Player-idle-process-animation-context-current-factory-unbound"));
        public uint? ReadBoundRadius(FalloutSourceSceneNode model) => world.RequireStandaloneIdleModelOwner().ReadBoundRadius(model);
        public float ReadDistanceToPlayer() => world.ReadActorDistanceToPlayer(world._enginePlayer, bypassCell: true);
        public float ReadDistanceSetting() => world.ReadIdleSetting("fAIIdleAnimationDistance");
        public bool ReadActorStatePredicate()
        {
            var current = world.ReadStandalonePlayerIdlePackage(before);
            // This source predicate reads the current package independently
            // of the earlier primary/current selection. Only kind three enters
            // the separate package-action state table; the other arms return.
            if (current.Result is { Kind: 3 })
                throw new NotSupportedException("source-Actor-idle-kind-three-package-action-state-table-unbound");
            return false;
        }
        public bool ReadActorClassPredicate()
        {
            if (before.ActorValueCache is not null)
                throw new NotSupportedException("source-Actor-idle-allocated-value-cache-current-owner-unbound");
            var result = FalloutActorIdleClassPredicate.Read(() => (world._standaloneIdleActorValue ??
                throw new NotSupportedException("source-Player-idle-current-actor-value-owner-unbound"))(47));
            ActorClassValue = result.Value; return result.Result;
        }
        public float ReadWaitSetting() => world.ReadIdleSetting("fAIIdleWaitTime");
        public float ReadMainScaledTimer() => readScaledTimer();
        public void StartIdle() =>
            throw new NotSupportedException("source-High-idle-selected-animation-start-child-unbound");
    }
}
