using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutProcessMovementFlagUpdate ReadAndClearStandalonePlayerMovementFlags()
    {
        _ = ReadStandaloneSceneProcessQueries();
        return ProcessCommon.ReadAndClearPlayerMovementFlags(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch);
    }
    internal int ReadStandaloneActorDistanceLimit() => unchecked((int)FalloutGameSettingIntegers.ReadRetained(records,
        "iAINumberActorsComplexScene", nameof(FalloutInterfaceActorDistanceList)));
    internal float ReadStandaloneActorDistanceTimerSetting() => FalloutGameSettingFloats.ReadRetained(records,
        "fSortActorDistanceListTimer", nameof(FalloutPlayerActorDistanceTimer));
    internal float ReadStandaloneActorDistance(FalloutFormKey actor) => ReadActorDistanceToPlayer(actor, bypassCell: false);
    private float ReadActorDistanceToPlayer(FalloutFormKey actor, bool bypassCell)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var read = _perceptionPlacement ?? throw new NotSupportedException("source-Player-actor-distance-current-placement-unbound");
        var from = read(actor); var to = read(_enginePlayer); from.Validate(); to.Validate();
        if (!bypassCell)
        {
            var first = FalloutCellSceneReader.ReadDefinition(records, from.Cell);
            var second = FalloutCellSceneReader.ReadDefinition(records, to.Cell);
            if (from.Cell != to.Cell && (first.Worldspace is null || first.Worldspace != second.Worldspace)) return float.MaxValue;
        }
        return FalloutActorDistanceMath.Between(from.Position, to.Position);
    }
    internal FalloutProcessPostActionUpdate UpdateStandalonePlayerPostActions(Func<float> readScaledTimer)
    {
        _ = ReadStandaloneSceneProcessQueries();
        return ProcessCommon.UpdatePostActions(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch, readScaledTimer);
    }
    internal FalloutProcessDetectionActionUpdate UpdateStandalonePlayerDetectionAction(Func<float> readScaledTimer)
    {
        _ = ReadStandaloneSceneProcessQueries();
        return ProcessCommon.UpdateDetectionAction(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch, readScaledTimer,
            () => FalloutGameSettingFloats.ReadRetained(records, "fDetectionActionTimer", nameof(FalloutActorProcessCommonState)));
    }
    internal FalloutProcessPlayerActionQuery ReadStandalonePlayerActionCondition()
    {
        _ = ReadStandaloneSceneProcessQueries();
        var process = ActorProcesses.Read(_enginePlayer);
        return ProcessCommon.ReadPlayerActionCondition(_enginePlayer, process.Epoch);
    }
    internal FalloutProcessSceneContextQuery ReadStandalonePlayerSceneContext()
    {
        _ = ReadStandaloneSceneProcessQueries();
        var process = ActorProcesses.Read(_enginePlayer);
        return ProcessCommon.ReadSceneContext(_enginePlayer, process.Epoch);
    }
    internal FalloutProcessPlayerSceneUpdate UpdateStandalonePlayerProcessScene()
    {
        _ = ReadStandaloneSceneProcessQueries();
        var process = ActorProcesses.Read(_enginePlayer);
        return ProcessCommon.UpdatePlayerScene(_enginePlayer, process.Epoch);
    }
    internal FalloutProcessSceneQuery ReadStandaloneSceneProcessQueries()
    {
        var process = ActorProcesses.Read(_enginePlayer);
        var query = ProcessCommon.ReadSceneQueries(_enginePlayer, process.Epoch);
        if (query.Level != process.Level || !query.Actor.EnginePlayer || query.Stack != _processRuntimeStack)
            throw new InvalidDataException("Player scene query changed its real process manager or source actor.");
        // These checks do not select a weapon from an equipment list. An
        // inventory change cannot quietly retain the constructor-null process
        // field until its real replacement-entry/animation writer is owned.
        var inventory = _playerInventory?.Contents ?? throw new NotSupportedException("source-Player-scene-process-inventory-unbound");
        if (inventory.Equipped.Any(id => records.GetEffective(records.RuntimeFormKey(id)).Signature == "WEAP"))
            throw new NotSupportedException("source-Player-scene-process-equipped-entry-replacement-writer-unowned");
        return query;
    }
}
