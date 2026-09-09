using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerFurnitureAudit : Node3D
{
    public override async void _Ready()
    {
        RuntimeNativePlayer? player = null;
        Node3D? furnitureNode = null;
        RuntimeNativeNifPrototype? prototype = null;
        var roomPrototypes = new List<RuntimeNativeNifPrototype>();
        Node3D? room = null;
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length is not (3 or 4 or 8) || args.Length >= 4 && args[3] is not ("--collision" or "--room-collision") ||
                args.Length == 8 && args[4] != "--start")
                throw new ArgumentException("Furniture audit needs owned root, CELL editor ID, furniture reference editor ID and optional --collision/--room-collision.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var configuration = RuntimeConfiguration.Load();
            var cell = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", args[1]).FormKey);
            var reference = uint.TryParse(args[2], System.Globalization.NumberStyles.HexNumber, null, out var referenceId)
                ? cell.References.Single(value => records.RuntimeFormId(value.FormKey) == referenceId)
                : cell.References.Single(value => value.EditorId == args[2]);
            var units = configuration.World.GameUnitsToMeters;
            var position = GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units;
            var placement = new Transform3D(new Basis(Vector3.Up, -reference.RotationRadians[2]), position);
            if (args.Length >= 4 && args[3] == "--room-collision")
            {
                using var world = new FalloutReferenceWorld(records);
                world.LoadCell(cell);
                room = new Node3D(); AddChild(room);
                var prototypes = new Dictionary<string, RuntimeNativeNifPrototype>(StringComparer.OrdinalIgnoreCase);
                foreach (var placed in cell.References.Where(value => world.IsEnabled(value.FormKey)))
                {
                    var source = cell.BaseObjects[placed.Base];
                    if (source.Signature != "STAT" || source.ModelPath is null ||
                        FalloutNewVegasBuiltinForms.IsInternalStatic(source.Signature, records.RuntimeFormId(source.FormKey))) continue;
                    if (!prototypes.TryGetValue(source.ModelPath, out var roomPrototype))
                    {
                        if (!content.TryRead(source.ModelPath, null, out var bytes, out _)) throw new FileNotFoundException(source.ModelPath);
                        roomPrototype = new(bytes, units); prototypes.Add(source.ModelPath, roomPrototype); roomPrototypes.Add(roomPrototype);
                    }
                    var transform = new Transform3D(new Basis(Vector3.Up, -placed.RotationRadians[2]),
                        GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * units);
                    var node = roomPrototype.InstantiatePlaced(transform);
                    node.Name = $"Reference_{placed.FormKey}"; room.AddChild(node);
                }
            }
            var creation = FalloutNativeRaceSexResolver.Resolve(records);
            var appearance = FalloutNpcAppearanceResolver.Resolve(records, creation.Player, equippedArmor: [],
                appearanceState: FalloutNativeCharacterCreation.ActorState(records, creation.Player, creation.Initial));
            player = new RuntimeNativePlayer();
            AddChild(player);
            player.Configure(configuration, placement);
            player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
            player.CreateFurnitureBody = () =>
            {
                var body = RuntimeNativeNpc.Create(appearance with { Reference = records.RuntimeFormKey(0x14) }, content, units,
                    (_, _, _, _) => new StandardMaterial3D());
                try { body.ConfigureContactShapes(configuration.Player.CollisionLayer); return body; }
                catch { body.Free(); throw; }
            };
            player.ActivateFurniture(records, new(records), reference, placement, cell.Cell.FormKey);
            if (args.Length >= 4)
            {
                var model = cell.BaseObjects[reference.Base].ModelPath!;
                if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException("Furniture model is absent.", model);
                prototype = new(bytes, units);
                furnitureNode = prototype.InstantiatePlaced(placement); AddChild(furnitureNode);
                var approach = player.FurnitureApproach;
                // A controlled lab start one stride behind the authored approach;
                // the real body must still move past the source collision shape.
                var start = approach.Origin + approach.Basis.Z * units * 32;
                if (args.Length == 8)
                    start = new(float.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture));
                if (!start.IsFinite()) throw new ArgumentException("Furniture start must be finite.");
                player.GlobalTransform = new(approach.Basis, start);
                GD.Print($"OPENNV_NATIVE_PLAYER_FURNITURE_APPROACH start={start} target={approach.Origin}");
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
            var phases = new HashSet<string>();
            bool occupied = false;
            var occupiedFrames = 0;
            for (var frame = 0; frame < 60 * 60; frame++)
            {
                player._PhysicsProcess(1.0 / 60);
                var state = JsonSerializer.SerializeToElement(player.FurnitureState);
                if (state.GetProperty("error").ValueKind != JsonValueKind.Null) throw new InvalidOperationException(state.ToString());
                var phase = state.GetProperty("phase").GetString()!;
                phases.Add(phase);
                if (phase == "occupied" && ++occupiedFrames >= 180 && !occupied)
                {
                    if (player.CurrentFurniture != reference.FormKey || player.GetChildren().OfType<RuntimeNativeNpc>().Count() != 1)
                        throw new InvalidOperationException("Furniture lost its actual player state/body.");
                    occupied = true;
                    if (!player.RequestFurnitureExit()) throw new InvalidOperationException("Occupied furniture did not accept exit.");
                }
                if (phase == "none" && occupied) break;
            }
            if (!phases.IsSupersetOf(["entering", "occupied", "exiting", "none"]) || player.CurrentFurniture is not null)
                throw new InvalidOperationException("Furniture phase or reference lifetime is incomplete.");
            GD.Print($"OPENNV_NATIVE_PLAYER_FURNITURE_AUDIT_PASS reference={reference.FormKey} entry=true body=true loop=true exit=true sourceCollision={args.Length >= 4} recording=off ordinary-input-and-pixels=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally
        {
            player?.Free(); furnitureNode?.Free(); room?.Free(); prototype?.Scene.Root.Free();
            foreach (var item in roomPrototypes) item.Scene.Root.Free();
        }
    }
}
