using System.Globalization;
using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed partial class Fo2ArroyoTrialRuntime
{
    private sealed record CameronIntRelease(
        ClassicIntWorldObjectState WorldState,
        int ActorHandle,
        int DoorHandle);

    private CameronIntRelease ExecuteCameronRelease(
        ClassicIntProcedureState source,
        Sprite3D actor,
        Sprite3D door)
    {
        var handles = new ClassicIntActorHandleTable();
        var playerHandle = handles.Register(_player);
        var actorHandle = handles.Register(actor);
        var doorHandle = handles.Register(door);
        var world = new ClassicIntWorldObjectState(
            false,
            new Dictionary<int, ClassicIntDoorObjectState>
            {
                [doorHandle] = new(
                    State.CameronDoorOpened,
                    !State.CameronDoorUnlocked),
            })
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
                    ParsePid(_contract.Cameron.Pid),
                    State.CameronTile,
                    _contract.Cameron.Elevation,
                    State.CameronVisible),
                [doorHandle] = new(
                    doorHandle,
                    ParsePid(_contract.Cameron.ReleaseDoorPid),
                    _contract.Cameron.ReleaseDoorTile,
                    _contract.Cameron.ReleaseDoorElevation,
                    true),
            },
        };
        var visibility = new ClassicIntActorQueryTable(
            new Dictionary<(int, int), bool>
            {
                [(actorHandle, playerHandle)] =
                    Fo1HexMath.Distance(
                        State.CameronTile,
                        _player.CurrentTile) == 1,
            });
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
            visibility,
            _timerContract,
            null,
            _player.CurrentMapIndex);

        var releaseProcedure = _cameronProgram.ExecutableProgram
            .Procedures["critter_p_proc"];
        var scheduleDispatchBudget = releaseProcedure.Instructions.Count;
        var current = Execute("critter_p_proc", source, context, world);
        while (current.WorldObjects.Timers.Pending.Count == 0 &&
            scheduleDispatchBudget-- > 0)
            current = Execute(
                "critter_p_proc", current.State, context, current.WorldObjects);
        if (current.WorldObjects.Timers.Pending is not [{ } pending])
            throw new InvalidOperationException(
                "Fallout 2 Cameron source release did not schedule one timer.");
        var delivery = ClassicIntTimerOwner.TakeNextDue(
            current.WorldObjects.Timers,
            pending.DueTick);
        if (delivery.Event.TargetHandle != actorHandle)
            throw new InvalidOperationException(
                "Fallout 2 Cameron source timer targets an unbound object.");
        world = current.WorldObjects with { Timers = delivery.State };
        current = Execute(
            "timed_event_p_proc",
            current.State,
            context with { FixedParameter = delivery.Event.FixedParameter },
            world);
        var remainingDispatchBudget = releaseProcedure.Instructions.Count;
        while (current.WorldObjects.Objects[actorHandle].Visible &&
            remainingDispatchBudget-- > 0)
            current = Execute(
                "critter_p_proc", current.State, context, current.WorldObjects);

        var actorState = current.WorldObjects.Objects[actorHandle];
        var doorState = current.WorldObjects.Doors[doorHandle];
        if (!current.WorldObjects.Movements.Select(row => row.DestinationTile)
                .SequenceEqual(_contract.Cameron.ReleaseActorTiles) ||
            actorState.Tile != _contract.Cameron.ReleaseActorTiles[^1] ||
            actorState.Visible != _contract.Cameron.ReleaseFinalVisible ||
            doorState.Open != _contract.Cameron.ReleaseDoorOpened ||
            doorState.Locked == _contract.Cameron.ReleaseDoorUnlocked ||
            current.WorldObjects.Timers.Pending.Count != 0 ||
            current.WorldObjects.Timers.CurrentTick != pending.DueTick ||
            current.WorldObjects.AttackRequests.Count != 0)
            throw new InvalidOperationException(
                "Fallout 2 Cameron owned critter release drifted.");
        _commitRandomState(source.RandomState, current.State.RandomState);
        return new CameronIntRelease(
            current.WorldObjects, actorHandle, doorHandle);
    }

    private ClassicIntProcedureResult Execute(
        string procedure,
        ClassicIntProcedureState state,
        ClassicIntExpressionContext context,
        ClassicIntWorldObjectState world)
    {
        var executable = _cameronProgram.ExecutableProgram.Procedures[procedure];
        return ClassicIntEventDispatcher.Execute(
            _cameronProgram,
            procedure,
            state,
            context with
            {
                ProgramVariables = state.ProgramVariables,
                LocalVariables = state.LocalVariables,
                ScriptLocalVariables = state.ScriptLocalVariables,
                MapVariables = state.MapVariables,
                GlobalVariables = state.GlobalVariables,
            },
            world,
            _randomContract,
            executable.Instructions.Count);
    }

    private static int ParsePid(string source) => unchecked((int)uint.Parse(
        source,
        NumberStyles.AllowHexSpecifier,
        CultureInfo.InvariantCulture));
}
