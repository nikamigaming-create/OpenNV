using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceBaseAssignmentCall(FalloutReferencePositionInvocation Invocation,
    FalloutFormKey? Before, FalloutFormKey After, FalloutReferenceBaseAssignmentInputs? Inputs,
    bool PointerStored, bool FlagReturned, byte? ActorResponse, bool Returned, string? Failure);

internal sealed partial class FalloutReferenceWorld
{
    private FalloutReferenceBaseAssignmentCall? _sourceReferenceBaseAssignment;
    private byte? _sourceReferenceActorResponse;

    private void PublishInitialReferenceBase()
    {
        if (_sourceReferenceBaseAssignment is not null)
            throw new InvalidOperationException("Winning reference Base assignment cannot replay.");
        if (!_sourceReferenceBaseConstructed || _sourceEarlyControllerHelperFailure is not null ||
            _sourceEarlyControllerConstruction is not { Returned: true })
            throw new NotSupportedException("source-reference-Base-assignment-preceding-controller-child-unreturned");
        _ = WithActualReferencePosition(FalloutReferencePositionIngress.BaseAssignment, invocation =>
        {
            var current = records.GetEffective(invocation.Actor.Base);
            var call = new FalloutReferenceBaseAssignmentCall(invocation, _sourceReferenceCurrentBase?.FormKey,
                current.FormKey, null, false, false, null, false, null);
            try
            {
                RequireActualReferencePosition(invocation);
                _sourceReferenceCurrentBase = current;
                call = call with { PointerStored = true };
                var inputs = FalloutReferenceBaseAssignmentInputs.Read(_sourcePlayerReferencePosition!.Source,
                    records, invocation.Actor);
                call = call with { Inputs = inputs };
                _ = _sourceReferenceScriptInitializer!.SetBaseDestructibleFlag(invocation, inputs, RequireActualReferencePosition);
                call = call with { FlagReturned = true };
                RequireActualReferencePosition(invocation);
                _sourceReferenceActorResponse = inputs.ActorResponse ? (byte)1 : (byte)0;
                call = call with { ActorResponse = _sourceReferenceActorResponse, Returned = true };
                return call;
            }
            catch (Exception error)
            {
                call = call with { Failure = error.Message };
                throw;
            }
            finally { _sourceReferenceBaseAssignment = call; }
        });
    }
}
