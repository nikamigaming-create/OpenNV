using OpenNV.Runtime.Campaigns.Classic.Native;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout1.Native;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using System.Text.Json;

namespace OpenNV.Runtime.Tools;

public sealed partial class ClassicWorldAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length < 1) throw new ArgumentException("Provide an owned classic installation, optional MAP/output/campaign/model prefix, and optional candidate recipe/model root.");
            using IFalloutClassicOwnedSource source = args.Length > 3 && args[3] == "fallout-2"
                ? Fo2NativeOwnedSource.LoadInstall(args[0]) : Fallout1OwnedContentSource.LoadInstall(args[0]);
            var maps = args.Length > 1 && args[1] != "all" ? new[] { args[1] } : source.EffectiveLogicalPaths("maps/", ".map");
            var failures = new List<string>(); var elevations = 0;
            if (args.Length == 6) throw new ArgumentException("Candidate recipe and model root must be supplied together.");
            using var authored = args.Length > 6 ? new ClassicAuthoredScenery(
                JsonSerializer.Deserialize<ClassicAuthoredSceneryRecipe>(File.ReadAllText(args[5])) ??
                    throw new InvalidDataException("Candidate recipe is absent."), args[6]) : null;
            foreach (var path in maps)
            {
                try
                {
                    var map = Fallout1NativeMapReader.Read(source.Read(path, out _));
                    foreach (var elevation in map.Elevations.Keys)
                    {
                        var world = ClassicWorldPreview.Build(this, source, path, elevation, sharedAuthored: authored);
                        try
                        {
                            if (world.Unresolved.Count != 0) failures.Add($"{path}/{elevation}: {world.Unresolved.Count} unresolved objects");
                            if (args.Length > 2 && DisplayServer.GetName() != "headless")
                            {
                                if (args.Length > 4)
                                {
                                    var selected = world.GetChildren().OfType<Node3D>().Where(node => node.HasMeta("authored_model") &&
                                        node.GetMeta("authored_model").AsString().StartsWith(args[4], StringComparison.Ordinal)).ToArray();
                                    if (selected.Length == 0) throw new InvalidDataException("Requested authored model group is absent.");
                                    var bounds = ClassicSceneryPlacement.Bounds(selected[0]);
                                    foreach (var node in selected.Skip(1)) bounds = bounds.Merge(ClassicSceneryPlacement.Bounds(node));
                                    world.Camera.StopShot(); world.Camera.Focus = bounds.GetCenter();
                                    world.Camera.Distance = Math.Max(4, bounds.Size.Length() * 1.25f);
                                    world.Camera.Pitch = .65f; world.Camera.SaveView();
                                    Directory.CreateDirectory(args[2]);
                                    File.WriteAllText(Path.Combine(args[2], "authored-placement.json"), JsonSerializer.Serialize(selected.Select(node => new
                                    {
                                        Serial = node.GetMeta("source_serial").AsInt32(),
                                        Model = node.GetMeta("authored_model").AsString(),
                                        Position = new[] { node.Position.X, node.Position.Y, node.Position.Z },
                                        Basis = node.Basis.ToString(),
                                        Bounds = ClassicSceneryPlacement.Bounds(node).ToString()
                                    })));
                                }
                                for (var frame = 0; frame < 4; frame++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                                Directory.CreateDirectory(args[2]);
                                using var image = GetViewport().GetTexture().GetImage();
                                if (image.SavePng(Path.Combine(args[2], Path.GetFileNameWithoutExtension(path.Replace('\\', '/')) + $"-{elevation}.png")) != Error.Ok)
                                    throw new IOException("Could not save the selected private visual diagnostic.");
                            }
                            var walls = world.FindChild("SourceJoinedWallBlockout", true, false) as MeshInstance3D
                                ?? throw new InvalidOperationException("Source wall shell is absent.");
                            if (walls.Mesh.GetSurfaceCount() != 0)
                            {
                                var arrays = walls.Mesh.SurfaceGetArrays(0);
                                var points = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                                var edges = new Dictionary<(int, int), int>();
                                for (var at = 0; at < indices.Length; at += 3)
                                {
                                    var a = points[indices[at]]; var b = points[indices[at + 1]]; var c = points[indices[at + 2]];
                                    if (!a.IsFinite() || !b.IsFinite() || !c.IsFinite() || (b - a).Cross(c - a).LengthSquared() < 1e-12f)
                                        throw new InvalidDataException("Joined wall shell contains an invalid triangle.");
                                    for (var side = 0; side < 3; side++)
                                    {
                                        var first = indices[at + side]; var second = indices[at + (side + 1) % 3];
                                        var edge = first < second ? (first, second) : (second, first);
                                        edges[edge] = edges.GetValueOrDefault(edge) + 1;
                                    }
                                }
                                if (edges.Values.Any(count => count != 2)) throw new InvalidDataException("Joined wall shell is open or non-manifold.");
                            }
                            elevations++;
                        }
                        finally { RemoveChild(world); world.Free(); }
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                }
                catch (Exception error) { failures.Add($"{path}: {error.Message}"); }
            }
            foreach (var error in failures) GD.Print($"OPENNV_CLASSIC_WORLD_AUDIT_UNBOUND {error}");
            GD.Print($"OPENNV_CLASSIC_WORLD_AUDIT maps={maps.Count} elevations={elevations} failures={failures.Count} gameplay=false parity=unverified");
            GetTree().Quit(failures.Count == 0 ? 0 : 1);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
