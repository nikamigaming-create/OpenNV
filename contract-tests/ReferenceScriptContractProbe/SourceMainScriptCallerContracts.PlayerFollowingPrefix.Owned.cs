using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    // Add to the existing owned HUD/component audit, after actual common and
    // selected source Player field construction. This does not enter a fake
    // Main caller or claim the Ready Item query/whole frame returned.
    private static object AuditOwnedPlayerFollowingConstruction(FalloutPluginStack records,
        FalloutActorProcessCommonState common, FalloutPlayerInhibitionState inhibition,
        FalloutPlayerFollowingTimerFlagSnapshot timerByte, FalloutPlayerFollowingContinuationSource source)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Owned following audit lost its selected original source.");
        var read = FalloutExecutableStringTable.ReadPlayerFollowingContinuation(owned.FalloutExecutablePath, source.Controls);
        Require(read == source, "Owned following source reader selected a foreign class/caller contract.");
        var player = records.RuntimeFormKey(0x14);
        var current = common.ReadSourcePackageSlot(player, 1, FalloutProcessPackageSlot.Current);
        var primary = common.ReadSourcePackageSlot(player, 1, FalloutProcessPackageSlot.Primary);
        Require(current.Process.Actor.Reference == player && current.Process.Actor.EnginePlayer &&
            current.Process.Level == FalloutDetectionProcessLevel.High && current.Fields.Fields == FalloutProcessPackageWords.Constructed &&
            primary.Fields.Fields == FalloutProcessPackageWords.Constructed && current.Process.Process == primary.Process.Process &&
            current.Fields.Publication.Factory != primary.Fields.Publication.Factory &&
            current.Fields.InitialMove is { DestinationStores: 4, SourceStores: 4 } && primary.Fields.InitialMove is null,
            "Owned current/primary fields did not retain full independent source constructors and the initial Low move.");
        var bytes = inhibition.Capture();
        FalloutPlayerInhibitionState.Validate(bytes, source, bytes.Stack);
        Require(bytes.History.Count == 6 && bytes.Bytes.All(value => value is { Value: 0, Available: true }) &&
            bytes.History[3] is { CallbackArgument: null, CallbackReturned: true } &&
            bytes.History[5] is { CallbackArgument: true, CallbackReturned: true },
            "Owned four-byte constructor guessed its first callback argument or replaced source stores with semantic flags.");
        FalloutPlayerFollowingTimerFlagState.Validate(timerByte, source, bytes.Stack);
        Require(timerByte is { LastOwnedValue: 0, Available: true, Sequence: 0 } && timerByte.Observations.Count == 0,
            "Owned timer-byte constructor admitted an independent scalar or replayed source frame time.");
        return new { source = source.Identity, inhibition = bytes, current = current.Fields, primary = primary.Fields,
            timerByte, sourceScalar = "UNWRITTEN-AND-UNREAD", readyItem = "UNENTERED", wholePlayer = "UNEXECUTED" };
    }
}
