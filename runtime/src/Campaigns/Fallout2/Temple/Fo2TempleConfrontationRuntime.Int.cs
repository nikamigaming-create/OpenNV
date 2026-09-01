using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed partial class Fo2TempleConfrontationRuntime
{
    private void ExecuteGuardianPickupAndCritter()
    {
        var random = _readRandomState();
        var procedureState = new ClassicIntProcedureState(
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            _state.ScriptState.Locals,
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            [],
            random);
        var context = new ClassicIntExpressionContext(
            procedureState.ProgramVariables,
            procedureState.LocalVariables,
            procedureState.ScriptLocalVariables,
            procedureState.MapVariables,
            procedureState.GlobalVariables,
            _playerIntHandle,
            _guardianIntHandle,
            null,
            null,
            new Dictionary<(int, int), int>(),
            new Dictionary<(int, int), int>(),
            new Dictionary<int, int>(),
            new Dictionary<(int, int), int>(),
            new Dictionary<string, int>(),
            null,
            null,
            null,
            new ClassicIntObjectHandleTable(
                new Dictionary<ClassicIntObjectCreation, int>()),
            _playerIntHandle,
            new ClassicIntActorQueryTable(
                new Dictionary<(int, int), bool>
                {
                    [(_guardianIntHandle, _playerIntHandle)] = Adjacent(),
                }));
        var pickup = ClassicIntEventDispatcher.Execute(
            _guardianIntProgram,
            "pickup_p_proc",
            procedureState,
            context,
            _state.IntWorldState,
            _randomContract,
            _guardianIntProgram.ExecutableProgram.Procedures["pickup_p_proc"]
                .Instructions.Count);
        var critter = ClassicIntEventDispatcher.Execute(
            _guardianIntProgram,
            "critter_p_proc",
            pickup.State,
            context,
            pickup.WorldObjects,
            _randomContract,
            _guardianIntProgram.ExecutableProgram.Procedures["critter_p_proc"]
                .Instructions.Count);
        if (critter.WorldObjects.AttackRequests.Count !=
                _state.IntWorldState.AttackRequests.Count + 1 ||
            critter.WorldObjects.AttackRequests[^1] is not
            { ActorHandle: var actor, TargetHandle: var target } ||
            actor != _guardianIntHandle || target != _playerIntHandle ||
            critter.State.ProgramVariables.Count != 0 ||
            critter.State.LocalVariables.Count != 0 ||
            critter.State.MapVariables.Count != 0 ||
            critter.State.GlobalVariables.Count != 0 ||
            critter.State.ValueStack.Count != 0)
            throw new InvalidOperationException(
                "Fallout 2 guardian owned INT did not request player combat.");
        _commitRandomState(random, critter.State.RandomState);
        _state.ScriptState.ReplaceLocals(critter.State.ScriptLocalVariables);
        _state = _state with { IntWorldState = critter.WorldObjects };
    }
}
