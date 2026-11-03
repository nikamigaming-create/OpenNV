using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ActorSourceContracts
{
    private static void CompassNonSneakingDetection(string directory)
    {
        var floats = new Dictionary<string, float>
        {
            ["fSneakMaxDistance"] = 1000, ["fSneakExteriorDistanceMult"] = 1,
            ["fSneakDistanceAttenuationExponent"] = 1, ["fImprovedDetectModifier"] = 1,
            ["fSneakSkillMult"] = 1, ["fSneakPerceptionSkillMin"] = 10, ["fSneakPerceptionSkillMax"] = 90,
            ["fSneakAlertMod"] = 0, ["fSneakSleepBonus"] = 0, ["fSneakCombatMod"] = 0,
            ["fSneakAmbushTargetMod"] = 0, ["fSneakAmbushNonTargetMod"] = 0,
            ["fSneakBaseValue"] = -60, ["fPlayerDetectActorValue"] = -30,
            ["fSneakBootWeightBase"] = 0, ["fSneakBootWeightMult"] = 0, ["fSneakSoundsMult"] = 0,
            ["fSneakRunningMult"] = 1, ["fSneakSoundLosMult"] = 1, ["fSneakActionMult"] = 0,
            ["fDetectionViewCone"] = 180,
        };
        var integers = new Dictionary<string, int>
        {
            ["iHUDMaxCompassNPCTicks"] = 10, ["iMapMarkerVisibleDistance"] = 1000,
            ["iSoundLevelSilent"] = 0, ["iSoundLevelNormal"] = 1, ["iSoundLevelLoud"] = 2,
        };
        uint next = 0x100;
        byte[] Setting(string name, byte[] value) => Record("GMST", next++, Field("EDID", Text(name)), Field("DATA", value));
        var acbs = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(8), 1);
        var stats = new byte[17]; BinaryPrimitives.WriteInt16LittleEndian(stats.AsSpan(4), 50);
        File.WriteAllBytes(Path.Combine(directory, "CompassDetection.esm"), Join(Header(),
            Record("NPC_", 7),
            Record("CREA", 1, Field("ACBS", acbs), Field("DATA", stats),
                Field("PNAM", BitConverter.GetBytes(2u)), Field("NAM4", BitConverter.GetBytes(1u))),
            Record("BPTD", 2), Record("BPTD", 0x1d), Record("WRLD", 0x81), Group(1, 0x81, Join(
                Record("CELL", 0x80, Field("DATA", [0]), Field("XCLC", new byte[8])), Group(6, 0x80,
                    Record("ACRE", 0x90, Field("NAME", BitConverter.GetBytes(1u)), Field("DATA", new byte[24]))))),
            Join(floats.Select(pair => Setting(pair.Key, BitConverter.GetBytes(pair.Value))).ToArray()),
            Join(integers.Select(pair => Setting(pair.Key, BitConverter.GetBytes(pair.Value))).ToArray())));
        using var records = FalloutPluginStack.Load(directory, ["CompassDetection.esm"]);
        using var world = new FalloutReferenceWorld(records);
        FalloutFormKey Key(uint id) => new("CompassDetection.esm", id);
        var cell = Key(0x80); var reference = Key(0x90); var player = records.RuntimeFormKey(0x14);
        world.LoadCell(FalloutCellSceneReader.Read(records, cell));
        var actor = world.Get(reference);
        actor.Inventory = new FalloutReferenceInventory();
        actor.ActorValues["sneak"] = new(1000);
        actor.Engagement = new(player);
        var moving = true; var sneaking = false; var perception = 5f;
        actor.QuerySpatialPlacement = () => new(cell, [0, 10, 0], [0, 0, 0]);
        actor.QueryNativeCondition = query => query.Kind switch
        {
            FalloutConditionFunction.IsMoving => moving ? 1 : 0,
            FalloutConditionFunction.IsSneaking => sneaking ? 1 : 0,
            _ => throw new NotSupportedException("Unexpected actor compass query: " + query.Kind),
        };
        world.BindPlayerConditions(query => query.Kind switch
        {
            FalloutConditionFunction.GetActorValue when query.Argument1 == (uint)FalloutActorValueId.Health => 100,
            FalloutConditionFunction.GetActorValue when query.Argument1 == (uint)FalloutActorValueId.Perception => perception,
            FalloutConditionFunction.IsInCombat => 1,
            FalloutConditionFunction.GetSitting => 0,
            _ => throw new NotSupportedException("Unexpected player compass query: " + query.Kind),
        });
        FalloutCompassFrame Read() => world.Compass(new(cell, [0, 0, 0], [0, 0, 0]), 1, 300,
            new FalloutQuestState(records), (_, _) => throw new InvalidOperationException("No source objectives."));
        var walking = Read(); moving = false; var standing = Read();
        Check(walking.Markers.Single(marker => marker.Kind == "actor") is { Reference: var first, Hostile: true } &&
            first == reference && standing.Markers.Single(marker => marker.Kind == "actor").Reference == reference,
            "Ordinary movement deducted non-sneaking target skill or lost authoritative hostile color admission: " +
            string.Join("; ", walking.Unbound));
        sneaking = true;
        var stealth = Read();
        Check(!stealth.Markers.Any(marker => marker.Kind == "actor") && stealth.Unbound.Any(value => value.Contains("Stealth target", StringComparison.Ordinal)),
            "Unowned sneaking detection was silently admitted.");
        sneaking = false; perception = 0; moving = true;
        var unproved = Read();
        Check(!unproved.Markers.Any(marker => marker.Kind == "actor") && unproved.Unbound.Any(value => value.Contains("light/noise/history", StringComparison.Ordinal)),
            "A failed minimum score fabricated visual detection or a hostile marker.");
    }
}
