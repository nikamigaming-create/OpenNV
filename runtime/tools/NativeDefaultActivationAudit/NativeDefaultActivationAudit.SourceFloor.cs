using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeDefaultActivationAudit
{
    // A bounded component includes actual enabled STAT/SCOL source geometry.
    // A declared missing/unsupported model fails; no replacement floor is made.
    private sealed class SourceCorpseFloor : IDisposable
    {
        private readonly Dictionary<string, RuntimeNativeNifPrototype> _prototypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _resources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<FalloutFormKey, string> _records = [];
        private readonly Dictionary<FalloutFormKey, string> _placements = [];
        internal SourceCorpseFloor(FalloutPluginStack records, FalloutReferenceWorld world, FalloutCellScene cell,
            Node3D fixture, RuntimeLiveContentSource content, float units)
        {
            try
            {
                var instances = 0; var noModel = 0;
                foreach (var reference in cell.References.Where(reference => world.IsEnabled(reference.FormKey) &&
                    cell.BaseObjects[reference.Base].Signature is "STAT" or "SCOL"))
                {
                    var pose = world.Placement(reference.FormKey);
                    Require(pose.Cell == cell.Cell.FormKey, "Source floor reference is not in its retained cell.");
                    _placements.Add(reference.FormKey, JsonSerializer.Serialize(pose));
                    foreach (var form in new[] { reference.FormKey, reference.Base })
                        _records.TryAdd(form, Convert.ToHexString(SHA256.HashData(records.GetEffective(form).ReadData())));
                    if (cell.BaseObjects[reference.Base].ModelPath is not { } model) { noModel++; continue; }
                    if (!_prototypes.TryGetValue(model, out var prototype))
                    {
                        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
                        _resources.Add(model, Convert.ToHexString(SHA256.HashData(bytes)));
                        _prototypes.Add(model, prototype = new(bytes, units));
                    }
                    var node = prototype.InstantiatePlaced(new(GamebryoCoordinate.ConvertReferenceEuler(
                        new(pose.RotationRadians[0], pose.RotationRadians[1], pose.RotationRadians[2]), reference.Scale),
                        GamebryoCoordinate.ConvertVector(new(pose.Position[0], pose.Position[1], pose.Position[2])) * units));
                    node.SetMeta("opennv_audit_source_floor_reference", reference.FormKey.ToString()); fixture.AddChild(node); instances++;
                }
                Require(instances > 0, "Selected source cell has no admitted native static floor component.");
                GD.Print($"OPENNV_CORPSE_SOURCE_FLOOR instances={instances} models={_prototypes.Count} noModelDeclarations={noModel} " +
                    "scope=enabled-STAT-SCOL sourcePose=true completeCell=false syntheticFloor=false");
            }
            catch { Dispose(); throw; }
        }
        internal void VerifyInput(FalloutPluginStack records, FalloutReferenceWorld world, RuntimeLiveContentSource content)
        {
            Require(_records.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(records.GetEffective(pair.Key).ReadData()))) &&
                _placements.All(pair => world.IsEnabled(pair.Key) && pair.Value == JsonSerializer.Serialize(world.Placement(pair.Key))) &&
                _resources.All(pair => content.TryRead(pair.Key, null, out var bytes, out _) &&
                    pair.Value == Convert.ToHexString(SHA256.HashData(bytes))), "Source floor records/resources/placement or enable state changed.");
        }
        public void Dispose() { foreach (var prototype in _prototypes.Values) prototype.Scene.Root.Free(); _prototypes.Clear(); }
    }

    private async Task SettleSourceCorpse(RuntimeNativeNpc actor, RuntimeLiveContentSource content, SourceCorpseFloor floor,
        bool requireRenderer)
    {
        _ = floor;
        var root = actor.GlobalTransform;
        var rig = actor.GetChildren().OfType<RuntimeNativeActorRagdoll>().Single();
        var bodies = rig.GetChildren().OfType<RigidBody3D>().ToArray();
        Require(rig.Active && bodies.Length > 0 && bodies.All(body => !body.Freeze), "Source corpse is not in ordinary active physics.");
        static float BodySpan(RigidBody3D[] values) => values.Max(body => body.GlobalPosition.Y) - values.Min(body => body.GlobalPosition.Y);
        static float SkinSpan(CorpseSkinFrame frame) => frame.Meshes.Max(mesh => mesh.WorldBounds[4]) - frame.Meshes.Min(mesh => mesh.WorldBounds[1]);
        var initialBody = BodySpan(bodies);
        var initialSkin = await ObserveCorpseSkin(actor, content, "natural-initial", requireRenderer);
        var initialHeight = SkinSpan(initialSkin);
        Require(initialBody > 0 && initialHeight > 0, "Source component lacks finite initial physical and weighted-skin height.");
        rig.SetProcess(true);
        var frames = 0;
        while (frames < 720 && !(rig.Settled && BodySpan(bodies) < initialBody * .65f))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); await Deferred(); frames++;
            rig._Process(0);
            Require(actor.GlobalTransform == root && bodies.All(body => body.GlobalTransform.IsFinite()),
                "Natural source corpse changed its reference root or produced a nonfinite body pose.");
        }
        Require(rig.Settled && BodySpan(bodies) < initialBody * .65f,
            "Source native corpse did not naturally settle/collapse within the bounded physics interval.");
        string? support = null;
        foreach (var point in rig.AimPoints)
        {
            using var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * .2f, point - Vector3.Up * .8f, 1);
            ray.CollideWithAreas = false;
            var hit = actor.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count == 0 || hit["normal"].AsVector3().Y < .7f) continue;
            for (var node = hit["collider"].AsGodotObject() as Node; node is not null; node = node.GetParent())
                if (node.HasMeta("opennv_audit_source_floor_reference")) { support = node.GetMeta("opennv_audit_source_floor_reference").AsString(); break; }
            if (support is not null) break;
        }
        Require(support is not null, "Naturally collapsed source corpse has no actual source-static support ray.");
        var finalSkin = await ObserveCorpseSkin(actor, content, "natural-settled", requireRenderer);
        Require(SkinSpan(finalSkin) < initialHeight * .65f, "Physical corpse collapsed but actual source-weighted skin stayed upright.");
        GD.Print($"OPENNV_CORPSE_NATURAL_COLLAPSE_PASS frames={frames} support={support} initialBodyHeight={initialBody:R} " +
            $"finalBodyHeight={BodySpan(bodies):R} initialSkinHeight={initialHeight:R} finalSkinHeight={SkinSpan(finalSkin):R} " +
            "sourceSleeping=true sourceReferenceUnchanged=true actualBodyMotion=true syntheticFloor=false bodyPoseOverride=false campaign=false finalPixels=unverified");
    }
}
