using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class PlayerWorldOrientationContracts
{
    internal static int AuditOwned(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var main = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        var controls = FalloutPlayerMovementControlSource.Reviewed(main);
        var source = FalloutExecutableStringTable.ReadPlayerWorldOrientation(content.FalloutExecutablePath, controls);
        var settings = records.IniSettings; var inputs = FalloutWorldProjectionInputs.Read(source, content.StackId, settings);
        var binding = FalloutPlayerReferenceOrientationBinding.Read(records, records.RuntimeFormKey(0x14), records.RuntimeFormKey(7), source);
        var world = Guid.NewGuid(); var process = Guid.NewGuid(); var invocation = Guid.NewGuid(); var target = Guid.NewGuid();
        var root = new FalloutWorldProjectionOwner(source, content.StackId, world, () => true);
        var publication = new FalloutWorldProjectionPublication(source, content.StackId, world, () => true);
        var fields = new FalloutPlayerControlFieldsState(controls, content.StackId, process);
        FalloutPlayerReferenceOrientationOwner? reference = null;
        FalloutWorldProjectionOwner? coldRoot = null;
        FalloutPlayerReferenceOrientationOwner? coldReference = null;
        FalloutPlayerControlFieldsState? coldFields = null;
        FalloutWorldProjectionPublication? coldPublication = null;
        var targetReads = 0;
        try
        {
            var consumers = new FalloutWorldProjectionConsumers(root, inputs, () =>
            {
                targetReads++;
                return new(source.Identity, content.StackId, world, target, false, null, null, "authored-offline-absent-renderer-capability");
            });
            root.ConstructOrdinary(settings, consumers); publication.PublishReturned(root);
            reference = new(source, content.StackId, process, binding, publication, fields, () => true,
                (mainCall, ordinal) => Require(mainCall == invocation && ordinal == 1, "Owned component received a foreign authored Main lease."));
            reference.InitializePlayerFov(settings);
            var current = RoundTrip(root.Capture()); var retained = RoundTrip(reference.Capture());
            var completeProjection = new OpenNV.Runtime.World.Cells.FalloutReferenceWorldProjectionSnapshot(
                OpenNV.Runtime.World.Cells.FalloutReferenceWorldProjectionSnapshot.CurrentSchema, RoundTrip(publication.Capture()), null, null);
            FalloutWorldProjectionSaveContract.ValidatePlayer(completeProjection, retained, main, content.StackId, process);
            Require(current is { ConstructorReturned: true, Stores: 1, Camera: { Updates: 3, FrustumStores: 1, Attached: true } } &&
                current.CurrentFovBits == BitConverter.SingleToUInt32Bits((float)source.ReadConstructorFov(settings, content.StackId).Number!.Value) &&
                retained is { XBits: 0, YBits: 0, ZBits: 0, Stores: 0, PlayerFovReturned: true } && targetReads == 1 &&
                reference.ReadCurrentWorldFov(invocation, 1).Bits == current.CurrentFovBits &&
                reference.ReadCurrentReferencePitch(invocation, 1).Bits == 0 &&
                FalloutPlayerControlOrientation.Yaw(reference.ReadCurrentReferenceYaw(invocation, 1), fields.ReadYawOffset()) == 0,
                "Selected owned inputs did not construct the actual C# World/camera/reference fields through the reviewed source arm.");
            var freshWorld = Guid.NewGuid(); var freshProcess = Guid.NewGuid();
            coldRoot = new(source, content.StackId, freshWorld, () => true, current);
            coldPublication = new(source, content.StackId, freshWorld, () => true);
            coldPublication.Restore(RoundTrip(publication.Capture()), coldRoot);
            coldFields = new(controls, content.StackId, freshProcess, RoundTrip(fields.Capture()));
            coldReference = new(source, content.StackId, freshProcess, binding, coldPublication, coldFields, () => true,
                (mainCall, ordinal) => Require(mainCall == invocation && ordinal == 1, "Owned cold component received a foreign authored Main lease."), retained);
            Require(coldRoot.Factory != current.Factory && coldReference.Factory != retained.Factory && targetReads == 1 &&
                coldRoot.Capture().Camera.Updates == current.Camera.Updates && coldRoot.Capture().Stores == current.Stores &&
                coldReference.Capture().PlayerFovInvocation == retained.PlayerFovInvocation && coldReference.Capture().Stores == 0 &&
                coldReference.ReadCurrentWorldFov(invocation, 1).Bits == current.CurrentFovBits,
                "Owned cold logical owners reused historical factories or replayed construction, target queries or source setters.");
            var coldProjection = new OpenNV.Runtime.World.Cells.FalloutReferenceWorldProjectionSnapshot(
                OpenNV.Runtime.World.Cells.FalloutReferenceWorldProjectionSnapshot.CurrentSchema, coldPublication.Capture(), null, null);
            FalloutWorldProjectionSaveContract.ValidatePlayer(coldProjection, coldReference.Capture(), main, content.StackId, freshProcess);
            Console.WriteLine("OPENNV_OWNED_PLAYER_WORLD_ORIENTATION_PASS " + JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-player-world-orientation/v1", game = content.Game,
                source = source.Identity, executable = main.EngineSha256, stack = content.StackId,
                rootConstructorReturned = current.ConstructorReturned, cameraUpdates = current.Camera.Updates,
                frustumStores = current.Camera.FrustumStores, currentFovBits = current.CurrentFovBits,
                constructorFovOrigin = current.ConstructorSetting!.Origin,
                controlDefaultWorldFov = settings.Find(FalloutIniCollection.Main, FalloutPlayerControlConfiguration.WorldFovName),
                near = current.Camera.Frustum.Near, far = current.Camera.Frustum.Far,
                actualLogicalCamera = true, actualRawReferenceFields = true, targetReads,
                freshColdWithoutReplay = true, completeSaveWorldReferenceAssociation = true, rendererCapability = "AUTHORED_ABSENT",
                originalLibmAndMachineFlags = "UNVERIFIED", laterSpecializedWorldClip = "UNOWNED",
                changeMaskTwoAndNonzeroControlWriters = "UNOWNED", gameplay = "UNEXECUTED", framesRecorded = false
            }));
            return 0;
        }
        finally
        {
            coldReference?.Retire(); coldPublication?.RetireCurrent(); coldRoot?.Retire();
            reference?.Retire(); publication.RetireCurrent(); root.Retire();
        }
    }
}
