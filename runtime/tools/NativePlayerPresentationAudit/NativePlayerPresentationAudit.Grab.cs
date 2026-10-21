using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditOwnedGrab(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var configuration = RuntimeConfiguration.Load();
        DesktopInputMap.Configure(configuration.Player.DesktopInput);
        var cell = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey);
        var reference = cell.References.Single(value => cell.BaseObjects[value.Base].ModelPath?.Contains("skull", StringComparison.OrdinalIgnoreCase) == true);
        var path = cell.BaseObjects[reference.Base].ModelPath!;
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        var prototype = new RuntimeNativeNifPrototype(bytes, configuration.World.GameUnitsToMeters);
        var scene = new Node3D(); AddChild(scene);
        try
        {
            var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * configuration.World.GameUnitsToMeters);
            var instance = prototype.InstantiatePlaced(placement); scene.AddChild(instance);
            var body = NodeTraversal.SelfAndDescendants<RuntimeNifRigidBody>(instance).Single();
            var floor = new StaticBody3D { Position = placement.Origin + Vector3.Down * .3f };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, .2f, 10) } }); scene.AddChild(floor);
            var player = new RuntimeNativePlayer(); scene.AddChild(player);
            player.Configure(configuration, new(Basis.Identity, placement.Origin + new Vector3(0, -.2f, 1.5f)));
            player.ConfigureLocomotion(records, () => null);
            await Frames(20);
            Aim();
            var before = body.GlobalPosition;
            var layer = body.CollisionLayer; var mask = body.CollisionMask; var mass = body.Mass;
            Key(true);
            Require(Held(), "Ordinary Grab input did not acquire the source skull's dynamic body.");
            var foreign = new object();
            Require(!body.TryBeginGrab(foreign, body.GlobalPosition, 2, 30), "Another owner replaced an active grab.");
            body.ReleaseGrab(foreign); Require(Held(), "Another owner released the player's grab.");
            player.Position += Vector3.Right * .3f;
            player.Camera.RotateObjectLocal(Vector3.Right, .25f);
            await Frames(60);
            Require(body.GlobalPosition.X > before.X + .15f && !body.Freeze && body.CollisionLayer == layer && body.CollisionMask == mask,
                "Grab did not move the source physics body with collision retained.");
            Require(JsonSerializer.SerializeToElement(player.GrabState).GetProperty("separation").GetSingle() < .05f,
                "Unobstructed grab did not retain its picked surface near the target.");
            Key(false);
            Require(!Held(), "Releasing Grab retained its physics connection.");
            var released = body.GlobalPosition.Y;
            await Frames(45);
            Require(body.GlobalPosition.Y < released - .02f, "Released source body did not return to gravity.");
            Aim(); Key(true); Require(Held(), "Dropped source body could not be grabbed again.");
            var heldStart = body.GlobalPosition;
            var wall = new StaticBody3D { Position = heldStart + Vector3.Right * .8f };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.15f, 4, 10) } }); scene.AddChild(wall);
            for (var frame = 0; frame < 60; frame++)
            {
                player.Camera.LookAt(heldStart + Vector3.Right * (frame * .03f), Vector3.Up);
                await Frames(1);
                Require(body.GlobalPosition.X < wall.GlobalPosition.X, "Held source body crossed a solid obstacle.");
            }
            player.SetModalInput(true); Require(!Held(), "Opening a menu retained a grabbed body.");
            Key(false); Aim(); Key(true); Require(!Held(), "Grab acquired a source body through a modal menu.");
            wall.Free();
            player.SetModalInput(false); Key(false); Aim(); Key(true);
            Require(Held(), "Menu release prevented a new grab.");
            player.Teleport(player.GlobalTransform); Require(!Held(), "Teleport retained a physics connection to the old player frame.");
            Key(false); Aim(); body.Mass = Math.Max(1000, mass); Key(true);
            Require(!Held(), "Grab admitted a body above the owned weight limit.");
            Key(false); body.Mass = mass; body.Freeze = true; Aim(); Key(true);
            Require(!Held(), "Grab admitted a frozen body.");
            Key(false); body.Freeze = false; Aim(); Key(true); Require(Held(), "Source body did not recover after refused grabs.");
            instance.QueueFree(); await Frames(3); Require(!Held(), "Retiring the source body retained the grab owner.");
            GD.Print($"OPENNV_NATIVE_PLAYER_GRAB_PASS reference={reference.FormKey} model={path} sourceMass={mass:R} " +
                "input=true physicalMotion=true collisionRetained=true solidObstacle=true releaseGravity=true modalRelease=true teleportRelease=true weightRefused=true frozenRefused=true retirement=true scene=owned-skull-with-synthetic-floor ordinary-room-placement-and-save=unverified");

            void Aim() => player.Camera.LookAt(body.GlobalPosition, Vector3.Up);
            static void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }
            bool Held() => JsonSerializer.SerializeToElement(player.GrabState).GetProperty("body").ValueKind == JsonValueKind.String;
            void Key(bool pressed)
            {
                using var input = new InputEventKey { PhysicalKeycode = Godot.Key.Z, Pressed = pressed };
                player._UnhandledInput(input);
            }
            async Task Frames(int count)
            {
                for (var frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
        }
        finally { scene.Free(); prototype.Scene.Root.Free(); }
    }
}
