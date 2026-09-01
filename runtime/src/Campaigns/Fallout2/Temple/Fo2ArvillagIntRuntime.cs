using System.Globalization;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArvillagIntRoleState(
    string Role,
    int PlayerHandle,
    int ActorHandle,
    ClassicIntProcedureState ProcedureState,
    ClassicIntWorldObjectState WorldState,
    IClassicIntObjectFactory ObjectFactory);

internal sealed class Fo2ArvillagIntRuntime
{
    private const string SaveSchema = "opennv-fo2-arvillag-int-state/v1";
    private readonly Fo2ArvillagPresentationCatalog _catalog;
    private readonly Fo2ArroyoCavesPlayerBody _player;
    private readonly Fo2CharacterSelection _character;
    private readonly ClassicRetailRandomContract _randomContract;
    private readonly Func<ClassicRetailRandomLifecycleState> _readRandomState;
    private readonly Action<ClassicRetailRandomLifecycleState,
        ClassicRetailRandomLifecycleState> _commitRandomState;
    private readonly Dictionary<string, Fo2ArvillagIntRoleState> _roles;
    private readonly IReadOnlyDictionary<string, Sprite3D> _actors;

    private Fo2ArvillagIntRuntime(
        Fo2ArvillagPresentationCatalog catalog,
        Fo2ArroyoCavesPlayerBody player,
        Fo2CharacterSelection character,
        ClassicRetailRandomContract randomContract,
        Func<ClassicRetailRandomLifecycleState> readRandomState,
        Action<ClassicRetailRandomLifecycleState,
            ClassicRetailRandomLifecycleState> commitRandomState,
        Dictionary<string, Fo2ArvillagIntRoleState> roles,
        IReadOnlyDictionary<string, Sprite3D> actors)
    {
        _catalog = catalog;
        _player = player;
        _character = character;
        _randomContract = randomContract;
        _readRandomState = readRandomState;
        _commitRandomState = commitRandomState;
        _roles = roles;
        _actors = actors;
    }

    internal IReadOnlyDictionary<string, Fo2ArvillagIntRoleState> Roles => _roles;
    internal int PlayerIntelligence =>
        _character.Profile.Special[ClassicIntGameIdentifiers.IntelligenceStat];

    internal static Fo2ArvillagIntRuntime Enter(
        Fo2ArvillagPresentationCatalog catalog,
        Fo2ArvillagSceneCoverage scene,
        Fo2ArroyoCavesPlayerBody player,
        Fo2CharacterSelection character,
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
        var actors = new Dictionary<string, Sprite3D>(StringComparer.Ordinal);
        var sharedGlobals = new Dictionary<int, int>();
        foreach (var initial in catalog.IntRoles.Values.SelectMany(row =>
                     row.InitialGlobalVariables))
        {
            if (sharedGlobals.TryGetValue(initial.Key, out var existing) &&
                existing != initial.Value)
                throw new InvalidOperationException(
                    "Fallout 2 ARVILLAG initial global state conflicts.");
            sharedGlobals[initial.Key] = initial.Value;
        }
        IReadOnlyDictionary<int, int> sharedMapVariables =
            new Dictionary<int, int>();
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
            var objects = new Dictionary<int, ClassicIntWorldObject>
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
            };
            var inventory = new List<ClassicIntInventoryEntry>();
            var reservedHandles = objects.Keys.ToHashSet();
            var nextHandle = 1;
            int AllocateHandle()
            {
                while (reservedHandles.Contains(nextHandle))
                    nextHandle++;
                var result = nextHandle++;
                reservedHandles.Add(result);
                return result;
            }
            foreach (var item in role.InitialInventory.OrderBy(row => row.Serial))
            {
                var objectHandle = AllocateHandle();
                objects.Add(objectHandle, new ClassicIntWorldObject(
                    objectHandle,
                    item.Pid,
                    item.Tile,
                    item.Elevation,
                    false));
                inventory.Add(new ClassicIntInventoryEntry(
                    actorHandle, objectHandle, item.Quantity));
            }
            var creationRequests = new Dictionary<ClassicIntObjectCreationRequest, int>();
            foreach (var creation in role.ObjectCreations
                         .OrderBy(row => row.Procedure, StringComparer.Ordinal)
                         .ThenBy(row => row.Offset))
            {
                var request = new ClassicIntObjectCreationRequest(
                    role.Program.Program,
                    creation.Procedure,
                    creation.Offset,
                    creation.Source);
                if (!creationRequests.TryAdd(request, AllocateHandle()))
                    throw new InvalidOperationException(
                        $"Fallout 2 ARVILLAG {role.Role} object creation is duplicated.");
            }
            var objectFactory = new ClassicIntObjectHandleTable(
                new Dictionary<ClassicIntObjectCreation, int>())
            {
                Requests = creationRequests,
            };
            var procedureState = new ClassicIntProcedureState(
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                sharedMapVariables,
                sharedGlobals,
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
                objectFactory,
                null,
                null,
                null,
                null,
                Fo2ArvillagPresentationCatalog.MapIndex,
                InventoryContract: catalog.InventoryContract);
            var world = new ClassicIntWorldObjectState(
                false,
                new Dictionary<int, ClassicIntDoorObjectState>())
            {
                Objects = objects,
                Inventory = inventory,
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
            sharedGlobals = new Dictionary<int, int>(result.State.GlobalVariables);
            sharedMapVariables = result.State.MapVariables;
            results.Add(role.Role, new Fo2ArvillagIntRoleState(
                role.Role,
                playerHandle,
                actorHandle,
                result.State,
                result.WorldObjects,
                objectFactory));
            actors.Add(role.Role, actor);
        }
        foreach (var roleName in results.Keys.ToArray())
        {
            var roleState = results[roleName];
            results[roleName] = roleState with
            {
                ProcedureState = roleState.ProcedureState with
                {
                    GlobalVariables = sharedGlobals,
                    MapVariables = sharedMapVariables,
                },
            };
        }
        commitRandomState(sourceRandom, random);
        return new Fo2ArvillagIntRuntime(
            catalog, player, character, randomContract, readRandomState,
            commitRandomState, results, actors);
    }

    internal object Save() => new
    {
        schema = SaveSchema,
        sourceManifestSha256 = _catalog.SourceManifestSha256,
        roles = _roles.OrderBy(row => row.Key).Select(row => new
        {
            role = row.Key,
            programSha256 = _catalog.IntRoles[row.Key].Program.Sha256,
            playerHandle = row.Value.PlayerHandle,
            actorHandle = row.Value.ActorHandle,
            procedureState = row.Value.ProcedureState.Save(),
            worldState = row.Value.WorldState.Save(),
        }).ToArray(),
    };

    internal void Restore(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != SaveSchema ||
            source.GetProperty("sourceManifestSha256").GetString() !=
                _catalog.SourceManifestSha256)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG INT save identity drifted.");
        var savedRoles = source.GetProperty("roles").EnumerateArray().ToArray();
        if (savedRoles.Length != _roles.Count)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG INT save role set drifted.");
        var restored = new Dictionary<string, Fo2ArvillagIntRoleState>(
            StringComparer.Ordinal);
        foreach (var saved in savedRoles)
        {
            var roleName = saved.GetProperty("role").GetString() ?? "";
            if (!_roles.TryGetValue(roleName, out var active) ||
                saved.GetProperty("programSha256").GetString() !=
                    _catalog.IntRoles[roleName].Program.Sha256 ||
                saved.GetProperty("playerHandle").GetInt32() != active.PlayerHandle ||
                saved.GetProperty("actorHandle").GetInt32() != active.ActorHandle)
                throw new InvalidOperationException(
                    "Fallout 2 ARVILLAG INT saved role identity drifted.");
            var procedureState = ClassicIntProcedureState.Restore(
                saved.GetProperty("procedureState"), _randomContract);
            var worldState = ClassicIntWorldObjectState.Restore(
                saved.GetProperty("worldState"));
            if (!worldState.Objects.TryGetValue(active.ActorHandle, out var actor) ||
                actor.Pid != active.WorldState.Objects[active.ActorHandle].Pid ||
                actor.Tile != active.WorldState.Objects[active.ActorHandle].Tile ||
                actor.Elevation != active.WorldState.Objects[active.ActorHandle].Elevation)
                throw new InvalidOperationException(
                    "Fallout 2 ARVILLAG INT saved actor identity drifted.");
            _actors[roleName].Visible = actor.Visible;
            restored.Add(roleName, active with
            {
                ProcedureState = procedureState,
                WorldState = worldState,
            });
        }
        if (!restored.Keys.Order().SequenceEqual(_roles.Keys.Order()))
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG INT saved role set is incomplete.");
        var globalStates = restored.Values.Select(row => row.ProcedureState
                .GlobalVariables.OrderBy(value => value.Key).ToArray())
            .ToArray();
        if (globalStates.Skip(1).Any(row => !row.SequenceEqual(globalStates[0])))
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG INT saved global state is not shared.");
        _roles.Clear();
        foreach (var row in restored)
            _roles.Add(row.Key, row.Value);
    }

    internal string LookAt(string roleName)
    {
        var result = Execute(
            roleName,
            "look_at_p_proc",
            prepareDialogue: false,
            resetDialogueStart: false);
        var role = _catalog.IntRoles[roleName];
        if (!result.WorldObjects.ScriptOverrides ||
            result.MessageEffects is not [{ } message] ||
            message.MessageList != role.MessageListId ||
            !role.Messages.TryGetValue(message.MessageId, out var text))
            throw new InvalidOperationException(
                $"Fallout 2 ARVILLAG {roleName} look result drifted.");
        return text;
    }

    internal ClassicIntProcedureResult Talk(string roleName) =>
        Execute(
            roleName,
            "talk_p_proc",
            prepareDialogue: true,
            resetDialogueStart: true);

    internal ClassicIntProcedureResult Choose(
        string roleName,
        ClassicIntDialogueOption option)
    {
        var role = _catalog.IntRoles[roleName];
        if (option.TargetProcedureIndex < 0 ||
            option.TargetProcedureIndex >= role.Program.ExecutableProgram.ProcedureOrder.Count)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG dialogue option target drifted.");
        return Execute(
            roleName,
            role.Program.ExecutableProgram.ProcedureOrder[
                option.TargetProcedureIndex].Name,
            prepareDialogue: true,
            resetDialogueStart: false);
    }

    private ClassicIntProcedureResult Execute(
        string roleName,
        string procedure,
        bool prepareDialogue,
        bool resetDialogueStart)
    {
        var role = _catalog.IntRoles[roleName];
        var source = _roles[roleName];
        var random = _readRandomState();
        var state = source.ProcedureState with { RandomState = random };
        var stats = role.CritterStats.Select((value, index) =>
                (Key: (source.ActorHandle, index), Value: value))
            .ToDictionary(row => row.Key, row => row.Value);
        for (var index = 0; index < _character.Profile.Special.Count; index++)
            stats[(source.PlayerHandle, index)] = _character.Profile.Special[index];
        var playerHandle = source.PlayerHandle;
        stats[(playerHandle, ClassicIntGameIdentifiers.GenderStat)] =
            string.Equals(_character.Profile.Sex, "Female", StringComparison.Ordinal)
                ? 1
                : 0;
        var traits = _character.Profile.Traits.Select(name =>
        {
            var index = Array.IndexOf(Fo2CharacterStartCatalog.TraitNames, name);
            if (index < 0)
                throw new InvalidOperationException(
                    $"Fallout 2 selected trait is unavailable: {name}.");
            return (ClassicIntGameIdentifiers.CharacterTraitType, playerHandle, index);
        }).ToDictionary(row => row, _ => 1);
        var messages = role.Messages.Keys.ToDictionary(
            messageId => (role.MessageListId, messageId),
            messageId => messageId);
        var world = prepareDialogue
            ? source.WorldState with
            {
                DialogueStart = resetDialogueStart
                    ? null
                    : source.WorldState.DialogueStart,
                DialogueReplies = [],
                DialogueOptions = [],
                DialogueReady = resetDialogueStart
                    ? false
                    : source.WorldState.DialogueReady,
            }
            : source.WorldState;
        var context = new ClassicIntExpressionContext(
            state.ProgramVariables,
            state.LocalVariables,
            state.ScriptLocalVariables,
            state.MapVariables,
            state.GlobalVariables,
            playerHandle,
            source.ActorHandle,
            null,
            null,
            stats,
            new Dictionary<(int, int), int>(),
            new Dictionary<int, int>(),
            messages,
            new Dictionary<string, int>(),
            ClassicIntTimerState.Initial.CurrentTick,
            null,
            null,
            source.ObjectFactory,
            CurrentMapIndex: Fo2ArvillagPresentationCatalog.MapIndex,
            Traits: traits,
            InventoryContract: _catalog.InventoryContract);
        var budget = role.Program.ExecutableProgram.Procedures.Values
            .Sum(row => row.Instructions.Count);
        var result = ClassicIntEventDispatcher.Execute(
            role.Program, procedure, state, context, world, _randomContract, budget);
        _commitRandomState(random, result.State.RandomState);
        _roles[roleName] = source with
        {
            ProcedureState = result.State,
            WorldState = result.WorldObjects,
        };
        foreach (var otherRole in _roles.Keys.Where(name => name != roleName).ToArray())
        {
            var other = _roles[otherRole];
            _roles[otherRole] = other with
            {
                ProcedureState = other.ProcedureState with
                {
                    GlobalVariables = result.State.GlobalVariables,
                    MapVariables = result.State.MapVariables,
                },
            };
        }
        return result;
    }

    private static int ParsePid(string source) => unchecked((int)uint.Parse(
        source,
        NumberStyles.AllowHexSpecifier,
        CultureInfo.InvariantCulture));
}
