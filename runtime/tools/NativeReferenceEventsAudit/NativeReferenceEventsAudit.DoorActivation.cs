using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void DoorActivation(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(cell);
        var root = new Node3D(); AddChild(root);
        try
        {
            foreach (var id in Enumerable.Range(0x920, 8))
            {
                var node = new Node3D(); node.SetMeta("opennv_reference_form_key", Key((uint)id).ToString()); root.AddChild(node);
            }
            var interactions = new List<(FalloutFormKey Door, FalloutFormKey Actor)>();
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = _ => { } };
            events.SetProcess(false);
            events.Interact = (reference, _, signature, actor) =>
            {
                Require(signature == "DOOR", "NPC activation used a player inventory interaction.");
                interactions.Add((reference.FormKey, actor));
            };
            events.Configure(records, world, new(records), cell, root, new((_, _) => false, effect =>
            {
                Require(effect.Kind == FalloutReferenceEffectKind.DefaultActivate, "Door activation produced another effect.");
                events.DefaultActivate(effect.Target!.Value, effect.Argument);
            }), _ => Transform3D.Identity, 1, 1);
            root.AddChild(events);
            var npc = Key(0x920);
            // Execute the native queue without admitting the fixture's other
            // automatic event clocks. Source dispatch itself remains shared.
            events.ScriptActivate(Key(0x921), npc, false);
            Require(interactions.SequenceEqual([(Key(0x921), npc)]), "Default Activate replaced its NPC action reference with the player.");
            events.ScriptActivate(Key(0x922), npc, true);
            events.SetProcess(true); events._Process(0); events.SetProcess(false);
            Require(world.Get(Key(0x922)).Read(1) == 0x920 && world.Get(Key(0x922)).Read(2) == 1 && interactions.Count == 1,
                "NPC OnActivate lost GetActionRef or failed to suppress the default.");
            events.ScriptActivate(Key(0x922), npc, true);
            events.SetProcess(true); events._Process(0); events.SetProcess(false);
            Require(interactions.Count == 2 && interactions[^1] == (Key(0x922), npc), "Script Activate did not preserve the original NPC through default activation.");
            events.ScriptActivate(Key(0x923), npc, true);
            events.SetProcess(true); events._Process(0); events.SetProcess(false);
            Require(world.Get(Key(0x923)).ScriptError is not null && interactions.Count == 2, "Failed source activation opened its door.");
            foreach (var id in new uint[] { 0x924, 0x926 })
            {
                var denied = false;
                try { events.DefaultActivate(Key(id), npc); }
                catch (NotSupportedException) { denied = true; }
                Require(denied && interactions.Count == 2, "NPC default activation bypassed inaccessible or locked source access.");
                var playerStatus = events.PlayerRouteDoor(Key(id));
                Require(playerStatus.Reference == Key(id) && playerStatus.Error is not null && !playerStatus.Admitted && !playerStatus.Pending,
                    "Player route observation bypassed source door access or queued activation.");
            }
            // XAPD suppresses unrelated activation before either the source
            // script or default interaction. It is not an absent NPC owner.
            var parentOnly = Key(0x925);
            Require(!world.AllowsActivation(parentOnly, npc), "Source parent-only declaration admitted an unrelated NPC.");
            events.DefaultActivate(parentOnly, npc);
            events.ScriptActivate(parentOnly, npc, true);
            events.SetProcess(true); events._Process(0); events.SetProcess(false);
            var parentStatus = events.PlayerRouteDoor(parentOnly);
            Require(interactions.Count == 2 && parentStatus.Reference == parentOnly && parentStatus.Error is not null &&
                !parentStatus.Admitted && !parentStatus.Pending, "Suppressed parent-only activation dispatched a script, interaction or player route.");
            Require(events.PlayerRouteDoor(npc).Reference is null && interactions.Count == 2,
                "Player route observation admitted an actor as a door or performed activation.");
            Require(!interactions.Any(value => value.Door == Key(0x927)), "An unrelated door received activation.");
            GD.Print("OPENNV_NATIVE_NPC_DOOR_ACTIVATION_PASS actorIdentity=true sourceSuppression=true scriptDefault=true sourceFailure=true lockedRefused=true inaccessibleRefused=true parentOnlyRefused=true unrelatedDoorUntouched=true fixture=synthetic parity=unverified");
        }
        finally { root.Free(); }
    }

    private static byte[] DoorActivationReferences()
    {
        byte[] Placed(uint id, uint basis, params byte[][] extra) => Record(id == 0x920 ? "ACHR" : "REFR", id,
            Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]), extra.SelectMany(value => value).ToArray());
        var inaccessible = Placed(0x924, 0x721); inaccessible[9] = 1; // REFR header flag 8, inaccessible.
        return Placed(0x920, 0x720).Concat(Placed(0x921, 0x721)).Concat(Placed(0x922, 0x722))
            .Concat(Placed(0x923, 0x723)).Concat(inaccessible).Concat(Placed(0x925, 0x721, Field("XAPD", [1])))
            .Concat(Placed(0x926, 0x721, Field("XLOC", new byte[12]))).Concat(Placed(0x927, 0x721)).ToArray();
    }
    private static byte[] DoorActivationFixture()
    {
        var acbs = new byte[24]; acbs[8] = 1;
        return Record("NPC_", 0x720, Field("ACBS", acbs)).Concat(Record("DOOR", 0x721))
            .Concat(Record("DOOR", 0x722, Field("SCRI", BitConverter.GetBytes(0x540u))))
            .Concat(Record("DOOR", 0x723, Field("SCRI", BitConverter.GetBytes(0x541u))))
            .Concat(Record("SCPT", 0x540, Local(1, "actor"), Local(2, "activations"), Field("SCTX", Encoding.ASCII.GetBytes(
                "ref actor\nshort activations\nbegin OnActivate\nset actor to GetActionRef\nset activations to activations + 1\nif activations == 2\nActivate\nendif\nend"))))
            .Concat(Record("SCPT", 0x541, Field("SCTX", Encoding.ASCII.GetBytes("begin OnActivate\nUnsupportedDoorCommand\nend")))).ToArray();
    }
}
