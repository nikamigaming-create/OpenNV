using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutActorProcessBodyObservation ReadActualSourceProcess3D(FalloutFormKey actor)
    {
        EnsurePerceptionActor(actor);
        if (_processBodies.TryGetValue(actor, out var body))
        {
            if (!body.Living())
                return new(actor, new(null, "actual-source-body-lease-retired", "Actual source body is no longer published."),
                    null, "actual-High-process-3D-getter");
            return new(actor, new(true, "actual-source-body-lease:" + body.Source.Owner), body.Source, "actual-High-process-3D-getter");
        }
        if (ActorPerception.RequiresNativePublication(actor))
            return new(actor, new(null, "actual-required-current-or-cold-body-not-rebound",
                "The real source 3D lifetime requires an independent native body publication."), null, "actual-High-process-3D-getter");
        var current = ReadPerceptionObservation(actor);
        if (current.HasSource3D || _nativePerception.ContainsKey(actor))
            return new(actor, new(null, "actual-perception-body-has-no-source-common-binding",
                "Published source 3D has no exact common NIF/BPTD provider."), null, "actual-High-process-3D-getter");
        // This actual source-constructed or truly retired 3D reference is
        // known null. An absent callback/cold lease never reaches this arm.
        return new(actor, new(false, current.Owner + "/actual-source-3D-null"), null, "actual-High-process-3D-getter");
    }
}
