using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;

using var document = JsonDocument.Parse("""
    {
      "objects": {
        "totalTopLevelObjects": 2,
        "elevations": [
          {
            "elevation": 0,
            "objects": [
              {
                "sourceOffset": 100,
                "serial": 2,
                "elevation": 0,
                "sid": "04000001",
                "scriptIndex": 750,
                "inventory": [
                  {
                    "object": {
                      "sourceOffset": 160,
                      "serial": 1,
                      "elevation": 0,
                      "sid": "ffffffff",
                      "scriptIndex": -1,
                      "inventory": []
                    }
                  }
                ]
              },
              {
                "sourceOffset": 220,
                "serial": 3,
                "elevation": 0,
                "sid": "ffffffff",
                "scriptIndex": -1,
                "inventory": []
              }
            ]
          }
        ]
      },
      "scriptLists": [
        {
          "type": 4,
          "extentCount": 1,
          "liveCount": 1,
          "extents": [
            {
              "index": 0,
              "length": 1,
              "slots": [
                { "slot": 0, "sid": "04000001" },
                { "slot": 1, "sid": "cccccccc" }
              ]
            }
          ]
        }
      ]
    }
    """);

var initialization = ClassicMapInitializationOwner.Parse(document.RootElement);
if (!initialization.Objects.Select(row => row.SourceOffset).SequenceEqual([100, 160, 220]) ||
    !initialization.Objects.Select(row => row.Serial).SequenceEqual([2, 1, 3]) ||
    initialization.Objects[1].InventoryDepth != 1 ||
    initialization.ScriptSlots.Count != 1 ||
    initialization.ScriptSlots[0].Sid != "04000001" ||
    initialization.ScriptedObjects.Single().Serial != 2)
    throw new InvalidOperationException(
        "Classic MAP source initialization ordering or live-slot join drifted.");

using var intDocument = JsonDocument.Parse("""
    {
      "schema": "opennv-classic-map-int-initialization/v1",
      "engineInterleavingTransported": false,
      "mapHeader": {
        "storedScriptIndex": 0,
        "indexSemantics": "MAP-header-zero-means-no-program",
        "program": null
      },
      "liveScriptSlots": [{
        "order": 0,
        "type": 4,
        "extent": 0,
        "slot": 0,
        "sid": "04000001",
        "scriptIndex": 750,
        "program": {
          "scriptsListIndex": 750,
          "program": "ACKlint.int",
          "logicalPath": "scripts\\acklint.int",
          "sha256": "1111111111111111111111111111111111111111111111111111111111111111",
          "inventory": {
            "schema": "opennv-classic-int-initialization-inventory/v3",
            "randomOpcode": "80b4",
            "procedures": [{
              "name": "map_enter_p_proc",
              "bodyOffset": 200,
              "bodyEndOffset": 260,
              "canonicalEpilogueOffset": null,
              "instructions": [
                { "offset": 200, "opcode": "802b", "operand": null },
                { "offset": 202, "opcode": "c001", "operand": 236 },
                { "offset": 208, "opcode": "c001", "operand": 0 },
                { "offset": 214, "opcode": "802f", "operand": null },
                { "offset": 216, "opcode": "c001", "operand": 99 },
                { "offset": 222, "opcode": "c001", "operand": 3 },
                { "offset": 228, "opcode": "8013", "operand": null },
                { "offset": 236, "opcode": "c001", "operand": 9 },
                { "offset": 242, "opcode": "c001", "operand": 3 },
                { "offset": 248, "opcode": "8013", "operand": null },
                { "offset": 250, "opcode": "801c", "operand": null }
              ]
            }],
            "randomSites": [{
              "procedure": "map_enter_p_proc",
              "offset": 212,
              "operandKind": "literal-inclusive-range",
              "minimum": 1,
              "maximum": 5,
              "expressionStatus": "executable",
              "unsupported": null,
              "minimumExpression": {
                "kind": "literal", "offset": 200, "value": 1, "arguments": []
              },
              "maximumExpression": {
                "kind": "literal", "offset": 206, "value": 5, "arguments": []
              }
            }, {
              "procedure": "map_enter_p_proc",
              "offset": 224,
              "operandKind": "source-stack-expression",
              "minimum": null,
              "maximum": null,
              "expressionStatus": "executable",
              "unsupported": null,
              "minimumExpression": {
                "kind": "literal", "offset": 212, "value": 1, "arguments": []
              },
              "maximumExpression": {
                "kind": "critter-stat", "offset": 220, "value": null,
                "arguments": [
                  { "kind": "dude-object", "offset": 214, "value": null, "arguments": [] },
                  { "kind": "literal", "offset": 216, "value": 6, "arguments": [] }
                ]
              }
            }]
          }
        }
      }],
      "randomSites": [{
        "owner": "live-map-script-slot",
        "sid": "04000001",
        "program": "ACKlint.int",
        "procedure": "map_enter_p_proc",
        "offset": 212,
        "operandKind": "literal-inclusive-range",
        "minimum": 1,
        "maximum": 5,
        "expressionStatus": "executable",
        "unsupported": null,
        "minimumExpression": {
          "kind": "literal", "offset": 200, "value": 1, "arguments": []
        },
        "maximumExpression": {
          "kind": "literal", "offset": 206, "value": 5, "arguments": []
        }
      }, {
        "owner": "live-map-script-slot",
        "sid": "04000001",
        "program": "ACKlint.int",
        "procedure": "map_enter_p_proc",
        "offset": 224,
        "operandKind": "source-stack-expression",
        "minimum": null,
        "maximum": null,
        "expressionStatus": "executable",
        "unsupported": null,
        "minimumExpression": {
          "kind": "literal", "offset": 212, "value": 1, "arguments": []
        },
        "maximumExpression": {
          "kind": "critter-stat", "offset": 220, "value": null,
          "arguments": [
            { "kind": "dude-object", "offset": 214, "value": null, "arguments": [] },
            { "kind": "literal", "offset": 216, "value": 6, "arguments": [] }
          ]
        }
      }]
    }
    """);
var intInitialization = ClassicMapIntInitializationOwner.Parse(
    intDocument.RootElement,
    initialization);
if (intInitialization.EngineInterleavingTransported ||
    intInitialization.HeaderProgram is not null ||
    intInitialization.ScriptSlots.Single().Program.ScriptsListIndex != 750 ||
    intInitialization.RandomSites.Count != 2 ||
    intInitialization.RandomSites[0].Minimum != 1 ||
    intInitialization.RandomSites[0].Maximum != 5 ||
    intInitialization.RandomSites[1].OperandKind != "source-stack-expression" ||
    intInitialization.RandomSites[1].Minimum is not null ||
    intInitialization.RandomSites[1].Maximum is not null)
    throw new InvalidOperationException(
        "Classic MAP INT initialization contract drifted.");

using var randomDocument = JsonDocument.Parse(File.ReadAllBytes(
    Path.Combine("runtime", "config", "classic-retail-random-fo2-1.02-v1.json")));
var randomContract = ClassicRetailRandomContract.Parse(randomDocument.RootElement);
var randomState = ClassicRetailRandomLifecycle.Initialize(1, randomContract);
var messageHandles = Enumerable.Range(100, 24).ToDictionary(
    messageId => (MessageList: 344, MessageId: messageId),
    messageId => 9000 + messageId);
var gameContext = new ClassicIntExpressionContext(
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        100,
        200,
        1,
        1,
        new Dictionary<(int, int), int> { [(100, 6)] = 3 },
        new Dictionary<(int, int), int>(),
        new Dictionary<int, int>(),
        messageHandles,
        new Dictionary<string, int>(),
        0,
        0,
        0,
        new ClassicIntObjectHandleTable(
            new Dictionary<ClassicIntObjectCreation, int>()));
var expressionResult = ClassicIntExpressionOwner.EvaluateRandomSite(
    intInitialization.RandomSites[1],
    gameContext,
    randomState,
    randomContract);
if (expressionResult.Value is < 1 or > 3 ||
    expressionResult.RandomState.Events.Count != randomState.Events.Count + 1)
    throw new InvalidOperationException(
        "Classic INT source expression evaluation or RANDOM consumption drifted.");

var procedureProgram = ClassicIntProcedureVm.Parse(
    intDocument.RootElement.GetProperty("liveScriptSlots")[0]
        .GetProperty("program").GetProperty("inventory"),
    "ACKlint.int");
var procedureResult = ClassicIntProcedureVm.Execute(
    procedureProgram,
    "map_enter_p_proc",
    new ClassicIntProcedureState(
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        [],
        randomState),
    gameContext,
    ClassicIntWorldObjectState.Empty,
    randomContract,
    procedureProgram.Procedures["map_enter_p_proc"].Instructions.Count);
if (procedureResult.State.ProgramVariables[3] != 9 ||
    procedureResult.ExecutedInstructions != 8)
    throw new InvalidOperationException(
        "Classic INT procedure stack/store/return execution drifted.");

using var callDocument = JsonDocument.Parse("""
    { "procedures": [{
      "name": "caller", "bodyOffset": 100, "canonicalEpilogueOffset": 132,
      "instructions": [
        { "offset": 100, "opcode": "802b", "operand": null },
        { "offset": 102, "opcode": "c001", "operand": 124 },
        { "offset": 108, "opcode": "800d", "operand": null },
        { "offset": 110, "opcode": "c001", "operand": 0 },
        { "offset": 116, "opcode": "c001", "operand": 1 },
        { "offset": 122, "opcode": "8005", "operand": null },
        { "offset": 124, "opcode": "c001", "operand": 7 },
        { "offset": 130, "opcode": "8013", "operand": null },
        { "offset": 132, "opcode": "c001", "operand": 0 },
        { "offset": 138, "opcode": "800d", "operand": null },
        { "offset": 140, "opcode": "8019", "operand": null },
        { "offset": 142, "opcode": "802a", "operand": null },
        { "offset": 144, "opcode": "8029", "operand": null },
        { "offset": 146, "opcode": "800c", "operand": null },
        { "offset": 148, "opcode": "801c", "operand": null },
        { "offset": 150, "opcode": "802a", "operand": null },
        { "offset": 152, "opcode": "8029", "operand": null },
        { "offset": 154, "opcode": "801c", "operand": null }
      ]
    }, {
      "name": "callee", "bodyOffset": 200, "canonicalEpilogueOffset": 202,
      "instructions": [
        { "offset": 200, "opcode": "802b", "operand": null },
        { "offset": 202, "opcode": "c001", "operand": 0 },
        { "offset": 208, "opcode": "800d", "operand": null },
        { "offset": 210, "opcode": "8019", "operand": null },
        { "offset": 212, "opcode": "802a", "operand": null },
        { "offset": 214, "opcode": "8029", "operand": null },
        { "offset": 216, "opcode": "800c", "operand": null },
        { "offset": 218, "opcode": "801c", "operand": null },
        { "offset": 220, "opcode": "802a", "operand": null },
        { "offset": 222, "opcode": "8029", "operand": null },
        { "offset": 224, "opcode": "801c", "operand": null }
      ]
    }] }
    """);
var callProgram = ClassicIntProcedureVm.Parse(callDocument.RootElement, "abi.int");
var callResult = ClassicIntProcedureVm.Execute(
    callProgram, "caller",
    new ClassicIntProcedureState(
        new Dictionary<int, int>(), new Dictionary<int, int>(),
        new Dictionary<int, int>(), new Dictionary<int, int>(),
        new Dictionary<int, int>(), [], randomState),
    gameContext, ClassicIntWorldObjectState.Empty, randomContract, 29);
if (callResult.State.ProgramVariables[7] != 0 ||
    callResult.ExecutedInstructions != 29 || callResult.ReturnValue != 0)
    throw new InvalidOperationException(
        "Classic INT call/D-A return ABI execution drifted.");

using var missingScript = JsonDocument.Parse("""
    {
      "objects": {
        "totalTopLevelObjects": 1,
        "elevations": [{
          "elevation": 0,
          "objects": [{
            "sourceOffset": 100,
            "serial": 1,
            "elevation": 0,
            "sid": "04000001",
            "scriptIndex": 750,
            "inventory": []
          }]
        }]
      },
      "scriptLists": []
    }
    """);
try
{
    _ = ClassicMapInitializationOwner.Parse(missingScript.RootElement);
    throw new InvalidOperationException("Missing live script slot was accepted.");
}
catch (InvalidOperationException exception) when (
    exception.Message.StartsWith(
        "Classic MAP scripted objects have no live script slot",
        StringComparison.Ordinal))
{
}

foreach (var path in args)
{
    using var ownedDocument = JsonDocument.Parse(File.ReadAllBytes(path));
    if (ownedDocument.RootElement.TryGetProperty(
            "initializationScripts",
            out var ownedScripts))
    {
        var programs = ownedScripts.GetProperty("liveScriptSlots").EnumerateArray()
            .Select(row => row.GetProperty("program")).ToArray();
        if (ownedDocument.RootElement.TryGetProperty("map", out var ownedMapSource) &&
            ownedMapSource.TryGetProperty("objects", out _))
        {
            var ownedMap = ClassicMapInitializationOwner.Parse(ownedMapSource);
            var parsedInitialization = ClassicMapIntInitializationOwner.Parse(
                ownedScripts, ownedMap);
            if (parsedInitialization.HeaderProgram is { } header &&
                string.Equals(header.Program, "ArCaves.int",
                    StringComparison.OrdinalIgnoreCase) &&
                header.ExecutableProgram.Procedures.ContainsKey("map_enter_p_proc"))
            {
                var arcavesContext = gameContext with
                {
                    MetaruleValues = new Dictionary<(int, int), int>
                    {
                        [(14, 0)] = 1,
                    },
                    MessageHandles = new Dictionary<(int, int), int>
                    {
                        [(25, 100)] = 25100,
                    },
                };
                var arcavesState = new ClassicIntProcedureState(
                    new Dictionary<int, int>(), new Dictionary<int, int>(),
                    new Dictionary<int, int>(), new Dictionary<int, int>(),
                    new Dictionary<int, int> { [27] = 99 }, [], randomState);
                var instructionBudget = header.ExecutableProgram
                    .Procedures["map_enter_p_proc"].Instructions.Count;
                var entered = ClassicIntEventDispatcher.Execute(
                    header, "map_enter_p_proc", arcavesState, arcavesContext,
                    ClassicIntWorldObjectState.Empty, randomContract,
                    instructionBudget);
                if (entered.ExecutedInstructions != instructionBudget ||
                    entered.State.GlobalVariables[27] != 0 ||
                    entered.WorldObjects.LightLevel != 50 ||
                    entered.MessageEffects is not
                        [{ MessageList: 25, MessageId: 100 }])
                    throw new InvalidOperationException(
                        "Owned classic header map-enter effects drifted.");
                var restoredWorld = ClassicIntWorldObjectState.Restore(
                    JsonSerializer.SerializeToElement(entered.WorldObjects.Save()));
                if (restoredWorld.LightLevel != 50)
                    throw new InvalidOperationException(
                        "Owned classic map light save state drifted.");
                var hiddenMessage = ClassicIntEventDispatcher.Execute(
                    header, "map_enter_p_proc", arcavesState,
                    arcavesContext with
                    {
                        MetaruleValues = new Dictionary<(int, int), int>
                        {
                            [(14, 0)] = 0,
                        },
                    },
                    ClassicIntWorldObjectState.Empty, randomContract,
                    instructionBudget);
                if (hiddenMessage.MessageEffects.Count != 0 ||
                    hiddenMessage.WorldObjects.LightLevel != 50)
                    throw new InvalidOperationException(
                        "Owned classic conditional map-enter effects drifted.");
                Console.WriteLine(
                    $"{Path.GetFileName(path)}|{header.Program}|map_enter_p_proc|" +
                    $"{entered.ExecutedInstructions}");
            }
            if (parsedInitialization.HeaderProgram is { } templeHeader &&
                string.Equals(templeHeader.Program, "ARTemple.int",
                    StringComparison.OrdinalIgnoreCase))
            {
                var creation = new ClassicIntObjectCreation(7, 0, 0, -1);
                const int createdObjectHandle = 7000;
                var initialInventoryContext = gameContext with
                {
                    ObjectFactory = new ClassicIntObjectHandleTable(
                        new Dictionary<ClassicIntObjectCreation, int>
                        {
                            [creation] = createdObjectHandle,
                        }),
                };
                var initialInventoryState = new ClassicIntProcedureState(
                    new Dictionary<int, int>(), new Dictionary<int, int>(),
                    new Dictionary<int, int>(), new Dictionary<int, int>(),
                    new Dictionary<int, int>(), [], randomState);
                var instructionBudget = templeHeader.ExecutableProgram
                    .Procedures["Initial_Inven"].Instructions.Count;
                var inventoryResult = ClassicIntEventDispatcher.Execute(
                    templeHeader, "Initial_Inven", initialInventoryState,
                    initialInventoryContext, ClassicIntWorldObjectState.Empty,
                    randomContract, instructionBudget);
                if (inventoryResult.ExecutedInstructions != instructionBudget ||
                    inventoryResult.State.LocalVariables[0] != createdObjectHandle ||
                    inventoryResult.WorldObjects.CreatedObjects[createdObjectHandle]
                        .Source != creation ||
                    inventoryResult.WorldObjects.Inventory is not
                        [{ OwnerHandle: 100, ObjectHandle: createdObjectHandle, Quantity: 1 }])
                    throw new InvalidOperationException(
                        "Owned Temple initial inventory effects drifted.");
                var restoredInventory = ClassicIntWorldObjectState.Restore(
                    JsonSerializer.SerializeToElement(
                        inventoryResult.WorldObjects.Save()));
                if (restoredInventory.CreatedObjects[createdObjectHandle]
                        .Source != creation ||
                    restoredInventory.Inventory is not
                        [{ OwnerHandle: 100, ObjectHandle: createdObjectHandle, Quantity: 1 }])
                    throw new InvalidOperationException(
                        "Owned Temple initial inventory save state drifted.");
                Console.WriteLine(
                    $"{Path.GetFileName(path)}|{templeHeader.Program}|Initial_Inven|" +
                    $"{inventoryResult.ExecutedInstructions}");

                var mapEnterContext = initialInventoryContext with
                {
                    MetaruleValues = new Dictionary<(int, int), int>
                    {
                        [(14, 0)] = 1,
                    },
                    GameTimeHour = 1200,
                    Month = 1,
                };
                var mapEnterState = initialInventoryState with
                {
                    GlobalVariables = new Dictionary<int, int> { [27] = 99 },
                };
                var mapEnterBudget = templeHeader.ExecutableProgram
                    .Procedures["map_enter_p_proc"].Instructions.Count +
                    instructionBudget;
                var mapEntered = ClassicIntEventDispatcher.Execute(
                    templeHeader, "map_enter_p_proc", mapEnterState,
                    mapEnterContext, ClassicIntWorldObjectState.Empty,
                    randomContract, mapEnterBudget);
                if (mapEntered.State.GlobalVariables[27] != 0 ||
                    mapEntered.WorldObjects.LightLevel != 100 ||
                    mapEntered.WorldObjects.MapStartOverride is not
                    { TileX: 88, TileY: 87, Elevation: 0, Rotation: 5 } ||
                    mapEntered.WorldObjects.Inventory is not
                        [{ OwnerHandle: 100, ObjectHandle: createdObjectHandle, Quantity: 1 }])
                    throw new InvalidOperationException(
                        "Owned Temple map-enter procedure chain drifted: " +
                        JsonSerializer.Serialize(mapEntered.WorldObjects.Save()));
                Console.WriteLine(
                    $"{Path.GetFileName(path)}|{templeHeader.Program}|map_enter_p_proc|" +
                    $"{mapEntered.ExecutedInstructions}");

                var updateBudget = templeHeader.ExecutableProgram
                    .Procedures["map_update_p_proc"].Instructions.Count;
                var mapUpdated = ClassicIntEventDispatcher.Execute(
                    templeHeader, "map_update_p_proc", mapEntered.State,
                    mapEnterContext, mapEntered.WorldObjects,
                    randomContract, updateBudget);
                if (mapUpdated.WorldObjects.LightLevel != 100 ||
                    !mapUpdated.WorldObjects.Inventory.SequenceEqual(
                        mapEntered.WorldObjects.Inventory) ||
                    mapUpdated.WorldObjects.MapStartOverride !=
                        mapEntered.WorldObjects.MapStartOverride)
                    throw new InvalidOperationException(
                        "Owned Temple map-update procedure drifted.");
                Console.WriteLine(
                    $"{Path.GetFileName(path)}|{templeHeader.Program}|map_update_p_proc|" +
                    $"{mapUpdated.ExecutedInstructions}");
            }
            var lintGuardian = parsedInitialization.ScriptSlots.SingleOrDefault(row =>
                string.Equals(row.Program.Program, "ACKlint.int",
                    StringComparison.OrdinalIgnoreCase));
            if (lintGuardian is not null)
            {
                var playerActor = new object();
                var guardianActor = new object();
                var actorHandles = new ClassicIntActorHandleTable();
                var playerHandle = actorHandles.Register(playerActor);
                var guardianHandle = actorHandles.Register(guardianActor);
                if (playerHandle == guardianHandle ||
                    actorHandles.Register(playerActor) != playerHandle ||
                    actorHandles.Require(guardianActor) != guardianHandle)
                    throw new InvalidOperationException(
                        "Classic live actor handle registration drifted.");
                var guardianContext = gameContext with
                {
                    DudeObject = playerHandle,
                    SelfObject = guardianHandle,
                };
                var guardianState = new ClassicIntProcedureState(
                    new Dictionary<int, int>(), new Dictionary<int, int>(),
                    new Dictionary<int, int> { [5] = 0 },
                    new Dictionary<int, int>(), new Dictionary<int, int>(), [],
                    randomState);
                var pickupBudget = lintGuardian.Program.ExecutableProgram
                    .Procedures["pickup_p_proc"].Instructions.Count;
                var ignoredPickup = ClassicIntEventDispatcher.Execute(
                    lintGuardian.Program, "pickup_p_proc", guardianState,
                    guardianContext with { SourceObject = guardianHandle },
                    ClassicIntWorldObjectState.Empty, randomContract, pickupBudget);
                if (ignoredPickup.State.ScriptLocalVariables[5] != 0 ||
                    ignoredPickup.WorldObjects.AttackRequests.Count != 0)
                    throw new InvalidOperationException(
                        "Owned Temple guardian non-player pickup branch drifted.");

                var playerPickup = ClassicIntEventDispatcher.Execute(
                    lintGuardian.Program, "pickup_p_proc", guardianState,
                    guardianContext with { SourceObject = playerHandle },
                    ClassicIntWorldObjectState.Empty, randomContract, pickupBudget);
                if (playerPickup.State.ScriptLocalVariables[5] != 2 ||
                    playerPickup.WorldObjects.AttackRequests.Count != 0)
                    throw new InvalidOperationException(
                        "Owned Temple guardian player pickup branch drifted.");
                var restoredGuardianState = ClassicIntProcedureState.Restore(
                    JsonSerializer.SerializeToElement(playerPickup.State.Save()),
                    randomContract);
                var critterBudget = lintGuardian.Program.ExecutableProgram
                    .Procedures["critter_p_proc"].Instructions.Count;
                var hiddenPlayer = ClassicIntEventDispatcher.Execute(
                    lintGuardian.Program, "critter_p_proc", restoredGuardianState,
                    guardianContext with
                    {
                        SelfObject = guardianHandle,
                        ActorQueries = new ClassicIntActorQueryTable(
                            new Dictionary<(int, int), bool>
                            {
                                [(guardianHandle, playerHandle)] = false,
                            }),
                    },
                    playerPickup.WorldObjects, randomContract, critterBudget);
                if (hiddenPlayer.State.ScriptLocalVariables[5] != 2 ||
                    hiddenPlayer.WorldObjects.AttackRequests.Count != 0)
                    throw new InvalidOperationException(
                        "Owned Temple guardian hidden-player branch drifted.");

                var attackStarted = ClassicIntEventDispatcher.Execute(
                    lintGuardian.Program, "critter_p_proc", restoredGuardianState,
                    guardianContext with
                    {
                        SelfObject = guardianHandle,
                        ActorQueries = new ClassicIntActorQueryTable(
                            new Dictionary<(int, int), bool>
                            {
                                [(guardianHandle, playerHandle)] = true,
                            }),
                    },
                    playerPickup.WorldObjects, randomContract, critterBudget);
                if (attackStarted.State.ScriptLocalVariables[5] != 1 ||
                    attackStarted.WorldObjects.AttackRequests is not
                    [{ } attack] ||
                    attack.ActorHandle != guardianHandle ||
                    attack.TargetHandle != playerHandle ||
                    !attack.SourceArguments.SequenceEqual(
                        new[] { 0, 1, 0, 0, 30000, 0, 0 }))
                    throw new InvalidOperationException(
                        "Owned Temple guardian attack request drifted.");
                var restoredCombat = ClassicIntWorldObjectState.Restore(
                    JsonSerializer.SerializeToElement(
                        attackStarted.WorldObjects.Save()));
                if (restoredCombat.AttackRequests is not
                    [{ } restoredAttack] ||
                    restoredAttack.ActorHandle != guardianHandle ||
                    restoredAttack.TargetHandle != playerHandle ||
                    !restoredAttack.SourceArguments.SequenceEqual(
                        attack.SourceArguments))
                    throw new InvalidOperationException(
                        "Owned Temple guardian combat save state drifted.");
                Console.WriteLine(
                    $"{Path.GetFileName(path)}|{lintGuardian.Program.Program}|" +
                    $"pickup_p_proc+critter_p_proc|" +
                    $"{playerPickup.ExecutedInstructions + attackStarted.ExecutedInstructions}");
            }
            var cameron = parsedInitialization.ScriptSlots.SingleOrDefault(row =>
                string.Equals(row.Program.Program, "ACTemVil.int",
                    StringComparison.OrdinalIgnoreCase));
            if (cameron is not null)
            {
                const string terminalTaggedSpeechProcedure = "Node015a";
                var sourceState = new ClassicIntProcedureState(
                    new Dictionary<int, int>(), new Dictionary<int, int>(),
                    new Dictionary<int, int>(), new Dictionary<int, int>(),
                    new Dictionary<int, int>(), [], randomState);
                var instructionBudget = cameron.Program.ExecutableProgram
                    .Procedures[terminalTaggedSpeechProcedure].Instructions.Count;
                var completed = ClassicIntEventDispatcher.Execute(
                    cameron.Program,
                    terminalTaggedSpeechProcedure,
                    sourceState,
                    gameContext,
                    ClassicIntWorldObjectState.Empty,
                    randomContract,
                    instructionBudget);
                if (completed.ExecutedInstructions != instructionBudget ||
                    completed.State.ScriptLocalVariables.Count != 2 ||
                    completed.State.ScriptLocalVariables[12] != 1 ||
                    completed.State.ScriptLocalVariables[13] != 2 ||
                    completed.State.MapVariables is not { Count: 1 } ||
                    completed.State.MapVariables[20] != 2 ||
                    completed.State.GlobalVariables is not { Count: 1 } ||
                    completed.State.GlobalVariables[10] != 2 ||
                    completed.State.ValueStack.Count != 0 ||
                    completed.State.RandomState != randomState)
                    throw new InvalidOperationException(
                        "Owned Cameron tagged-Speech result drifted.");
                var restored = ClassicIntProcedureState.Restore(
                    JsonSerializer.SerializeToElement(completed.State.Save()),
                    randomContract);
                if (restored.ScriptLocalVariables[12] != 1 ||
                    restored.ScriptLocalVariables[13] != 2 ||
                    restored.MapVariables[20] != 2 ||
                    restored.GlobalVariables[10] != 2 ||
                    restored.ValueStack.Count != 0 ||
                    restored.RandomState.SeedState !=
                        completed.State.RandomState.SeedState ||
                    restored.RandomState.RandomState.Seed !=
                        completed.State.RandomState.RandomState.Seed ||
                    restored.RandomState.RandomState.Selector !=
                        completed.State.RandomState.RandomState.Selector ||
                    !restored.RandomState.RandomState.Shuffle.SequenceEqual(
                        completed.State.RandomState.RandomState.Shuffle) ||
                    restored.RandomState.Events.Count !=
                        completed.State.RandomState.Events.Count)
                    throw new InvalidOperationException(
                        "Owned Cameron dialogue result save state drifted.");
                Console.WriteLine(
                    $"{Path.GetFileName(path)}|{cameron.Program.Program}|" +
                    $"{terminalTaggedSpeechProcedure}|{completed.ExecutedInstructions}");
            }
        }
        var jasmine = programs.FirstOrDefault(row => string.Equals(
            row.GetProperty("program").GetString(), "jasmine.int",
            StringComparison.OrdinalIgnoreCase));
        if (jasmine.ValueKind != JsonValueKind.Undefined)
        {
            var ownedProgram = ClassicIntProcedureVm.Parse(
                jasmine.GetProperty("inventory"), "jasmine.int");
            var ownedResult = ExecuteOwned(ownedProgram, "map_enter_p_proc");
            if (ownedResult.State.ProgramVariables[4] != 0)
                throw new InvalidOperationException(
                    "Owned classic INT procedure mutation drifted.");
            Console.WriteLine(
                $"{Path.GetFileName(path)}|jasmine.int|map_enter_p_proc|" +
                $"{ownedResult.ExecutedInstructions}");
        }
        var v13Computer = programs.FirstOrDefault(row => string.Equals(
            row.GetProperty("program").GetString(), "V13Comp.int",
            StringComparison.OrdinalIgnoreCase));
        if (v13Computer.ValueKind != JsonValueKind.Undefined)
        {
            var ownedProgram = ClassicIntProcedureVm.Parse(
                v13Computer.GetProperty("inventory"), "V13Comp.int");
            var ownedResult = ExecuteOwned(ownedProgram, "description_p_proc");
            if (ownedResult.ExecutedInstructions != 15 ||
                ownedResult.ReturnValue != 0 ||
                ownedResult.MessageEffects is not
                    [{ MessageList: 344, MessageId: 103, MessageHandle: 9103 }])
                throw new InvalidOperationException(
                    "Owned V13 computer description effect drifted.");
            Console.WriteLine(
                $"{Path.GetFileName(path)}|V13Comp.int|description_p_proc|" +
                $"{ownedResult.ExecutedInstructions}");

            const int v13DoorHandle = 42;
            var useContext = gameContext with
            {
                ExternalVariables = new Dictionary<string, int>
                {
                    ["vault_door_ptr"] = v13DoorHandle,
                },
                GameTime = 864000,
            };
            var useState = new ClassicIntProcedureState(
                new Dictionary<int, int>(),
                new Dictionary<int, int>(),
                new Dictionary<int, int> { [0] = 0, [1] = 0 },
                new Dictionary<int, int>(),
                new Dictionary<int, int> { [1409] = 0, [1618] = 0 },
                [],
                randomState);
            var closed = new ClassicIntWorldObjectState(
                false,
                new Dictionary<int, ClassicIntDoorObjectState>
                {
                    [v13DoorHandle] = new(false, true),
                });
            var overridden = ClassicIntProcedureVm.Execute(
                ownedProgram, "use_p_proc",
                useState with
                {
                    ScriptLocalVariables = new Dictionary<int, int>
                    {
                        [0] = 0,
                        [1] = 1,
                    },
                },
                useContext, closed, randomContract,
                ownedProgram.Procedures["use_p_proc"].Instructions.Count);
            if (!overridden.WorldObjects.ScriptOverrides ||
                overridden.MessageEffects is not [{ MessageId: 123 }] ||
                overridden.SoundEffects.Count != 0)
                throw new InvalidOperationException(
                    "Owned V13 computer override-message branch drifted.");

            var recentContext = useContext with { GameTime = 0 };
            var firstRecent = ClassicIntProcedureVm.Execute(
                ownedProgram, "use_p_proc", useState, recentContext, closed,
                randomContract,
                ownedProgram.Procedures["use_p_proc"].Instructions.Count);
            if (firstRecent.State.ScriptLocalVariables[0] != 1 ||
                firstRecent.MessageEffects is not [{ MessageId: 100 }])
                throw new InvalidOperationException(
                    "Owned V13 computer first-use message branch drifted.");
            var secondRecent = ClassicIntProcedureVm.Execute(
                ownedProgram, "use_p_proc", firstRecent.State, recentContext,
                firstRecent.WorldObjects, randomContract,
                ownedProgram.Procedures["use_p_proc"].Instructions.Count);
            if (secondRecent.MessageEffects is not
                [{ MessageId: 101 }, { MessageId: 102, ObjectHandle: 200, Color: 0 }])
                throw new InvalidOperationException(
                    "Owned V13 computer repeated-use message branch drifted.");

            var opened = ClassicIntProcedureVm.Execute(
                ownedProgram, "use_p_proc", useState, useContext, closed,
                randomContract,
                ownedProgram.Procedures["use_p_proc"].Instructions.Count);
            if (!opened.WorldObjects.ScriptOverrides ||
                opened.WorldObjects.Doors[v13DoorHandle] is not
                { Open: true, Locked: false } ||
                opened.SoundEffects is not ["SLDOORSO"])
                throw new InvalidOperationException(
                    "Owned V13 computer door-open effect drifted.");
            var restoredWorld = ClassicIntWorldObjectState.Restore(
                JsonSerializer.SerializeToElement(opened.WorldObjects.Save()));
            var closedAgain = ClassicIntProcedureVm.Execute(
                ownedProgram, "use_p_proc", opened.State, useContext,
                restoredWorld, randomContract,
                ownedProgram.Procedures["use_p_proc"].Instructions.Count);
            if (closedAgain.WorldObjects.Doors[v13DoorHandle] is not
                { Open: false, Locked: true } ||
                closedAgain.SoundEffects is not ["SLDOORSO"])
                throw new InvalidOperationException(
                    "Owned V13 computer door-close effect drifted.");

            var banished = ClassicIntProcedureVm.Execute(
                ownedProgram, "use_p_proc",
                useState with
                {
                    GlobalVariables = new Dictionary<int, int>
                    {
                        [1409] = 0,
                        [1618] = 1,
                        [1619] = 0,
                    },
                },
                useContext, closed, randomContract,
                ownedProgram.Procedures["use_p_proc"].Instructions.Count +
                ownedProgram.Procedures["Banished"].Instructions.Count);
            if (banished.MessageEffects is not
                [
                { MessageId: 101 },
                { MessageId: >= 104 and <= 108, ObjectHandle: 200, Color: 8 },
                ])
                throw new InvalidOperationException(
                    "Owned V13 computer procedure-call message branch drifted: " +
                    JsonSerializer.Serialize(banished.MessageEffects));
            Console.WriteLine(
                $"{Path.GetFileName(path)}|V13Comp.int|use_p_proc|" +
                $"{opened.ExecutedInstructions}|{closedAgain.ExecutedInstructions}");
        }
        continue;
    }
    var owned = ClassicMapInitializationOwner.Parse(
        ownedDocument.RootElement.GetProperty("map"));
    Console.WriteLine(
        $"{Path.GetFileName(path)}|{owned.Objects.Count}|" +
        $"{owned.ScriptedObjects.Count}|{owned.ScriptSlots.Count}");
}

ClassicIntProcedureResult ExecuteOwned(
    ClassicIntProgram ownedProgram,
    string procedure) => ClassicIntProcedureVm.Execute(
    ownedProgram,
    procedure,
    new ClassicIntProcedureState(
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        [],
        randomState),
    gameContext,
    ClassicIntWorldObjectState.Empty,
    randomContract,
    ownedProgram.Procedures[procedure].Instructions.Count);
