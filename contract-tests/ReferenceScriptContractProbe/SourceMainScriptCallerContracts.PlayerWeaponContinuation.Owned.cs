using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static object AuditOwnedPlayerWeaponConstructor(FalloutPluginStack records,
        FalloutActorProcessCommonState common, FalloutStandalonePlayerSceneState scene,
        FalloutDirectInputState input, FalloutPlayerWeaponContinuationSource source)
    {
        var player = records.RuntimeFormKey(0x14); var constructed = scene.Capture();
        Reject(() => common.ReadPlayerWeaponAction(player, 1));
        common.InitializePlayerWeaponActionConstructor(player, 1, source, constructed.Constructor, () =>
        {
            scene.ControlFields.RequireActualInput(input);
            Require(scene.Capture().Constructor == constructed.Constructor && scene.Capture().Clock is null,
                "Owned weapon-action store left its genuine pre-frame Player constructor.");
        });
        var action = common.ReadPlayerWeaponAction(player, 1);
        var activity = common.ReadPlayerWeaponActivity(player, 1);
        Require(action.Value == 0 && activity.Value == 0 && action.Process.Actor.EnginePlayer &&
            action.Process.Actor.Reference == player && action.Process.GameplayOwnership == activity.Process.GameplayOwnership,
            "Owned Player constructor/getters lost the exact actual Actor lease or independent process fields.");
        var fields = common.Capture().Current.Single(entry => entry.Source.Reference == player).SceneFields?.WeaponContinuation ??
            throw new InvalidDataException("Owned Player construction omitted the complete process cached-value context.");
        Require(fields is { WeaponAction: 0, ActionStores: 1, CacheStores: 0, CacheFlags: 0,
            FirstContextWord: -1, SecondContextWord: -1, FirstContextPointer: null, SecondContextPointer: null, ContextByte: 0 } &&
            fields.Initialization is { } initialization && initialization.PlayerConstructor == constructed.Constructor &&
            initialization.Source == source.Identity && fields.CachedScalarBits.Count == FalloutPlayerWeaponContinuationSource.CachedScalarCount &&
            fields.CachedScalarBits.All(bits => bits == FalloutPlayerWeaponContinuationSource.UnqueriedScalarBits),
            "Owned constructor did not retain every source context store and the actual later Player action-byte producer.");
        var values = new FalloutPlayerActorValues(records);
        Require(values.IsStatusValue("Paralysis") && values.StatusValue("Paralysis") == FalloutPlayerWeaponContinuationSource.ParalysisActorValue &&
            values.ReadBase(FalloutPlayerWeaponContinuationSource.ParalysisActorValue) == 0,
            "Owned status descriptor did not construct the selected current value's independent base pool.");
        // The component audit does not possess the campaign's active effect
        // owner. Its absence must not turn a real current getter into base0.
        Reject(() => common.ReadPlayerFollowingStatus(player, 1,
            () => values.ReadCurrent(FalloutPlayerWeaponContinuationSource.ParalysisActorValue)));
        var after = common.Capture().Current.Single(entry => entry.Source.Reference == player).SceneFields?.WeaponContinuation;
        Require(after == fields || after is not null && after.CacheStores == 0 && after.CacheFlags == 0 &&
            after.CachedScalarBits.SequenceEqual(fields.CachedScalarBits),
            "Unbound owned modifier query stored a fabricated successful cached status.");
        return new { source = source.Identity, originalPlayerConstructorWriter = true, actionByte = action.Value,
            signedActivity = activity.Value, cachedValueContext = fields,
            currentStatusWithoutCampaignModifiers = "REFUSED", wholePlayer = "UNEXECUTED" };
    }
}
