using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void AuditOwnedLateActorTimeSources(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var following = FalloutExecutableStringTable.ReadPlayerFollowingContinuation(content.FalloutExecutablePath,
            FalloutPlayerMovementControlSource.Reviewed(main));
        var source = FalloutExecutableStringTable.ReadPlayerLateActorTime(content.FalloutExecutablePath, following);
        var clock = FalloutExecutableStringTable.ReadPlayerSceneClock(content.FalloutExecutablePath, main);
        Require(source.Following.Controls.Main == clock.Main, "Owned Actor-time and clock selected different actual Main sources.");
        Console.WriteLine("OPENNV_OWNED_LATE_ACTOR_TIME_SOURCE_PASS source=" + source.Identity + " clock=" + clock.Contract +
            " gameplay=UNEXECUTED");
    }

    // Join this to the existing owned HUD/Player/Common audit. It qualifies
    // actual original bytes and constructed fields without inventing an entered
    // Main scope or reporting the ordinary Player virtual as returned.
    private static object AuditOwnedLateActorTimeConstruction(FalloutPluginStack records,
        FalloutActorProcessCommonState common, FalloutPlayerFollowingContinuationSource following)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Owned Actor-time audit lost the real installation.");
        var source = FalloutExecutableStringTable.ReadPlayerLateActorTime(owned.FalloutExecutablePath, following);
        var clock = FalloutExecutableStringTable.ReadPlayerSceneClock(owned.FalloutExecutablePath, following.Controls.Main);
        Require(source == FalloutPlayerLateActorTimeSource.Reviewed(following) &&
            clock.Main == source.Following.Controls.Main,
            "Owned Actor-time and Scene clock lost their same selected original Main/Player/manager association.");
        var snapshot = RoundTripPackage(common.Capture());
        FalloutActorProcessCommonState.Validate(snapshot);
        var player = snapshot.Current.Single(row => row.Source.Reference == records.RuntimeFormKey(0x14));
        Require(player.Source.EnginePlayer && player.Level == FalloutDetectionProcessLevel.High &&
            player.Scalars.PackedDateWord == 0 && player.Scalars.ClockBits == 0xbf800000 &&
            player.Scalars.FirstWord == 0 && player.Scalars.FinalFirstBits is null,
            "Owned Low-to-High process construction lost the independent packed date or substituted another scalar.");
        Reject(() => (source with { Contract = new('c', 64) }).Validate());
        return new
        {
            source = source.Identity, clock = clock.Contract, common = snapshot.CapturedProcess,
            schema = snapshot.Schema, packedDateWord = player.Scalars.PackedDateWord,
            constructorFields = FalloutPlayerLateActorFields.Constructed,
            currentWriterClosure = "ACTUAL-RUNTIME-INGRESS-GUARD-REQUIRED",
            wholePlayer = "UNEXECUTED", saveQuitColdContinue = "UNEXECUTED"
        };
    }
}
