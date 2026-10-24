using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

/// <summary>Geometry fitting checks; these do not certify model likeness or campaign parity.</summary>
public sealed partial class ClassicSceneryFitAudit : Node
{
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            Synthetic();
            GD.Print("OPENNV_CLASSIC_SCENERY_FIT_CONTRACT_PASS indexed=true transformed=true hiddenExcluded=true");
            if (args.Length == 0) { GetTree().Quit(); return; }
            if (args.Length != 1) throw new ArgumentException("Provide the optional owned Fallout 1 installation.");
            using var source = Fallout1OwnedContentSource.LoadInstall(args[0]);
            var art = new ClassicArtCache(path => source.Read(path).Bytes);
            var recipe = JsonSerializer.Deserialize<ClassicAuthoredSceneryRecipe>(
                Godot.FileAccess.GetFileAsString("res://config/classic-authored-scenery-v1.json"))!;
            var policy = JsonSerializer.Deserialize<ClassicBlockoutPolicy>(
                Godot.FileAccess.GetFileAsString("res://config/classic-blockout-v1.json"))!;
            var pixelsPerMeter = policy.PixelsPerMeter;
            using var scenery = new ClassicAuthoredScenery();
            var failures = new List<string>(); var checkedFits = 0; var worst = 0f; var worstCenter = 0f;
            foreach (var binding in recipe.Bindings.Where(row => row.OwnedSurface is null))
                for (var rotation = 0; rotation < 6; rotation++)
                {
                    var path = "art/scenery/" + binding.Art;
                    var frame = art.Frame(path, rotation);
                    var prop = scenery.Prop(path, frame.Texture, frame.Frame, pixelsPerMeter, sourceRotation: rotation)!;
                    try
                    {
                        prop.Rotation = new(0, -rotation * Mathf.Pi / 3, 0);
                        var measured = ExactProjection(prop, pixelsPerMeter);
                        var fitted = Math.Max(measured.Size.X / frame.Frame.Width, measured.Size.Y / frame.Frame.Height);
                        var error = Math.Abs(1 - fitted);
                        worst = Math.Max(worst, error);
                        if (error > 0.0001f) failures.Add($"{binding.Art}/{rotation}: fitted extent {fitted}");
                        prop.Position += ClassicSceneryPlacement.ArtOffset(prop, frame.Frame, pixelsPerMeter);
                        var center = ExactProjection(prop, pixelsPerMeter).GetCenter();
                        var expected = new Vector2(frame.Frame.DirectionX + frame.Frame.FrameX,
                            frame.Frame.DirectionY + frame.Frame.FrameY - frame.Frame.Height / 2f);
                        worstCenter = Math.Max(worstCenter, center.DistanceTo(expected));
                        if (center.DistanceTo(expected) > 0.001f) failures.Add($"{binding.Art}/{rotation}: center error {center.DistanceTo(expected)} pixels");
                        checkedFits++;
                    }
                    finally { prop.Free(); }
                }
            GD.Print(JsonSerializer.Serialize(new { checkedFits, worstExtentError = worst, worstCenterErrorPixels = worstCenter, failures, likeness = "unverified" }));
            GetTree().Quit(failures.Count == 0 ? 0 : 1);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Synthetic()
    {
        var root = new Node3D { Position = new(2, 1, -3), Rotation = new(0, 0.3f, 0) };
        try
        {
            var mesh = new ArrayMesh();
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[] { new(0, 0, 0), new(1, 0, 0), new(0, 1, 1), new(100, 100, 100) };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2 };
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var child = new MeshInstance3D { Mesh = mesh, Position = new(0.4f, 0, -0.2f), Scale = new(2, 1, 3) };
            root.AddChild(child);
            ClassicWorldAudit.ValidateWallShell(0, null);
            try
            {
                ClassicWorldAudit.ValidateWallShell(1, null);
                throw new InvalidOperationException("Visible source walls passed without their shell.");
            }
            catch (InvalidDataException) { }
            try
            {
                ClassicWorldAudit.ValidateWallShell(1, child);
                throw new InvalidOperationException("An open triangle passed the wall-shell contract.");
            }
            catch (InvalidDataException) { }
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * 100 }, Visible = false });
            var exact = ExactProjection(root);
            var measured = ClassicSceneryPlacement.Project(root, 32);
            if (exact.Position.DistanceTo(measured.Position) > 0.0001f || exact.Size.DistanceTo(measured.Size) > 0.0001f)
                throw new InvalidDataException("Projected scenery includes empty box corners, unused vertices or hidden geometry.");
        }
        finally { root.Free(); }
    }

    private static Rect2 ExactProjection(Node3D root, float pixelsPerMeter = 32)
    {
        Rect2? result = null;
        void Visit(Node3D node, Transform3D parent)
        {
            if (!node.Visible) return;
            var placed = parent * node.Transform;
            if (node is MeshInstance3D mesh)
                for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                {
                    var arrays = mesh.Mesh.SurfaceGetArrays(surface);
                    var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    foreach (var index in indices.Length == 0 ? Enumerable.Range(0, vertices.Length) : indices)
                    {
                        var point = placed * vertices[index];
                        var pixel = new Vector2(-16 * MathF.Sqrt(3) * point.X + 16 * point.Z,
                            4 * MathF.Sqrt(3) * point.X - pixelsPerMeter * point.Y + 12 * point.Z);
                        result = result?.Expand(pixel) ?? new Rect2(pixel, Vector2.Zero);
                    }
                }
            foreach (var child in node.GetChildren().OfType<Node3D>()) Visit(child, placed);
        }
        Visit(root, Transform3D.Identity);
        return result ?? throw new InvalidDataException("No projected vertices.");
    }
}
