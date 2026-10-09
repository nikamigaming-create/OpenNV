using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    internal FalloutActorProcessBodyBinding RequireCharacterControllerSourceBody(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy(); var current = Require(actor);
        if (current.Epoch != epoch || current.Level is not (FalloutDetectionProcessLevel.High or FalloutDetectionProcessLevel.MiddleHigh) ||
            current.Gameplay is null || current.Boundary is not null || current.Phase != FalloutProcessCommonPhase.Initialized ||
            current.Source3D is not { Value: true, Failure: null } || current.Body is not { } body)
            throw new NotSupportedException("Character controller source load lacks its actual initialized current process/body epoch.");
        var actual = Callback(() => _body(actor)) ?? throw new NotSupportedException("Character controller actual source body lease is absent.");
        if (!BodyEquivalent(actual, body)) throw new InvalidDataException("Character controller factory changed its current source body.");
        return actual;
    }
}
