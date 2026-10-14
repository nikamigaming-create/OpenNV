using OpenNV.Runtime.World.Physics;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private static void ValidatePostActionControllerChildren(FalloutStandalonePlayerSceneSnapshot saved,
        FalloutStandalonePostActionsCall call)
    {
        var previous = -1;
        foreach (var child in call.ControllerChildren ?? [])
        {
            var index = child.QueryOrdinal;
            if (index <= previous || index >= call.ContextQueries.Count || index is not (0 or 1) ||
                call.ContextQueries[index].Context is null || child.Returned == (child.Failure is not null) ||
                child.Failure is { Length: 0 } || child.Failure is not null && child.Failure != call.Failure)
                throw new InvalidDataException("Post-action controller lost its current ordered query/failure prefix.");
            previous = index;
            if (child.Body is { } body)
            {
                ValidateControllerReadLineage(saved, body.Invocation, body.Lineage, body.Controller, call.ContextQueries[index],
                    call.Main, call.MainOrdinal, call.Entered, call.Changed, index, FalloutControllerPlayerSourceScope.PostActions);
                if (index != 0 || body.Field != FalloutControllerPostProcessField.FirstBody || body.Value is not null ||
                    body.Returned == (body.Failure is not null) || !body.Returned && body.Failure != child.Failure)
                    throw new InvalidDataException("Post-action body field lost its actual getter result.");
            }
            if (child.Flags is { } flags)
            {
                ValidateControllerReadLineage(saved, flags.Invocation, flags.Lineage, flags.Controller, call.ContextQueries[index],
                    call.Main, call.MainOrdinal, call.Entered, call.Changed, index, FalloutControllerPlayerSourceScope.PostActions);
                if (index != 1 || flags.Revision < 0 || flags.Returned == (flags.Failure is not null) ||
                    flags.Returned && flags.Flags is null || !flags.Returned && flags.Failure != child.Failure)
                    throw new InvalidDataException("Post-action flags lost their actual current word/read revision.");
            }
            if (child.Returned && (index == 0
                ? child.Body is not { Returned: true } || child.Flags is not null || !child.NullBodyReferenceCalled ||
                    child.Reference is not null || !child.RemoveEight || child.ClearContextFlag || call.BodyReference is not { Returned: true }
                : child.Body is not null || child.Flags is not { Returned: true, Flags: { } word } ||
                    (word & FalloutEarlyControllerSource.PostActionContextFlag) != 0 || child.NullBodyReferenceCalled || child.RemoveEight || !child.ClearContextFlag))
                throw new InvalidDataException("Returned controller post-action omitted its actual null-body or flags branch.");
        }
        if (call.BodyReference is not { } reference)
        {
            if (call.ControllerChildren?.Any(child => child.NullBodyReferenceCalled) == true)
                throw new InvalidDataException("Post-action body lost its actual forced getter/reference conversion.");
            return;
        }
        if (reference.Main != call.Main || reference.MainOrdinal != call.MainOrdinal ||
            !ChildBelongsToParentProcessLineage(saved, reference.Process) || reference.Entered <= call.Entered ||
            reference.Changed < reference.Entered || reference.Changed > call.Changed || reference.Input is not null ||
            reference.Result is not null || reference.Returned == (reference.Failure is not null) ||
            reference.Returned && !reference.GetterReturned || !reference.GetterReturned && reference.FirstPerson is not null ||
            reference.Failure is not null && reference.Failure != call.Failure ||
            call.ControllerChildren?.Any(child => child.QueryOrdinal == 0 && child.NullBodyReferenceCalled) != true)
            throw new InvalidDataException("Post-action null-body conversion lost its actual current source result.");
        ValidateForcedFirstPersonField(saved, reference);
    }
}
