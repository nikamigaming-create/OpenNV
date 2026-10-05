using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void AnimatedActivators(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var sourceCell = FalloutCellSceneReader.Read(records, Key(0x800));
        var cell = sourceCell with { References = sourceCell.References.Where(value => value.FormKey.ObjectId is >= 0xb40 and <= 0xb47).ToArray() };
        world.LoadCell(cell);
        var roots = new List<Node3D>();
        var captures = new List<(FalloutReferenceInstance Instance, Func<IReadOnlyList<FalloutObjectAnimationSnapshot>> Capture)>();
        var nodes = new Dictionary<FalloutFormKey, Node3D>();
        var controllers = new Dictionary<FalloutFormKey, RuntimeNifControllerPlayer>();
        var changes = 0;
        Node3D Bind(FalloutReferenceWorld owner, IEnumerable<uint> ids)
        {
            var root = new Node3D(); AddChild(root); roots.Add(root);
            foreach (var id in ids)
            {
                var key = Key(id);
                var native = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(AnimatedActivatorNifFixture()), 1);
                var node = native.Root; node.SetMeta("opennv_reference_form_key", key.ToString()); root.AddChild(node);
                var controller = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(node).Single();
                controller.SetProcess(false);
                var instance = owner.Get(key);
                if (instance.ObjectAnimations is { Count: > 0 } saved)
                    controller.RestoreScriptState(saved.Single());
                Func<IReadOnlyList<FalloutObjectAnimationSnapshot>> capture = () =>
                    new[] { controller.CaptureScriptState() }.OfType<FalloutObjectAnimationSnapshot>().ToArray();
                instance.BindObjectAnimationCapture(capture); captures.Add((instance, capture));
                if (id is not (0xb43 or 0xb44))
                {
                    var motion = new RuntimeNativeDoorMotion(instance, [controller], () => changes++);
                    node.AddChild(motion); motion.SetProcess(false);
                    if (id == 0xb47)
                    {
                        var duplicate = new RuntimeNativeDoorMotion(instance, [controller], () => changes++);
                        node.AddChild(duplicate); duplicate.SetProcess(false);
                    }
                }
                nodes[key] = node; controllers[key] = controller;
            }
            return root;
        }
        RuntimeNativeReferenceEvents BindEvents(FalloutReferenceWorld owner, Node3D root, List<string> failures)
        {
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = failures.Add };
            events.SetProcess(false);
            events.Configure(records, owner, new(records), cell, root, new((_, _) => false, effect =>
            {
                if (effect.Kind == FalloutReferenceEffectKind.ScriptActivate)
                {
                    events.ScriptActivate(effect.Target ?? effect.Source, effect.Argument!.Value, effect.Enable);
                    return;
                }
                Require(effect.Kind == FalloutReferenceEffectKind.DefaultActivate, "Synthetic ACTI produced an unrelated effect.");
                events.DefaultActivate(effect.Target ?? effect.Source, effect.Argument);
            }), _ => Transform3D.Identity, 1, 1);
            root.AddChild(events);
            return events;
        }
        static void Frame(RuntimeNativeReferenceEvents events)
        { events.SetProcess(true); events._Process(0); events.SetProcess(false); }
        static void Refuse(Action action)
        {
            try { action(); }
            catch (NotSupportedException) { return; }
            throw new InvalidDataException("Unsupported ACTI default silently succeeded.");
        }
        try
        {
            var root = Bind(world, [0xb40, 0xb41, 0xb42, 0xb43, 0xb44, 0xb45, 0xb47]);
            var failures = new List<string>(); var events = BindEvents(world, root, failures);
            var player = records.RuntimeFormKey(0x14);
            Require(events.TryActivate(nodes[Key(0xb40)]), "Unscripted animated ACTI was not admitted."); Frame(events);
            var plain = world.Get(Key(0xb40));
            Require(plain.DoorOpen && plain.DoorMotion?.OpenState == 2, "ACTI default did not use its native motion owner.");
            var clock = JsonSerializer.Serialize(controllers[Key(0xb40)].CaptureScriptState());
            events.ScriptActivate(Key(0xb40), Key(0xb40), false);
            Require(clock == JsonSerializer.Serialize(controllers[Key(0xb40)].CaptureScriptState()), "Self activation restarted unfinished source motion.");
            controllers[Key(0xb40)]._Process(.25);
            nodes[Key(0xb40)].GetChildren().OfType<RuntimeNativeDoorMotion>().Single().Synchronize();
            Require(plain.DoorMotion?.OpenState == 1, "ACTI native opening did not settle.");
            events.ScriptActivate(Key(0xb41), Key(0xb45), true); Frame(events);
            var suppressed = world.Get(Key(0xb41));
            Require(suppressed.Read(1) == records.RuntimeFormId(Key(0xb45)) && suppressed.Read(2) == 1 && !suppressed.DoorOpen,
                "OnActivate lost GetActionRef or failed to suppress ACTI default.");
            events.ScriptActivate(Key(0xb41), Key(0xb45), true); Frame(events);
            Require(suppressed.Read(1) == records.RuntimeFormId(Key(0xb45)) && suppressed.Read(2) == 2 && suppressed.DoorOpen,
                "Authored Activate lost its original caller or failed to delegate.");
            events.ScriptActivate(Key(0xb42), player, true); Frame(events);
            var prefixFailure = world.Get(Key(0xb42));
            Require(prefixFailure.ScriptError is not null && prefixFailure.Read(1) == 1 && !prefixFailure.DoorOpen,
                "Failed source prefix reached the ACTI default.");
            Frame(events);
            Require(prefixFailure.Read(1) == 1 && !prefixFailure.DoorOpen, "GameMode replayed a consumed activation prefix.");
            events.ScriptActivate(Key(0xb43), player, true); Frame(events);
            var missingMotion = world.Get(Key(0xb43));
            Require(missingMotion.ScriptError is not null && missingMotion.Read(1) == 1 && !missingMotion.DoorOpen,
                "Absent native default owner silently completed its source suffix.");
            foreach (var key in new[] { Key(0xb44), Key(0xb46), Key(0xb47) })
                Refuse(() => events.DefaultActivate(key, player));
            Require(!world.Get(Key(0xb44)).DoorOpen && !world.Get(Key(0xb46)).DoorOpen && !world.Get(Key(0xb47)).DoorOpen,
                "Missing presentation/motion or ambiguous motion mutated shared state.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var oldClock = JsonSerializer.Serialize(controllers[Key(0xb41)].CaptureScriptState());
            var coldRoot = Bind(cold, [0xb40, 0xb41, 0xb42, 0xb43, 0xb44, 0xb45, 0xb47]);
            Require(cold.Get(Key(0xb40)).DoorMotion?.OpenState == 1 && cold.Get(Key(0xb41)).DoorMotion?.OpenState == 2 &&
                oldClock == JsonSerializer.Serialize(controllers[Key(0xb41)].CaptureScriptState()), "Cold ACTI state or active source clock changed.");
            var coldEvents = BindEvents(cold, coldRoot, []); Frame(coldEvents);
            Require(cold.Get(Key(0xb42)).ScriptError == prefixFailure.ScriptError && cold.Get(Key(0xb42)).Read(1) == 1 &&
                cold.Get(Key(0xb43)).ScriptError == missingMotion.ScriptError && cold.Get(Key(0xb43)).Read(1) == 1,
                "Cold retained source/default-owner failure replayed its prefix.");
            coldEvents.DefaultActivate(Key(0xb40), Key(0xb40));
            Require(!cold.Get(Key(0xb40)).DoorOpen && cold.Get(Key(0xb40)).DoorMotion?.OpenState == 4,
                "Cold self default did not use ordinary source closing.");
            Require(failures.Count == 2, "Synthetic ACTI failure observation lost or duplicated a source error.");
            GD.Print($"OPENNV_NATIVE_ANIMATED_ACTIVATORS_PASS defaultMotion=true selfActivation=true originalActionRef=true sourceSuppression=true consumedFailure=true missingOwnerRefused=true ambiguousOwnerRefused=true coldClock=true coldFailure=true changes={changes} fixture=first-party recording=false campaign=unverified parity=unverified");
        }
        finally
        {
            foreach (var (instance, capture) in captures) instance.UnbindObjectAnimationCapture(capture);
            foreach (var root in roots) root.Free();
        }
    }

    private static byte[] AnimatedActivatorReferences()
    {
        var result = new List<byte>();
        for (uint id = 0xb40; id <= 0xb47; id++)
        {
            var basis = id switch { 0xb41 => 0x761u, 0xb42 => 0x762u, 0xb43 => 0x763u, _ => 0x760u };
            result.AddRange(Record("REFR", id, Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24])));
        }
        return result.ToArray();
    }
    private static byte[] AnimatedActivatorRecords() => Record("ACTI", 0x760)
        .Concat(Record("ACTI", 0x761, Field("SCRI", BitConverter.GetBytes(0x580u))))
        .Concat(Record("ACTI", 0x762, Field("SCRI", BitConverter.GetBytes(0x581u))))
        .Concat(Record("ACTI", 0x763, Field("SCRI", BitConverter.GetBytes(0x582u))))
        .Concat(Record("SCPT", 0x580, Local(1, "caller"), Local(2, "calls"), Field("SCTX", Encoding.ASCII.GetBytes(
            "ref caller\nshort calls\nbegin OnActivate\nset caller to GetActionRef\nset calls to calls + 1\nif calls == 2\nActivate caller\nendif\nend"))))
        .Concat(Record("SCPT", 0x581, Local(1, "prefix"), Field("SCTX", Encoding.ASCII.GetBytes(
            "short prefix\nbegin OnActivate\nset prefix to prefix + 1\nUnsupportedActivatorAction\nActivate\nend"))))
        .Concat(Record("SCPT", 0x582, Local(1, "prefix"), Field("SCTX", Encoding.ASCII.GetBytes(
            "short prefix\nbegin OnActivate\nset prefix to prefix + 1\nActivate\nset prefix to 99\nend")))).ToArray();

    private static byte[] AnimatedActivatorNifFixture()
    {
        static byte[] Emit(Action<BinaryWriter> emit)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); emit(writer); return stream.ToArray(); }
        void Time(BinaryWriter w, int next, ushort flags = 0x004c)
        { w.Write(next); w.Write(flags); w.Write(1f); w.Write(0f); w.Write(float.MaxValue); w.Write(float.MinValue); w.Write(0); }
        byte[] Sequence(int name, int keys) => Emit(w =>
        { w.Write(name); w.Write(0); w.Write(0); w.Write(1f); w.Write(keys); w.Write(2U); w.Write(1f); w.Write(0f); w.Write(.2f); w.Write(1); w.Write(0); w.Write((ushort)0); });
        byte[] Keys() => Emit(w => { w.Write(-1); w.Write(2); w.Write(0f); w.Write(3); w.Write(.2f); w.Write(4); });
        (string Type, byte[] Data)[] blocks =
        [
            ("NiNode", Emit(w =>
            {
                w.Write(0); w.Write(0); w.Write(1); w.Write((ushort)14); w.Write((ushort)0);
                foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) w.Write(value);
                w.Write(0); w.Write(-1); w.Write(0); w.Write(0);
            })),
            ("NiControllerManager", Emit(w => { Time(w, 2); w.Write(false); w.Write(2); w.Write(3); w.Write(4); w.Write(7); })),
            ("NiMultiTargetTransformController", Emit(w => { Time(w, -1, 0x006c); w.Write((ushort)1); w.Write(0); })),
            ("NiControllerSequence", Sequence(1, 5)), ("NiControllerSequence", Sequence(2, 6)),
            ("NiTextKeyExtraData", Keys()), ("NiTextKeyExtraData", Keys()),
            ("NiDefaultAVObjectPalette", Emit(w => { w.Write(0); w.Write(1); w.Write(9); w.Write("activator"u8); w.Write(0); })),
        ];
        string[] names = ["activator", "Open", "Close", "start", "end"];
        return Emit(w =>
        {
            w.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
            w.Write(blocks.Length); w.Write(34U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); w.Write((ushort)blocks.Length);
            foreach (var block in blocks) { w.Write(block.Type.Length); w.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Length; index++) w.Write((ushort)index);
            foreach (var block in blocks) w.Write(block.Data.Length);
            w.Write(names.Length); w.Write(names.Max(name => name.Length));
            foreach (var name in names) { w.Write(name.Length); w.Write(Encoding.ASCII.GetBytes(name)); }
            w.Write(0); foreach (var block in blocks) w.Write(block.Data); w.Write(1); w.Write(0);
        });
    }
}
