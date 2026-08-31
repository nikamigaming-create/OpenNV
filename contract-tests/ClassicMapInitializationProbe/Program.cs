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
