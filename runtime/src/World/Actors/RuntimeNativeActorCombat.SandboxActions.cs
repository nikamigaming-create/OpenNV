using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    internal void BindSandboxActions(IFalloutSandboxNativeActionConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (SandboxActions is NativeSandboxActions current && current.Owns(consumer)) return;
        if (SandboxActions is not null) throw new InvalidOperationException("Sandbox actions already have an actual native actor lifetime.");
        SandboxActions = new NativeSandboxActions(this, consumer);
    }

    private sealed class NativeSandboxActions(RuntimeNativeActorCombat actor, IFalloutSandboxNativeActionConsumer consumer)
        : IFalloutNativeSandboxActions
    {
        private FalloutSandboxActionSource? _source;
        internal bool Owns(IFalloutSandboxNativeActionConsumer value) => ReferenceEquals(consumer, value);
        public FalloutSandboxTimerSample Timer() => actor._world.SandboxTimer();
        public FalloutSandboxActionContext Context() => actor._world.SandboxContext(actor._state.Reference);
        public float GameHour() => consumer.GameHour;
        public FalloutSandboxActionTime SelectionTime(float duration) => new(
            FalloutSandboxActionDeadline.Read(actor._world.CampaignPlayerRuntimeSource.Receipt), consumer.GameHour, duration);
        public (uint Minimum, uint Maximum) RescanInterval() => FalloutSandboxActionSource.RescanInterval(actor._records);
        public uint RepeatMilliseconds() => FalloutSandboxActionSource.RepeatMilliseconds(actor._records);
        public IReadOnlyList<FalloutSandboxCandidate> Discover(FalloutSandboxPackage source, FalloutSandboxArea area)
            => Discovery(source, area).Candidates;
        public FalloutSandboxDiscovery Discovery(FalloutSandboxPackage source, FalloutSandboxArea area)
            => actor._world.DiscoverSandbox(actor._state.Reference, source, area);
        public float Duration(FalloutSandboxCandidate chosen)
        {
            _source ??= FalloutSandboxActionSource.Read(actor._world.CampaignPlayerRuntimeSource.Receipt);
            var energy = FalloutSandboxActionSource.Energy(actor._records,
                actor._records.GetEffective(actor._state.Base), actor._state.Templates);
            return _source.Duration(FalloutSandboxActionSource.DurationInputs(actor._records, (FalloutSandboxAction)chosen.Action, energy),
                (minimum, maximum) => (float)((double)minimum + ((double)maximum - minimum) * actor._state.SoundRandom.NextUnitFloat()));
        }
        public void Enter(FalloutSandboxCandidate chosen)
        {
            actor._world.RequireSandboxCandidateSource(chosen);
            if (!actor._actor.IsInsideTree() || !actor._world.IsEnabled(actor._state.Reference))
                throw new NotSupportedException("Sandbox selected action lost its actual native source actor.");
            consumer.Enter(chosen);
        }
        public void Advance(double seconds) => consumer.Advance(seconds);
        public bool ObserveReturned(FalloutSandboxCandidate chosen) => consumer.ObserveRetired(chosen);
        public void Retire(FalloutSandboxCandidate chosen) => consumer.RequestRetirement(chosen);
        public FalloutSandboxNativeIdleContinuation Capture(FalloutSandboxCandidate chosen) => consumer.Capture(chosen);
        public void Restore(FalloutSandboxNativeIdleContinuation saved) => consumer.Restore(saved);
    }

    internal bool AdvanceSandboxMarkerApproach(FalloutPluginRecord package, FalloutFormKey marker, double seconds)
    {
        var actual = _world.RequireSandboxPublication(marker);
        if (actual.Cell != _world.Placement(_state.Reference).Cell)
            throw new NotSupportedException("Sandbox marker action requires its actual other-CELL route child.");
        var p = actual.Position;
        var authored = new Vector3(p[0], p[2], -p[1]) * _skeleton.UnitsToMetres;
        var destination = ProjectPackageDestination(authored);
        var tolerance = _mover!.SafeMargin * 8;
        AdvancePackageMotion(package, destination, tolerance, FalloutSandboxPackage.Read(package).Running,
            seconds, requireArrivalHeight: true);
        return _mover.IsOnFloor() && _actor.GlobalPosition.DistanceTo(destination) <= tolerance;
    }

    internal void RequireSandboxNativePublication()
    {
        if (!_actor.IsInsideTree() || !_world.IsEnabled(_state.Reference))
            throw new NotSupportedException("Sandbox native child has no actually attached/enabled actor publication.");
    }

    internal void RequireSandboxNativeRestoredPose()
    {
        RequireSandboxNativePublication();
        if (_state.PackageMotion is not { } motion ||
            _actor.GlobalPosition != new Vector3(motion.Position[0], motion.Position[1], motion.Position[2]))
            throw new NotSupportedException("Sandbox native child has no actually published/restored package position.");
    }
}
