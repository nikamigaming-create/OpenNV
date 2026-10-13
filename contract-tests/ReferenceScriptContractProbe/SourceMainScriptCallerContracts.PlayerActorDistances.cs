using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerActorDistanceTimer()
    {
        Require(BitConverter.SingleToUInt32Bits(FalloutActorDistanceMath.Between([0, 0, 0], [.1f, .2f, .3f])) == 0x3ebf92a8,
            "Actor distance used double products instead of the source's rounded Float32 lanes.");
        Require(FalloutActorDistanceMath.Between([0, 0, 0], [3, 4, 12]) == 13 &&
            float.IsPositiveInfinity(FalloutActorDistanceMath.Between([0, 0, 0], [float.MaxValue, 0, 0])),
            "Actor distance changed the source square root or sanitized arithmetic overflow.");
        Require(FalloutPlayerActorDistanceTimer.Allocated is { Bits: 0, Stores: 0, SubtractsArgument: false },
            "Managed Player did not expose its deliberate deterministic timer initialization.");
        var positive = FalloutPlayerActorDistanceTimer.Allocated.Reset(.25f);
        var overshot = positive.Subtract(.5f);
        Require(overshot is { Bits: 0xbe800000, Stores: 2, SubtractsArgument: false },
            "Actor-distance timer clamped an overshoot or used a different arithmetic lane.");
        foreach (var bits in new uint[] { 0, 0x80000000, 0xbf800000 })
            Require(!(positive with { Bits = bits }).SubtractsArgument,
                "An ordered nonpositive actor-distance timer entered subtraction.");
        Require((positive with { Bits = 0x7fc00012 }).SubtractsArgument,
            "The actor-distance timer lost its source unordered subtraction arm.");
        var setting = BitConverter.UInt32BitsToSingle(0x80000000);
        var raw = overshot.Reset(setting);
        Require(raw is { Bits: 0x80000000, Stores: 3 } &&
            JsonSerializer.Deserialize<FalloutPlayerActorDistanceTimer>(JsonSerializer.Serialize(raw)) == raw,
            "Actor-distance timer sanitized setting bits or lost its stored cold value.");
        Console.WriteLine("OPENNV_PLAYER_ACTOR_DISTANCE_TIMER_PASS separateSceneArgument=true unorderedArm=true unclamped=true " +
            "rawSettingAndCold=true initialization=managed-zero retailInitialByteParity=UNACCEPTED");
    }
}
