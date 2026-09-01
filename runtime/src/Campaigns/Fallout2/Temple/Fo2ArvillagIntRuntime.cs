using System.Globalization;
using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArvillagIntRoleState(
    string Role,
    int ActorHandle,
    ClassicIntProcedureState ProcedureState,
    ClassicIntWorldObjectState WorldState);

internal sealed class Fo2ArvillagIntRuntime
{
    private readonly IReadOnlyDictionary<string, Fo2ArvillagIntRoleState> _roles;

    private Fo2ArvillagIntRuntime(
        IReadOnlyDictionary<string, Fo2ArvillagIntRoleState> roles)
    {
        _roles = roles;
    }

    internal IReadOnlyDictionary<string, Fo2ArvillagIntRoleState> Roles => _roles;

    internal static Fo2ArvillagIntRuntime Enter(
        Fo2ArvillagPresentationCatalog catalog,
        Fo2ArvillagSceneCoverage scene,
        Fo2ArroyoCavesPlayerBody player,
        bool isLoadingGame,
        ClassicRetailRandomContract randomContract,
        Func<ClassicRetailRandomLifecycleState> readRandomState,
        Action<ClassicRetailRandomLifecycleState,
            ClassicRetailRandomLifecycleState> commitRandomState)
    {
        var sourceRandom = readRandomState();
        var random = sourceRandom;
        var results = new Dictionary<string, Fo2ArvillagIntRoleState>(
            StringComparer.Ordinal);
        var sourceOrderedRoles = catalog.IntRoles.Values.OrderBy(role =>
            catalog.IntInitialization.ScriptSlots.Single(slot =>
                slot.Sid == role.ActorSid &&
                slot.ScriptIndex == role.ActorScriptIndex).Order);
        foreach (var role in sourceOrderedRoles)
        {
            var actor = NodeTraversal.Descendants<Sprite3D>(scene.Root)
                .SingleOrDefault(row => row.HasMeta("map_serial") &&
                    row.GetMeta("map_serial").AsInt32() == role.ActorSerial) ??
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG {role.Role} actor is absent.");
            if (actor.GetMeta("source_pid").AsString() != role.ActorPid ||
                actor.GetMeta("source_sid").AsString() != role.ActorSid ||
                actor.GetMeta("map_tile").AsInt32() != role.ActorTile)
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG {role.Role} actor identity drifted.");
            var handles = new ClassicIntActorHandleTable();
            var playerHandle = handles.Register(player);
            var actorHandle = handles.Register(actor);
            var procedureState = new ClassicIntProcedureState(
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                role.InitialGlobalVariables,
                [],
                random);
            var metarules = role.MapEnterMetarules.ToDictionary(
                row => (row.Rule, row.Argument),
                row => row.Semantic switch
                {
                    "isLoadingGame" => isLoadingGame ? 1 : 0,
                    _ => throw new InvalidOperationException(
                        $"Fallout 2 ARVILLAG metarule semantic is unsupported: " +
                        row.Semantic),
                });
            var context = new ClassicIntExpressionContext(
                procedureState.ProgramVariables,
                procedureState.LocalVariables,
                procedureState.ScriptLocalVariables,
                procedureState.MapVariables,
                procedureState.GlobalVariables,
                playerHandle,
                actorHandle,
                null,
                null,
                new Dictionary<(int, int), int>(),
                metarules,
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
                Fo2ArvillagPresentationCatalog.MapIndex);
            var world = new ClassicIntWorldObjectState(
                false,
                new Dictionary<int, ClassicIntDoorObjectState>())
            {
                Objects = new Dictionary<int, ClassicIntWorldObject>
                {
                    [playerHandle] = new(
                        playerHandle,
                        null,
                        player.CurrentTile,
                        player.CurrentElevation,
                        true),
                    [actorHandle] = new(
                        actorHandle,
                        ParsePid(role.ActorPid),
                        role.ActorTile,
                        role.ActorElevation,
                        actor.Visible),
                },
            };
            var executable = role.Program.ExecutableProgram
                .Procedures["map_enter_p_proc"];
            var result = ClassicIntEventDispatcher.Execute(
                role.Program,
                "map_enter_p_proc",
                procedureState,
                context,
                world,
                randomContract,
                executable.Instructions.Count);
            var actorState = result.WorldObjects.Objects[actorHandle];
            if (result.WorldObjects.TraitAssignments.Count == 0 ||
                result.WorldObjects.TraitAssignments.Any(row =>
                    row.ObjectHandle != actorHandle) ||
                result.WorldObjects.Movements.Count != 0 ||
                result.WorldObjects.AttackRequests.Count != 0 ||
                result.MessageEffects.Count != 0 ||
                result.SoundEffects.Count != 0 ||
                result.State.ValueStack.Count != 0)
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG {role.Role} map-enter result drifted.");
            actor.Visible = actorState.Visible;
            actor.SetMeta("classic_int_map_enter_applied", true);
            foreach (var assignment in result.WorldObjects.TraitAssignments)
                actor.SetMeta(
                    $"classic_int_trait_{assignment.TraitType}_{assignment.Trait}",
                    assignment.Amount);
            random = result.State.RandomState;
            results.Add(role.Role, new Fo2ArvillagIntRoleState(
                role.Role,
                actorHandle,
                result.State,
                result.WorldObjects));
        }
        commitRandomState(sourceRandom, random);
        return new Fo2ArvillagIntRuntime(results);
    }

    private static int ParsePid(string source) => unchecked((int)uint.Parse(
        source,
        NumberStyles.AllowHexSpecifier,
        CultureInfo.InvariantCulture));
}
