using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void AuditOwnedLoadedPlayerCatalog(string dataRoot)
    {
        using var content = RuntimeLiveContentSource.Open(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var controls = FalloutPlayerMovementControlSource.Reviewed(main);
        var early = FalloutExecutableStringTable.ReadEarlyController(content.FalloutExecutablePath,
            FalloutPackageCommandFieldsSource.Reviewed(controls), FalloutPlayerThreadContextSource.Reviewed(main));
        var mover = FalloutPlayerMoverDeltaSource.Reviewed(early);
        var movement = FalloutExecutableStringTable.ReadPlayerActorMovement(content.FalloutExecutablePath, mover,
            FalloutInitialPlacementCellsSource.Reviewed(FalloutReferencePositionSource.Reviewed(early.Fields)),
            FalloutPlayerWorldOrientationSource.Reviewed(controls));
        var tail = FalloutExecutableStringTable.ReadPlayerReturnTail(content.FalloutExecutablePath, movement,
            FalloutStandalonePlayerSceneSource.Read(main));
        var source = FalloutExecutableStringTable.ReadLoadedPlayerAnimation(content.FalloutExecutablePath, tail);
        const uint PlayerBaseFormId = 0x7;
        var appearance = FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(PlayerBaseFormId));
        var prepared = FalloutNpcPreparedGeometry.Read(appearance, content);
        var model = prepared.SourceModel;
        var scene = model.ConstructLogicalScene();
        void RequireCurrent()
        {
            records.RequireLiveLoadReceipt(records.LoadReceipt);
            if (!ReferenceEquals(model.LogicalScene, scene) || scene.SaveBlocker is not null)
                throw new InvalidOperationException("Owned catalog audit lost its actual returned source model.");
        }
        var resources = new FalloutLoadedAnimationResources(source, records, content, model, RequireCurrent,
            FalloutLoadedAnimationRole.Common);
        var manager = new FalloutLoadedAnimationManagerState(source, resources, scene, RequireCurrent);
        try
        {
            manager.RegisterResources();
            if (!manager.ApplyInitialIdle())
                throw new InvalidDataException("Owned catalog audit did not execute the selected initial idle.");
            var returned = manager.Capture();
            if (returned.LastUpdate is not { Returned: true } || returned.Failure is not null)
                throw new InvalidDataException("Owned catalog audit did not retain the returned C# initial sample.");
            Console.WriteLine($"OPENNV_OWNED_LOADED_ANIMATION_CATALOG_PASS npc={appearance.Npc} skeleton={appearance.SkeletonPath} " +
                $"resources={returned.Sequences.Count} actualWinningFiles=true initialIdleSample=true pureCSharp=true " +
                "scope=default-owned-NPC-assembly activeGameAppearance=UNEXECUTED wholeLoader=UNRETURNED gameplay=UNEXECUTED");
        }
        finally
        {
            NativeNifSceneRetirement.Drain("Owned loaded-animation catalog audit", [manager.Retire, scene.Retire]);
        }
    }
}
