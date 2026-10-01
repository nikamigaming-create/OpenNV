using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeRenderedMenuAudit
{
    private void PlayerPackage(string baseRoot, string mod, string root, string packageId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        RuntimeNativePlayer? player = null, restoredPlayer = null;
        try
        {
            using var records = FalloutPluginStack.Load(RuntimeLiveContentSource.Current!.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var package = FalloutDialogueTopic.Find(records, "PACK", packageId);
            var definition = FalloutScriptPackage.Read(package);
            var marker = world.Placement(definition.LocationReference ?? throw new InvalidDataException("Owned fixture requires an explicit reference location."));
            var configuration = RuntimeConfiguration.Load();
            var transform = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(marker.RotationRadians[0], marker.RotationRadians[1], marker.RotationRadians[2]), 1),
                GamebryoCoordinate.ConvertVector(new(marker.Position[0], marker.Position[1], marker.Position[2])) * configuration.World.GameUnitsToMeters);
            RuntimeNativePlayer Create()
            {
                var actor = new RuntimeNativePlayer();
                actor.Configure(configuration, transform);
                AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false); actor.SetProcessUnhandledInput(false);
                return actor;
            }
            player = Create();
            var session = new FalloutScriptSession();
            var owner = new RuntimeNativePlayerPackage(records, player, session, world, () => marker.Cell);
            owner.Apply(package.FormKey); owner.Advance(.375);
            var saved = session.Capture();
            if (saved.PlayerPackage?.Elapsed != .375) throw new InvalidDataException("Owned package did not retain its source clock.");
            var cold = new FalloutScriptSession();
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(saved))!);
            restoredPlayer = Create();
            var restored = new RuntimeNativePlayerPackage(records, restoredPlayer, cold, world, () => marker.Cell);
            Transform3D Camera(RuntimeNativePlayer actor) => actor.GetChildren().OfType<Camera3D>().Single().Transform;
            if (cold.PlayerPackage != saved.PlayerPackage || Camera(player) != Camera(restoredPlayer))
                throw new InvalidDataException("Cold package restarted its event or changed its source camera sample.");
            owner.Advance(1.125); restored.Advance(1.125);
            if (session.PlayerPackage != cold.PlayerPackage || Camera(player) != Camera(restoredPlayer))
                throw new InvalidDataException("Cold package lost its following source clock/camera sample.");
            owner.Advance(9.125); restored.Advance(9.125);
            if (session.PlayerPackage != cold.PlayerPackage || Camera(player) != Camera(restoredPlayer))
                throw new InvalidDataException("Cold package lost the remainder across its event/idle boundary.");
            var beforePending = session.PlayerPackage;
            world.QueuePlayerMoveTo(package.FormKey, definition.LocationReference!.Value, 0, 0, 0);
            owner.Advance(5);
            if (session.PlayerPackage != beforePending) throw new InvalidDataException("Pending source movement advanced the package's camera clock.");
            world.PlayerMoves.Clear();
            var corrupted = new FalloutScriptSession();
            corrupted.Restore(saved with { PlayerPackage = saved.PlayerPackage! with { AnimationSha256 = new string('0', 64) } });
            var rejected = false;
            try { _ = new RuntimeNativePlayerPackage(records, player, corrupted, world, () => marker.Cell); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("Cold package accepted a changed animation source.");
            var forgedComplete = new FalloutScriptSession();
            forgedComplete.Restore(saved with
            {
                PlayerPackage = saved.PlayerPackage! with
                { Idle = null, AnimationSha256 = null, Elapsed = 0, PackageEvent = false, EventKind = null, Complete = true }
            });
            rejected = false;
            try { _ = new RuntimeNativePlayerPackage(records, player, forgedComplete, world, () => marker.Cell); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("Source looping package accepted forged completion.");
            GD.Print($"OPENNV_NATIVE_PLAYER_PACKAGE_PASS package={package.FormKey} target={definition.LocationReference} coldClock=true coldCamera=true pendingMovePaused=true sourceDriftRejected=true sourcePhaseRejected=true recording=false boundary=owned-component-fixture campaign=unverified nonCameraTargets=unbound parity=unverified");
        }
        finally { player?.Free(); restoredPlayer?.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
