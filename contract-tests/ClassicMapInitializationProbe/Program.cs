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
            "schema": "opennv-classic-int-initialization-inventory/v1",
            "randomOpcode": "80b4",
            "procedures": [{
              "name": "map_enter_p_proc",
              "bodyOffset": 200,
              "bodyEndOffset": 240
            }],
            "randomSites": [{
              "procedure": "map_enter_p_proc",
              "offset": 212,
              "operandKind": "literal-inclusive-range",
              "minimum": 1,
              "maximum": 5
            }, {
              "procedure": "map_enter_p_proc",
              "offset": 224,
              "operandKind": "source-stack-expression",
              "minimum": null,
              "maximum": null
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
        "maximum": 5
      }, {
        "owner": "live-map-script-slot",
        "sid": "04000001",
        "program": "ACKlint.int",
        "procedure": "map_enter_p_proc",
        "offset": 224,
        "operandKind": "source-stack-expression",
        "minimum": null,
        "maximum": null
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
    var owned = ClassicMapInitializationOwner.Parse(
        ownedDocument.RootElement.GetProperty("map"));
    Console.WriteLine(
        $"{Path.GetFileName(path)}|{owned.Objects.Count}|" +
        $"{owned.ScriptedObjects.Count}|{owned.ScriptSlots.Count}");
}
