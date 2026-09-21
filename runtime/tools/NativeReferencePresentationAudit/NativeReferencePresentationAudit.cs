using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferencePresentationAudit : Node3D
{
    public override void _Ready()
    {
        Node3D? root = null;
        RuntimeNativeNifPrototype? prototype = null;
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 1) throw new ArgumentException("Reference presentation audit needs one owned root.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey);
            world.LoadCell(cell);
            var cards = new[] { "TempRorschachTest01REF" }
                .Select(name => cell.References.Single(reference => reference.EditorId == name)).ToArray();
            var path = cell.BaseObjects[cards[0].Base].ModelPath!;
            if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Owned model is absent.", path);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            prototype = new(bytes, units);
            root = new Node3D(); AddChild(root);
            var materializations = 0;
            var projection = new RuntimeNativeReferencePresentation(world, cards, reference =>
            {
                materializations++;
                var transform = new Transform3D(new Basis(Vector3.Up, -reference.RotationRadians[2]),
                    GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
                var node = prototype.InstantiatePlaced(transform); root.AddChild(node); return node;
            });
            root.AddChild(projection);
            var scene = cell with { References = cards };
            var events = new RuntimeNativeReferenceEvents();
            events.Configure(records, world, new(records), scene, root, new((_, _) => false, _ => { }),
                _ => Transform3D.Identity, units, 1);
            root.AddChild(events); events.SetProcess(false);
            var first = projection.Resolve(cards[0].FormKey)!;
            // An unplaced resource clone is the mutation control, not a second
            // gameplay reference with invented source identity or placement.
            var second = prototype.Instantiate(); root.AddChild(second);
            static MeshInstance3D Target(Node3D node) => node.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
                .Single(mesh => mesh.GetMeta("opennv_nif_source_name", "").AsString() == "PipBoyCard01:0");
            if (events.AimedPresentation(Target(first)) != first || events.AimedPresentation(Target(second)) is not null)
                throw new InvalidOperationException("Late materialization did not bind the owned reference exclusively.");
            events.SetResidency(scene, root);
            if (events.AimedPresentation(Target(first)) != first)
                throw new InvalidOperationException("Event residency ignored the presentation index for a source model without root metadata.");
            var otherMaterial = Target(second).GetActiveMaterial(0);
            var originalMaterial = Target(first).GetActiveMaterial(0);
            projection.Apply(new(FalloutReferenceEffectKind.Texture, cards[0].FormKey, cards[0].FormKey,
                NodeName: "PipBoyCard01:0", TexturePath: "clutter/rorchachtest/InkBlot02"));
            if (Target(first).GetActiveMaterial(0) == originalMaterial || Target(second).GetActiveMaterial(0) != otherMaterial)
                throw new InvalidOperationException("Texture replacement mutated the shared source material.");
            var replacement = Target(first).GetActiveMaterial(0);
            var texture = replacement switch
            {
                StandardMaterial3D standard => standard.AlbedoTexture,
                ShaderMaterial shader when shader.ResourceName == NativeNifLightingMaterial.ResourceIdentity => shader.GetShaderParameter("base_map").AsGodotObject() as Texture2D,
                ShaderMaterial shader when shader.ResourceName == NativeNifEffectMaterial.ResourceIdentity => shader.GetShaderParameter("source_texture").AsGodotObject() as Texture2D,
                _ => null,
            };
            if (texture?.GetMeta("opennv_logical_texture").AsString() != "textures/clutter/rorchachtest/InkBlot02.dds")
                throw new InvalidOperationException("The actual material does not bind the requested owned texture.");
            world.SetEnabled(cards[0].FormKey, true);
            projection.Apply(new(FalloutReferenceEffectKind.ReferenceEnable, cards[0].FormKey, cards[0].FormKey, Enable: true));
            projection.Advance(0);
            if (!first.Visible || !second.Visible || materializations != 1) throw new InvalidOperationException("Enabling a reference rebuilt it or changed the control instance.");
            world.SetEnabled(cards[0].FormKey, false, true);
            projection.Advance(.1);
            if (!first.Visible || !world.IsEnabled(cards[0].FormKey) || Target(first).Transparency <= 0 || Target(second).Transparency != 0)
                throw new InvalidOperationException("A fading reference disabled early, lacked opacity or faded the shared control instance.");
            world.SetEnabled(cards[0].FormKey, true, true);
            for (var frame = 0; frame < 60; frame++) projection.Advance(1d / 60);
            if (Target(first).Transparency != 0 || !first.Visible) throw new InvalidOperationException("Cancelled disable failed to recover opacity.");
            world.SetEnabled(cards[0].FormKey, false);
            projection.Apply(new(FalloutReferenceEffectKind.ReferenceEnable, cards[0].FormKey, cards[0].FormKey));
            projection.Advance(0);
            if (first.Visible || first.ProcessMode != ProcessModeEnum.Disabled ||
                first.FindChildren("*", "", true, false).OfType<CollisionObject3D>().Any(collision => collision.CollisionLayer != 0 || collision.CollisionMask != 0))
                throw new InvalidOperationException("Disable left rendering, processing or contact queries active.");
            world.SetEnabled(cards[0].FormKey, true);
            projection.Advance(0);
            projection.SetResidency([], _ => throw new InvalidOperationException("Inactive cache constructed new 3D."), _ => true);
            events.SetResidency(scene with { References = [] }, root);
            if (events.AimedPresentation(Target(first)) is not null || events.TryActivate(Target(first)))
                throw new InvalidOperationException("Warm nonresident geometry retained gameplay activation.");
            if (projection.Nodes.Count != 0 || projection.WarmNodeCount != 1 || first.Visible ||
                first.ProcessMode != ProcessModeEnum.Disabled || !projection.HasPrepared(cards[0].FormKey))
                throw new InvalidOperationException("Warm 3D remained resident or was discarded.");
            projection.SetResidency(cards, _ => throw new InvalidOperationException("Boundary reversal rebuilt warm 3D."));
            events.SetResidency(scene, root);
            if (events.AimedPresentation(Target(first)) != first)
                throw new InvalidOperationException("Warm reentry lost the reference's native interaction binding.");
            if (projection.Resolve(cards[0].FormKey) != first || !first.Visible || projection.WarmNodeCount != 0 || materializations != 1 ||
                Target(first).GetActiveMaterial(0) != replacement)
                throw new InvalidOperationException("Boundary reversal lost the original instance or its material state.");
            projection.SetResidency([], _ => null, _ => false);
            if (projection.WarmNodeCount != 0 || projection.HasPrepared(cards[0].FormKey) || !first.IsQueuedForDeletion())
                throw new InvalidOperationException("Expired warm 3D was not evicted.");
            GD.Print("OPENNV_NATIVE_REFERENCE_PRESENTATION_AUDIT_PASS sharedModel=true instanceTexture=true ownedDds=true enable=true disable=true noRebuild=true fadeOpacity=true reversal=true warmBoundary=true indexedEvents=true lateMaterialization=true inactiveContactsRejected=true boundedEviction=true pixels=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
        finally { root?.Free(); prototype?.Scene.Root.Free(); }
    }
}
