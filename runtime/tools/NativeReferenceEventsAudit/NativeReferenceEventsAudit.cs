using System.Buffers.Binary;
using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit : Node
{
    public override async void _Ready()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-contact-audit-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "Contact.esm");
        Node3D? root = null;
        RuntimeNativePlayer? player = null;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Contact.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            world.LoadCell(cell);
            root = new Node3D();
            AddChild(root);
            var activator = new Node3D();
            activator.SetMeta("opennv_reference_form_key", Key(0x901).ToString());
            var collider = new StaticBody3D();
            activator.AddChild(collider);
            root.AddChild(activator);
            var effects = new List<FalloutReferenceScriptEffect>();
            var reported = new List<string>();
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = reported.Add };
            events.Configure(records, world, new(records), cell, root, new((_, _) => false, effects.Add),
                _ => Transform3D.Identity, 1, 1);
            root.AddChild(events);
            var area = root.GetChildren().OfType<Area3D>().Single();
            Require(((BoxShape3D)area.GetChild<CollisionShape3D>(0).Shape).Size == new Vector3(8, 2, 4),
                "XPRM half extents or source axis conversion differ.");
            player = new RuntimeNativePlayer { CollisionLayer = 1, CollisionMask = 1, Position = new Vector3(3, 0, 0) };
            player.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.1f } });
            AddChild(player);
            player.SetPhysicsProcess(false);
            player.SetProcessUnhandledInput(false);
            async Task Frames()
            {
                for (var frame = 0; frame < 5; ++frame)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            await Frames();
            var triggerState = world.Get(Key(0x900));
            Require(triggerState.ScriptError is null && triggerState.Read(1) == 1 && triggerState.Read(3) > 0,
                "Native physical overlap did not execute the model-less trigger.");
            events.SetResidency(cell, root);
            await Frames();
            Require(root.GetChildren().OfType<Area3D>().Single() == area && triggerState.Read(1) == 1 &&
                triggerState.Read(5) == 1 && world.Get(Key(0x901)).Read(5) == 1,
                "Retained residency rebuilt a trigger, repeated OnLoad or lost active contact membership.");
            player.Position = new Vector3(10, 0, 0);
            await Frames();
            Require(triggerState.Read(2) == 1, "Native physical departure did not execute OnTriggerLeave.");
            player.Position = new Vector3(3, 0, 0);
            await Frames();
            Require(triggerState.Read(1) == 2, "Native trigger suppressed ordinary repeated entry.");
            Require(events.TryActivate(collider), "Native input did not admit source activation.");
            await Frames();
            Require(world.Get(Key(0x901)).Read(4) == 1 && effects.Count == 0,
                "Native activation did not reach the reference-local script or suppressed its default incorrectly.");
            triggerState.ScriptError = "OnTriggerEnter: audit capability was unavailable.";
            var contactsBeforeFault = triggerState.Read(3);
            await Frames();
            Require(triggerState.ScriptError is not null && triggerState.Read(3) == contactsBeforeFault,
                "An unchanged physical overlap retried a failed source event.");
            player.Position = new Vector3(10, 0, 0);
            await Frames();
            player.Position = new Vector3(3, 0, 0);
            await Frames();
            Require(triggerState.ScriptError is null && triggerState.Read(1) == 3,
                "A saved script fault suppressed a fresh native contact entry.");
            world.Get(Key(0x901)).ScriptError = "OnActivate: audit capability was unavailable.";
            Require(events.TryActivate(collider), "A failed activation permanently suppressed ordinary input.");
            await Frames();
            Require(world.Get(Key(0x901)).ScriptError is null && world.Get(Key(0x901)).Read(4) == 2,
                "Fresh native activation did not retry its source block.");
            Require(reported.Count == 1 && reported[0].EndsWith("OnTriggerEnter: audit capability was unavailable.", StringComparison.Ordinal),
                "Native fault reporting lost the expected divergence or reported an unexpected one.");
            events.SetProcess(false);
            var state = System.Text.Json.JsonSerializer.Serialize(world.Capture());
            world.UnloadCell(cell.Cell.FormKey);
            world.LoadCell(cell);
            Require(state == System.Text.Json.JsonSerializer.Serialize(world.Capture()), "Native adapter changed reference state on residency change.");
            await ScriptEvents(records, world, root);
            GD.Print("OPENNV_NATIVE_REFERENCE_EVENTS_AUDIT_PASS physicalContacts=true primitiveHalfExtents=true axisConversion=true modelLess=true leave=true reentry=true retainedContacts=true retainedOnLoad=true activation=true faultReentry=true faultActivation=true localState=true parity=unverified");
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
            return;
        }
        finally
        {
            player?.Free();
            root?.Free();
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
        GetTree().Quit();
    }

    private static FalloutFormKey Key(uint id) => new("Contact.esm", id);
    private async Task ScriptEvents(FalloutPluginStack records, FalloutReferenceWorld world, Node root)
    {
        var callbacks = new FalloutScriptEvents();
        var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
            _ => throw new InvalidDataException("Unexpected callback effect."), Events: callbacks));
        var adapter = new RuntimeNativeScriptEvents(callbacks, () => executor.InvokeFunction) { Active = true };
        root.AddChild(adapter);
        var player = records.RuntimeFormKey(0x14);
        callbacks.SetKey(Key(0x521), player, true, true, 42);
        callbacks.SetKey(Key(0x522), player, true, false, 42);
        callbacks.SetMainLoop(Key(0x520), player, true, modes: 1);
        callbacks.SetMainLoop(Key(0x523), player, true, modes: 2);
        var instance = world.Get(Key(0x901));
        for (var frame = 0; frame < 3; ++frame) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(instance.Read(9) > 0 && instance.Read(10) == 0, "Native GameMode callbacks have no frame owner.");
        GetTree().Paused = true;
        try
        {
            var before = instance.Read(9);
            for (var frame = 0; frame < 3; ++frame) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(instance.Read(9) == before && instance.Read(10) > 0, "Native pause did not switch callback modes.");
            using var down = new InputEventKey { PhysicalKeycode = Godot.Key.Shift, Pressed = true };
            using var repeat = new InputEventKey { PhysicalKeycode = Godot.Key.Shift, Pressed = true, Echo = true };
            using var up = new InputEventKey { PhysicalKeycode = Godot.Key.Shift, Pressed = false };
            Input.ParseInputEvent(down); Input.ParseInputEvent(repeat); Input.ParseInputEvent(up);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(instance.Read(7) == 42 && instance.Read(8) == 42 && !callbacks.IsKeyPressed(42),
                "Native physical key events lost their DirectInput IDs, duplicated echo or failed in MenuMode.");
            Require(NativeScriptKeys.Code(Godot.Key.Shift, KeyLocation.Right) == 54 &&
                NativeScriptKeys.Code(Godot.Key.Ctrl, KeyLocation.Right) == 157,
                "Left and right physical modifiers shared source key IDs.");
            GD.Print("OPENNV_NATIVE_SCRIPT_EVENTS_PASS frameCallbacks=true pausedMenuCallbacks=true physicalKeyEdges=true sourceVariables=true parity=unverified");
        }
        finally { GetTree().Paused = false; adapter.Free(); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static byte[] Fixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var source = "begin OnTriggerEnter player\nset entered to entered + 1\nend\n" +
            "begin OnTriggerLeave player\nset departed to departed + 1\nend\n" +
            "begin OnTrigger player\nset contacts to contacts + 1\nend\n" +
            "begin OnActivate\nset activations to activations + 1\nend\nbegin OnLoad\nset loads to loads + 1\nend";
        var script = Record("SCPT", 0x500, Local(1, "entered"), Local(2, "departed"), Local(3, "contacts"), Local(4, "activations"), Local(5, "loads"),
            Local(7, "keyDown"), Local(8, "keyUp"), Local(9, "frames"), Local(10, "menus"),
            Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Encoding.ASCII.GetBytes(source)));
        var primitive = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(primitive, 4);
        BinaryPrimitives.WriteSingleLittleEndian(primitive.AsSpan(4), 2);
        BinaryPrimitives.WriteSingleLittleEndian(primitive.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(primitive.AsSpan(28), 1);
        byte[] Reference(uint id, params byte[][] fields) => Record("REFR", id,
            Field("EDID", Encoding.ASCII.GetBytes(id == 0x901 ? "AuditRef\0" : "TriggerRef\0")),
            Field("NAME", BitConverter.GetBytes(0x700u)), Field("DATA", new byte[24]), fields.SelectMany(field => field).ToArray());
        var children = Reference(0x900, Field("XPRM", primitive), Field("XTRI", BitConverter.GetBytes(12u)))
            .Concat(Reference(0x901)).ToArray();
        var group = new byte[24 + children.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
        BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); children.CopyTo(group, 24);
        byte[] Function(uint id, string body, bool key = false) => Record("SCPT", id,
            Field("SCHR", new byte[20]), Field("SCRO", BitConverter.GetBytes(0x901u)), key ? Local(1, "key") : [],
            Field("SCTX", Encoding.ASCII.GetBytes((key ? "int key\n" : "") + "begin Function {" + (key ? "key" : "") + "}\n" + body + "\nend")));
        return Record("TES4", 0, Field("HEDR", header)).Concat(script)
            .Concat(Function(0x520, "AuditRef.frames += 1"))
            .Concat(Function(0x521, "AuditRef.keyDown += key", true))
            .Concat(Function(0x522, "AuditRef.keyUp += key", true))
            .Concat(Function(0x523, "AuditRef.menus += 1"))
            .Concat(Record("ACTI", 0x700, Field("SCRI", BitConverter.GetBytes(0x500u))))
            .Concat(Record("CELL", 0x800, Field("DATA", [1]))).Concat(group).ToArray();
    }
    private static byte[] Local(uint index, string name)
    {
        var bytes = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, index);
        return Field("SLSD", bytes).Concat(Field("SCVR", Encoding.ASCII.GetBytes(name + '\0'))).ToArray();
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = fields.SelectMany(bytes => bytes).ToArray();
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
}
