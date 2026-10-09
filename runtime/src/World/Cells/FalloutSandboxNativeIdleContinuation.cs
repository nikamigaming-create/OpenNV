using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// An IDLM child retains its own selection cursor/KF, independently of the
// continuous parent PACK collection and the actor-wide replay/RNG fields.
internal sealed record FalloutSandboxNativeIdleContinuation(FalloutFormKey Actor, FalloutFormKey ActorBase,
    string ActorBaseSha256, FalloutSandboxCandidate Selection, FalloutFormKey MarkerCell,
    float[] MarkerPosition, FalloutIdleCollectionPlaybackSnapshot Collection,
    FalloutActorPackageIdleAnimation? Animation, long AnimationRevision, bool UsesPackageBase, ulong RandomState)
{
    internal void Validate()
    {
        if (Selection is null || Collection is null)
            throw new InvalidDataException("Sandbox native IDLM continuation omitted its selected child.");
        Selection.Validate(); Selection.RequireSourceIdentity(); Animation?.Validate();
        if (!FalloutActorFurnitureContinuation.ValidKey(Actor) || !FalloutActorFurnitureContinuation.ValidKey(ActorBase) ||
            !FalloutActorFurnitureContinuation.ValidHash(ActorBaseSha256) || Selection.Action != (int)FalloutSandboxAction.IdleMarker ||
            !FalloutActorFurnitureContinuation.ValidKey(MarkerCell) || MarkerPosition is not { Length: 3 } ||
            MarkerPosition.Any(value => !float.IsFinite(value)) || Collection is null || AnimationRevision < 0 ||
            AnimationRevision == long.MaxValue || Animation is { } active && active.Revision != AnimationRevision ||
            UsesPackageBase && Animation is null)
            throw new InvalidDataException("Sandbox native IDLM continuation lost its actual actor/marker/selection identity.");
    }

    internal void ValidateSource(FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey actor)
    {
        Validate();
        var instance = world.Get(actor);
        if (Actor != actor || instance.Base != ActorBase || instance.Deleted ||
            FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(ActorBase)) != ActorBaseSha256)
            throw new InvalidDataException("Sandbox native IDLM continuation changed its living actor source.");
        world.RequireSandboxCandidateSource(Selection);
        var marker = records.GetEffective(Selection.Base!.Value);
        var source = FalloutIdleCollection.Read(marker);
        var placement = world.Placement(Selection.Reference!.Value);
        if (placement.Cell != MarkerCell || !placement.Position.SequenceEqual(MarkerPosition))
            throw new InvalidDataException("Sandbox native marker moved relative to its captured source child.");
        var playback = new FalloutIdleCollectionPlayback(source, new(), _ =>
            throw new InvalidOperationException("Cold IDLM validation cannot reevaluate or select an idle."));
        playback.Restore(Collection);
        if (Animation is { } active)
        {
            active.ValidateSource(records);
            if (!source.Idles.Contains(active.Idle) || Collection.Cursor == 0 || Collection.SelectionCount == 0 ||
                Collection.WaitSeconds != 0 || Collection.Complete)
                throw new InvalidDataException("Sandbox native KF has no genuine consumed IDLM selection.");
        }
    }

    internal FalloutSandboxNativeIdleContinuation Copy() => this with
    { MarkerPosition = MarkerPosition.ToArray(), Animation = Animation?.Copy() };
}
