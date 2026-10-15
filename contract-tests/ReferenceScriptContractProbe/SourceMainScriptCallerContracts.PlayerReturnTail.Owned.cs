using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    internal static object AuditOwnedPlayerReturnTailSource(FalloutPluginStack records,
        FalloutPlayerActorMovementSource movement, FalloutStandalonePlayerSceneSource scene)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Player tail qualification requires original owned inputs.");
        records.RequireLiveLoadReceipt(records.LoadReceipt);
        var source = FalloutExecutableStringTable.ReadPlayerReturnTail(owned.FalloutExecutablePath, movement, scene);
        AuditOwnedLoadedPlayerAnimation(owned.FalloutExecutablePath, source);
        AuditOwnedPlayerPostAnimation(owned.FalloutExecutablePath, source);
        using var fixture = new PlayerReturnTailFixture(original: records, controls: movement.Mover.Controller.Fields.Controls);
        var call = fixture.Update(); FalloutPlayerReturnTailState.Validate(fixture.State.Capture());
        Require(call.Returned && call.Animations.Count == 2 && call.Queue is { Returned: true } &&
            call.Extra is { Extra: null, Payload: null }, "Owned constructor/component tail did not execute its actual null/empty branches.");
        return new { source = source.Identity, deathDelay = call.Delay, sameSceneBracket = call.Stores
            .Where(store => store.Field == FalloutPlayerReturnTailField.AnimationBracket).Select(store => new { store.Before, store.After }).ToArray(),
            actualWinningGameSettings = true, ctorComponentScopeOnly = true,
            sourcePointerLoadWriter = "explicit-typed-invalidation-or-actual-first-AnimData-factory-store",
            loadedNonNullAnimDataUpdate = "UNOWNED", wholeThreeDLoader = "UNRETURNED", wholePlayer = "UNRETURNED" };
    }
}
