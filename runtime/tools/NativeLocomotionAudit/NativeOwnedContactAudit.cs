using System.Globalization;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

// A private, source-geometry collision fixture. It never advances a campaign or
// supplies footage; ordinary traversal must separately pass the same contact.
internal static class NativeOwnedContactAudit
{
    internal static async Task Run(Node3D owner, string[] arguments)
    {
        if (arguments.Length is not (5 or 9 or 10 or 12)) throw new ArgumentException("Owned contact needs installation, reference, three metre coordinates, optional quaternion, nearby geometry and capsule height/radius.");
        RuntimeLiveContentSource.Configure(arguments[0], RuntimeLiveContentSource.FalloutNewVegasGame);
        GD.Print($"OPENNV_CONTACT_ENGINE {ProjectSettings.GetSetting("physics/3d/physics_engine")} server={PhysicsServer3D.Singleton.GetClass()}");
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var key = records.RuntimeFormKey(Convert.ToUInt32(arguments[1], 16));
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(key))!.Value);
        var reference = cell.References.Single(value => value.FormKey == key);
        var path = cell.BaseObjects[reference.Base].ModelPath!;
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
            new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
            GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * .0142875f);
        var instance = prototype.InstantiatePlaced(placement);
        var body = new CharacterBody3D
        {
            Position = new(float.Parse(arguments[2], CultureInfo.InvariantCulture),
            float.Parse(arguments[3], CultureInfo.InvariantCulture), float.Parse(arguments[4], CultureInfo.InvariantCulture)),
            CollisionLayer = 2,
            CollisionMask = 1,
            FloorSnapLength = .32f,
            FloorMaxAngle = Mathf.DegToRad(RuntimeConfiguration.Load().Player.MaximumWalkableSlopeDegrees)
        };
        if (arguments.Length >= 9)
        {
            var rotation = arguments.Skip(5).Take(4).Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            body.Basis = new(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]).Normalized());
        }
        var height = arguments.Length == 12 ? float.Parse(arguments[10], CultureInfo.InvariantCulture) : 1.8f;
        var radius = arguments.Length == 12 ? float.Parse(arguments[11], CultureInfo.InvariantCulture) : .32f;
        body.FloorSnapLength = radius;
        body.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up * height / 2,
            Shape = new CapsuleShape3D { Height = height, Radius = radius }
        });
        owner.AddChild(instance); owner.AddChild(body);
        var nearby = new List<Node3D>();
        if (arguments.Length >= 10)
        {
            var selected = arguments[9].StartsWith("references=", StringComparison.Ordinal)
                ? arguments[9][11..].Split(',').Select(hex => records.RuntimeFormKey(Convert.ToUInt32(hex, 16))).ToHashSet() : null;
            foreach (var other in cell.References.Where(value => value.FormKey != key && (value.Flags & 0x800) == 0))
            {
                var point = GamebryoCoordinate.ConvertVector(new(other.Position[0], other.Position[1], other.Position[2])) * .0142875f;
                if ((selected is null ? point.DistanceTo(body.Position) > 8 : !selected.Contains(other.FormKey)) ||
                    cell.BaseObjects[other.Base].ModelPath is not { } model || !model.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)) continue;
                if (!content.TryRead(model, null, out var payload, out _)) throw new FileNotFoundException(model);
                var candidate = new RuntimeNativeNifPrototype(payload, .0142875f);
                var node = candidate.InstantiatePlaced(new(GamebryoCoordinate.ConvertReferenceEuler(
                    new(other.RotationRadians[0], other.RotationRadians[1], other.RotationRadians[2]), other.Scale), point));
                node.Name = "Reference_" + other.FormKey;
                GD.Print($"OPENNV_OWNED_CONTACT_NEARBY reference={other.FormKey} model={model} position={point}");
                owner.AddChild(node); nearby.Add(node); candidate.Scene.Root.Free();
            }
        }
        try
        {
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
            foreach (var direction in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back, Vector3.Up, Vector3.Down })
            {
                using var request = new PhysicsTestMotionParameters3D
                {
                    From = body.GlobalTransform,
                    Motion = direction * .06f,
                    Margin = body.SafeMargin,
                    MaxCollisions = 6
                };
                using var result = new PhysicsTestMotionResult3D();
                var collided = PhysicsServer3D.BodyTestMotion(body.GetRid(), request, result);
                static float[] Point(Vector3 value) => [value.X, value.Y, value.Z];
                GD.Print(JsonSerializer.Serialize(new
                {
                    kind = "owned-contact-sweep",
                    reference = key.ToString(),
                    path,
                    direction = Point(direction),
                    collided,
                    travel = Point(result.GetTravel()),
                    contacts = Enumerable.Range(0, result.GetCollisionCount()).Select(index => new
                    {
                        point = Point(result.GetCollisionPoint(index)),
                        normal = Point(result.GetCollisionNormal(index)),
                        depth = result.GetCollisionDepth(index),
                        collider = (result.GetCollider(index) as Node)?.GetPath().ToString(),
                        priority = (result.GetCollider(index) as CollisionObject3D)?.CollisionPriority
                    })
                }));
            }
            var before = body.GlobalPosition;
            var maximumEscape = 0f;
            foreach (var direction in new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back })
            {
                body.GlobalPosition = before;
                var blocks = new Dictionary<string, int>();
                for (var frame = 0; frame < 60; frame++)
                {
                    await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
                    body.Velocity = direction * 3.6f + Vector3.Down * .16f;
                    if (NativeCharacterStep.TryStep(body, direction * .06f, .4f, out var blocked)) body.Velocity = Vector3.Down * .01f;
                    blocks[blocked ?? "climbed"] = blocks.GetValueOrDefault(blocked ?? "climbed") + 1;
                    body.MoveAndSlide();
                }
                GD.Print($"OPENNV_OWNED_CONTACT_ESCAPE direction={direction} from={before} to={body.GlobalPosition} distance={before.DistanceTo(body.GlobalPosition):R} steps={JsonSerializer.Serialize(blocks)}");
                maximumEscape = Math.Max(maximumEscape, before.DistanceTo(body.GlobalPosition));
            }
            if (maximumEscape < 1) throw new InvalidDataException("The owned contact fixture has no one-metre escape through ordinary body motion.");
            GD.Print("OPENNV_OWNED_CONTACT_PASS ordinaryGameplay=separate");
        }
        finally { foreach (var node in nearby) node.QueueFree(); body.QueueFree(); instance.QueueFree(); prototype.Scene.Root.Free(); }
    }
}
