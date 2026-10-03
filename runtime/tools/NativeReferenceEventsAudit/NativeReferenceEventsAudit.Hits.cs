using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void HitEvents()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-native-hit-");
        var root = new Node3D(); AddChild(root);
        var paused = GetTree().Paused;
        try
        {
            FalloutFormKey Form(uint id) => new("Contact.esm", id);
            var placed = Record("REFR", 0x90, Field("NAME", BitConverter.GetBytes(7u)), Field("DATA", new byte[24]));
            var group = new byte[24 + placed.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BitConverter.GetBytes((uint)group.Length).CopyTo(group, 4); BitConverter.GetBytes(0x80u).CopyTo(group, 8);
            BitConverter.GetBytes(6u).CopyTo(group, 12); placed.CopyTo(group, 24);
            var bytes = Record("TES4", 0, Field("HEDR", new byte[12])).Concat(
                Record("SCPT", 0x50, Local(1, "hits"), Local(2, "caller"), Local(3, "action"),
                    Field("SCRO", BitConverter.GetBytes(0x70u)), Field("SCTX", System.Text.Encoding.ASCII.GetBytes(
                        "short hits\nshort caller\nshort action\nbegin OnHitWith NativeGun\nset hits to hits + 1\nset caller to GetSelf\nset action to GetActionRef\nend"))))
                .Concat(Record("WEAP", 0x70, Field("EDID", System.Text.Encoding.ASCII.GetBytes("NativeGun\0"))))
                .Concat(Record("ACTI", 7, Field("SCRI", BitConverter.GetBytes(0x50u))))
                .Concat(Record("CELL", 0x80, Field("DATA", [1]))).Concat(group).ToArray();
            File.WriteAllBytes(Path.Combine(directory.FullName, "Contact.esm"), bytes);
            using var records = FalloutPluginStack.Load(directory.FullName, ["Contact.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, Form(0x80)); world.LoadCell(cell);
            var target = new Node3D(); target.SetMeta("opennv_reference_form_key", Form(0x90).ToString()); root.AddChild(target);
            var collider = new StaticBody3D(); target.AddChild(collider);
            var errors = new List<string>();
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = errors.Add };
            events.Configure(records, world, new(records), cell, root,
                new((_, _) => false, _ => throw new InvalidDataException("Unexpected native hit effect.")),
                _ => Transform3D.Identity, 1, 1);
            root.AddChild(events);
            var actual = events.CollisionReference(collider.GetInstanceId());
            Require(actual == Form(0x90), "Native collision descendant lost its source hit reference.");
            world.HitEvents.Mark(actual!.Value, records.RuntimeFormKey(0x14), Form(0x70), FalloutReferenceHitKind.Projectile);
            world.HitEvents.Mark(actual.Value, records.RuntimeFormKey(0x14), Form(0x70), FalloutReferenceHitKind.Projectile);
            GetTree().Paused = true; events._Process(1d / 60);
            Require(world.Get(actual.Value).Read(1) == 0 && world.PendingHitEventCount == 1,
                "Paused native frame consumed or executed a pending projectile hit.");
            GetTree().Paused = false; events._Process(1d / 60);
            var instance = world.Get(actual.Value);
            Require(instance.ScriptError is null && errors.Count == 0 && instance.Read(1) == 1 &&
                instance.Read(2) == 0x90 && instance.Read(3) == 0 && world.PendingHitEventCount == 0,
                "Native adapter lost the authored hit block, coalescence, GetSelf, null action ref or consumption: " + instance.ScriptError);
            events._Process(1d / 60);
            Require(instance.Read(1) == 1, "Native frame repeated a contact without a fresh source mark.");
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            Require(cold.PendingHitEventCount == 0 && cold.Get(actual.Value).Read(1) == 1,
                "Consumed native hit replayed in cold reference state.");
            GD.Print("OPENNV_NATIVE_REFERENCE_HIT_PASS collisionIdentity=true pausedRetained=true resumedSourceBlock=true " +
                "pelletsCoalesced=true sourceCaller=true nullActionRef=true consumedOnce=true cold=true fixture=synthetic nativeBallisticContact=separate parity=unverified");
        }
        finally { GetTree().Paused = paused; root.Free(); directory.Delete(true); }
    }
}
