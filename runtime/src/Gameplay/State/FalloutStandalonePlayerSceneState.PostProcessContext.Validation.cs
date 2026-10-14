using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private static void ValidateControllerContextChildren(FalloutStandalonePlayerSceneSnapshot saved,
        FalloutStandalonePostProcessContext call)
    {
        var previous = -1;
        foreach (var child in call.ControllerChildren ?? [])
        {
            var index = child.QueryOrdinal;
            if (index <= previous || index >= call.Queries.Count || index is not (0 or 1) ||
                call.Queries[index].Context is null || child.Fields is null || child.Fields.Count > (index == 0 ? 3 : 1) ||
                child.Returned == (child.Failure is not null) || child.Failure is { Length: 0 } ||
                child.Failure is not null && child.Failure != call.Failure)
                throw new InvalidDataException("Controller continuation lost its ordered actual getter/failure prefix.");
            previous = index;
            for (var at = 0; at < child.Fields.Count; ++at)
            {
                var read = child.Fields[at]; var entered = read.Invocation; var lineage = read.Lineage;
                var field = index == 1 ? FalloutControllerPostProcessField.Object : at == 0
                    ? FalloutControllerPostProcessField.FirstBody : FalloutControllerPostProcessField.SecondBody;
                ValidateControllerReadLineage(saved, entered, lineage, read.Controller, call.Queries[index], call.Main,
                    call.MainOrdinal, call.Entered, call.Changed, index, FalloutControllerPlayerSourceScope.PostProcessContext);
                if (read.Field != field || read.Value is not null ||
                    read.Returned == (read.Failure is not null) || read.Failure is { Length: 0 } ||
                    !read.Returned && (at != child.Fields.Count - 1 || child.Returned || read.Failure != child.Failure))
                    throw new InvalidDataException("Controller field read changed its current source lineage, field order or result.");
            }
            if (child.Returned && (index == 0
                ? child.Fields.Count != 2 || !child.ComparedEqual || child.NullObjectReferenceCalled
                : child.Fields.Count != 1 || child.ComparedEqual || !child.NullObjectReferenceCalled || call.ObjectReference is not { Returned: true }))
                throw new InvalidDataException("Returned controller continuation omitted its actual equality/null-object child.");
        }
        if (call.ObjectReference is not { } reference)
        {
            if (call.ControllerChildren?.Any(child => child.NullObjectReferenceCalled) == true)
                throw new InvalidDataException("Controller object continuation lost its forced getter/reference-conversion receipt.");
            return;
        }
        if (reference.Main != call.Main || reference.MainOrdinal != call.MainOrdinal ||
            !ChildBelongsToParentProcessLineage(saved, reference.Process) || reference.Entered <= call.Entered ||
            reference.Changed < reference.Entered || reference.Changed > call.Changed || reference.Input is not null ||
            reference.Result is not null || reference.Returned == (reference.Failure is not null) ||
            reference.Returned && !reference.GetterReturned || !reference.GetterReturned && reference.FirstPerson is not null ||
            reference.Failure is not null && reference.Failure != call.Failure ||
            call.ControllerChildren?.Any(child => child.QueryOrdinal == 1 && child.NullObjectReferenceCalled) != true)
            throw new InvalidDataException("Controller null-object conversion lost its actual forced getter/result history.");
        ValidateForcedFirstPersonField(saved, reference);
    }

    internal static void ValidateControllerReadLineage(FalloutStandalonePlayerSceneSnapshot saved,
        FalloutControllerPostProcessInvocation entered, FalloutEarlyControllerLineage lineage, Guid controller,
        OpenNV.Runtime.World.Cells.FalloutProcessSceneContextQuery query, Guid main, long ordinal,
        long parentEntered, long parentChanged, int index,
        FalloutControllerPlayerSourceScope scope)
    {
        var lifetimes = saved.PointOfView?.Lifetimes ?? [];
        var lifetimeIndex = -1;
        for (var at = 0; at < lifetimes.Count; ++at)
            if (lifetimes[at].Process == entered.PlayerProcess && lifetimes[at].Constructor == entered.Constructor)
            { lifetimeIndex = at; break; }
        var source = OpenNV.Runtime.Content.FalloutEarlyControllerSource.Reviewed(
            OpenNV.Runtime.Content.FalloutPackageCommandFieldsSource.Reviewed(
                OpenNV.Runtime.Content.FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main)),
            OpenNV.Runtime.Content.FalloutPlayerThreadContextSource.Reviewed(saved.Source.Main));
        if (entered.Main != main || entered.MainOrdinal != ordinal || lifetimeIndex < 0 ||
            parentEntered <= lifetimes[lifetimeIndex].Published ||
            lifetimeIndex + 1 < lifetimes.Count && parentChanged >= lifetimes[lifetimeIndex + 1].Published ||
            saved.ThreadContextEntry is not { } thread || thread.Process != entered.PlayerProcess ||
            thread.Factory != lineage.ThreadFactory || thread.Main != main || thread.MainOrdinal != ordinal ||
            thread.Thread != entered.ThreadId || lineage.Source != source.Identity ||
            entered.SourceScope != scope || entered.QueryOrdinal != index || entered.Context != query || controller != query.Context ||
            lineage.Actor != query.Process.Actor || lineage.CommonProcess != query.Process.Process || lineage.Epoch != query.Process.Epoch ||
            lineage.Level != query.Process.Level || lineage.Stack != saved.Stack || lineage.Constructor != entered.Constructor ||
            lineage.RuntimeProcess != entered.RuntimeProcess || lineage.ThreadProcess != entered.PlayerProcess || lineage.Thread != entered.ThreadId ||
            lineage.Factory == Guid.Empty || controller == Guid.Empty || entered.RuntimeProcess == Guid.Empty || entered.PlayerProcess == Guid.Empty)
            throw new InvalidDataException("Controller read lost its actual current source/caller/process lineage.");
    }
}
