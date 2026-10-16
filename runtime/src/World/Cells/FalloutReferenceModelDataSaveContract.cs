using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutReferenceModelDataSaveContract
{
    internal static void ValidateOwned(FalloutProcessCommonSnapshot common, FalloutStandalonePlayerSceneSnapshot scene)
    {
        var data = common.ReferenceOwners?.ModelData;
        if (data is null)
        {
            if (scene.MoverDelta is not null) throw new InvalidDataException("Player mover omitted its actual reference model data owner.");
            return;
        }
        FalloutReferenceModelDataState.Validate(data);
        if (common.ReferenceOwners?.Position is null || data.Stack != scene.Stack || data.Stack != common.Stack || data.Process != scene.CapturedProcess ||
            data.Constructor != scene.Constructor || data.CommonProcess != common.CapturedProcess ||
            data.Source.Controller.Fields.Controls.Main != scene.Source.Main ||
            data.Actor != common.ReferenceOwners!.Position.Actor || data.Retired || data.RetirementStarted || data.Failure is not null || data.WriterBoundary is not null)
            throw new InvalidDataException("Reference model data changed its real Scene/Common/Actor/source or retained an unfinished writer.");
        var constructors = scene.PointOfView?.Lifetimes.Select(lifetime => (lifetime.Process, lifetime.Constructor)).ToHashSet() ?? [];
        constructors.Add((scene.CapturedProcess, scene.Constructor));
        var commonProcesses = common.Publications?.Select(publication => publication.Process).ToHashSet() ?? [];
        foreach (var publication in data.Publications)
            if (!constructors.Contains((publication.Process, publication.Constructor)) || !commonProcesses.Contains(publication.CommonProcess))
                throw new InvalidDataException("Reference model data substituted an unowned historical Scene/Common constructor.");
        if (data.Fields?.Model is { } model)
        {
            if (model.Process != data.Process || data.ModelScene is not { } graph)
                throw new InvalidDataException("Saved reference model has no actual current graph binding.");
            FalloutActorLogicalScene.RequireComplete(graph);
        }
        var reads = new List<FalloutReferenceModelDataRead>();
        if (scene.MoverDelta?.LastCall?.Data is { } mover)
        {
            if (scene.MoverDelta.Source != data.Source) throw new InvalidDataException("Mover read changed its actual source reference data service.");
            reads.Add(mover);
        }
        if (scene.ActorMovement?.LastCall?.Invocation.Data is { } actor) reads.Add(actor);
        FalloutReferenceModelDataState.ValidateReads(data, reads);
    }
}
