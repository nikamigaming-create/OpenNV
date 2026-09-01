using Godot;
using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed partial class Fo2ArroyoTrialRuntime
{
    private sealed record KlintMapEnterResult(
        ClassicIntWorldObjectState WorldObjects,
        int GateHandle);

    private KlintMapEnterResult ExecuteKlintMapEnter(
        Fo2TemplePresentationCatalog catalog,
        Sprite3D actor,
        Sprite3D gate)
    {
        var program = catalog.IntInitialization.ScriptSlots.Single(row =>
            row.ScriptIndex == _contract.KlintGate.ActorScriptIndex &&
            row.Program.LogicalPath.Equals(
                _contract.KlintGate.ActorProgramLogicalPath,
                StringComparison.OrdinalIgnoreCase) &&
            row.Program.Sha256 == _contract.KlintGate.ActorProgramSha256).Program;
        if (catalog.Confrontation.Critter.Serial != _contract.KlintGate.ActorSerial ||
            catalog.Confrontation.Critter.ScriptIndex !=
                _contract.KlintGate.ActorScriptIndex)
            throw new InvalidOperationException(
                "Fallout 2 ACKlint source actor identity drifted.");
        var random = _readRandomState();
        var source = new ClassicIntProcedureState(
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            new Dictionary<int, int>
            {
                [_contract.GlobalVariableIndex] = State.GlobalVariable10,
            },
            [],
            random);
        var handles = new ClassicIntActorHandleTable();
        var playerHandle = handles.Register(_player);
        var actorHandle = handles.Register(actor);
        var gateHandle = handles.Register(gate);
        var world = new ClassicIntWorldObjectState(
            false,
            new Dictionary<int, ClassicIntDoorObjectState>())
        {
            Objects = new Dictionary<int, ClassicIntWorldObject>
            {
                [playerHandle] = new(
                    playerHandle,
                    null,
                    _player.CurrentTile,
                    _player.CurrentElevation,
                    true),
                [actorHandle] = new(
                    actorHandle,
                    ParsePid(catalog.Confrontation.Critter.Pid),
                    actor.GetMeta("map_tile").AsInt32(),
                    catalog.Confrontation.Critter.Elevation,
                    actor.Visible),
                [gateHandle] = new(
                    gateHandle,
                    ParsePid(_contract.KlintGate.GatePid),
                    gate.GetMeta("map_tile").AsInt32(),
                    _contract.KlintGate.Elevation,
                    gate.Visible),
            },
        };
        var context = new ClassicIntExpressionContext(
            source.ProgramVariables,
            source.LocalVariables,
            source.ScriptLocalVariables,
            source.MapVariables,
            source.GlobalVariables,
            playerHandle,
            actorHandle,
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
            null,
            null,
            null,
            null,
            Fo2TemplePresentationCatalog.MapIndex);
        var executable = program.ExecutableProgram.Procedures["map_enter_p_proc"];
        var result = ClassicIntEventDispatcher.Execute(
            program,
            "map_enter_p_proc",
            source,
            context,
            world,
            _randomContract,
            executable.Instructions.Count);
        var movedGate = result.WorldObjects.Objects[gateHandle];
        if (result.WorldObjects.Movements is not [{ } movement] ||
            movement.ObjectHandle != gateHandle ||
            movement.SourceTile != _contract.KlintGate.SourceTile ||
            movement.DestinationTile != _contract.KlintGate.DestinationTile ||
            movement.DestinationElevation != _contract.KlintGate.Elevation ||
            movedGate.Tile != _contract.KlintGate.DestinationTile ||
            movedGate.Elevation != _contract.KlintGate.Elevation ||
            result.WorldObjects.TraitAssignments.Count == 0 ||
            result.WorldObjects.TraitAssignments.Any(row =>
                row.ObjectHandle != actorHandle) ||
            result.WorldObjects.AttackRequests.Count != 0 ||
            result.MessageEffects.Count != 0 ||
            result.SoundEffects.Count != 0 ||
            result.State.ValueStack.Count != 0)
            throw new InvalidOperationException(
                "Fallout 2 ACKlint owned map-enter result drifted.");
        _commitRandomState(random, result.State.RandomState);
        return new KlintMapEnterResult(result.WorldObjects, gateHandle);
    }
}
