using System.Buffers.Binary;
using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void ExerciseIdleReferenceClosures()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-idle-reference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Contact.esm"), IdleReferenceClosureFixture());
            using var records = FalloutPluginStack.Load(directory, ["Contact.esm"]);
            var source = FalloutCellSceneReader.Read(records, Key(0x800));
            var idle = source.References.Where(reference => reference.Base == Key(1)).ToArray();
            var smallBytes = MeasureIdleBindings(records, source with { References = idle.Take(32).ToArray() });
            var largeBytes = MeasureIdleBindings(records, source with { References = idle });
            // Native frame/relay overhead may be constant, but adding idle
            // references must not allocate a closure for each binding/sample.
            Require(idle.Length == 1024 && largeBytes <= smallBytes + 4096,
                $"Idle reference allocations scale with bindings: 32={smallBytes}, 1024={largeBytes} bytes over 128 frames.");
            ExerciseQueuedActivationCallbacks(records, source with
            {
                References = source.References.Where(reference => reference.Base == Key(2)).ToArray()
            });
            GD.Print($"OPENNV_NATIVE_IDLE_REFERENCE_CLOSURES_PASS smallBindings=32 largeBindings=1024 frames=128 " +
                $"smallAllocatedBytes={smallBytes} largeAllocatedBytes={largeBytes} sourceOrder=true twoActivations=true " +
                "liveCallbackReplacement=true scriptActivationUnobserved=true pauseDeferral=true disabledProcessDeferral=true exactlyOnce=true parity=unverified");
        }
        finally { Directory.Delete(directory, true); }
    }

    private long MeasureIdleBindings(FalloutPluginStack records, FalloutCellScene cell)
    {
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        var root = new Node3D(); AddChild(root);
        try
        {
            var events = new RuntimeNativeReferenceEvents();
            events.Configure(records, world, new(records), cell, root,
                new((_, _) => false, _ => throw new InvalidDataException("Idle reference dispatched an effect.")),
                _ => Transform3D.Identity, 1, 1);
            root.AddChild(events);
            events.SetProcess(true);
            for (var frame = 0; frame < 128; ++frame) events._Process(0);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var frame = 0; frame < 128; ++frame) events._Process(0);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally { root.Free(); }
    }

    private void ExerciseQueuedActivationCallbacks(FalloutPluginStack records, FalloutCellScene cell)
    {
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        var root = new Node3D(); AddChild(root);
        var tree = GetTree();
        var wasPaused = tree.Paused;
        try
        {
            Require(!wasPaused && cell.References.Select(reference => reference.FormKey).SequenceEqual([Key(0x900), Key(0x901)]),
                "Native callback fixture needs an unpaused tree and two authored reference bindings.");
            var nodes = new List<Node3D>();
            foreach (var reference in cell.References)
            {
                var node = new Node3D(); node.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
                root.AddChild(node); nodes.Add(node);
            }
            var reports = new List<string>();
            var trace = new List<(string Stage, FalloutFormKey Reference)>();
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = reports.Add };
            events.Configure(records, world, new(records), cell, root,
                new((_, _) => false, _ => throw new InvalidDataException("Authored activation delegated an unexpected default effect.")),
                _ => Transform3D.Identity, 1, 1);
            root.AddChild(events); events.SetProcess(false);
            void Frame()
            {
                events.SetProcess(true);
                try { events._Process(0); }
                finally { events.SetProcess(false); }
            }
            void QueuePair()
            {
                Require(events.TryActivate(nodes[0]) && events.TryActivate(nodes[1]) && !events.TryActivate(nodes[0]),
                    "Two native inputs did not retain distinct, exactly-once admissions.");
            }
            void RequireTrace(params (string Stage, FalloutFormKey Reference)[] expected) =>
                Require(trace.SequenceEqual(expected), "Native activation observers changed reference identity or callback order.");
            var first = Key(0x900); var second = Key(0x901);
            events.ObservePlayerActivationEnd = (_, _) => throw new InvalidDataException("A replaced observer was snapshotted before activation began.");
            events.ObservePlayerActivationBegin = reference =>
            {
                Require(world.Get(reference).Read(1) == 12, "Source GameMode/OnLoad did not precede its authored OnActivate block.");
                trace.Add(("begin", reference));
                if (reference == first) events.ObservePlayerActivationEnd = (ended, success) =>
                {
                    Require(success, "Successful source activation ended unsuccessfully.");
                    trace.Add(("replaced-end", ended));
                };
            };
            events.ObservePlayerActivationFinished = (reference, success) =>
            {
                Require(success, "Successful source activation finished unsuccessfully."); trace.Add(("finished", reference));
            };
            QueuePair(); Frame();
            RequireTrace(("begin", first), ("replaced-end", first), ("finished", first),
                ("begin", second), ("replaced-end", second), ("finished", second));
            Require(world.Get(first).Read(1) == 123 && world.Get(second).Read(1) == 123 &&
                world.Get(first).Read(2) == 1 && world.Get(second).Read(2) == 1,
                "Event admission replaced authored block order or repeated source activation.");
            trace.Clear();
            events.ScriptActivate(second, first, true); Frame();
            Require(trace.Count == 0 && world.Get(second).Read(2) == 2 && world.Get(first).Read(2) == 1,
                "Script activation acquired player-input observers or lost its actual source admission.");

            foreach (var pause in new[] { true, false })
            {
                trace.Clear();
                var firstBefore = world.Get(first).Read(2); var secondBefore = world.Get(second).Read(2);
                events.ObservePlayerActivationBegin = reference => trace.Add(("begin", reference));
                events.ObservePlayerActivationEnd = (reference, success) =>
                {
                    Require(success, "Deferred activation ended unsuccessfully."); trace.Add(("end", reference));
                    if (reference != first) return;
                    if (pause) tree.Paused = true;
                    else events.SetProcess(false); // Cell replacement disables the retired event owner.
                };
                QueuePair(); Frame();
                RequireTrace(("begin", first), ("end", first), ("finished", first));
                Require(world.Get(first).Read(2) == firstBefore + 1 && world.Get(second).Read(2) == secondBefore,
                    "A modal/retiring owner dispatched a later binding or repeated the first source effect.");
                if (pause) { Frame(); Require(trace.Count == 3, "A paused frame consumed the pending second activation."); }
                tree.Paused = false; Frame();
                RequireTrace(("begin", first), ("end", first), ("finished", first),
                    ("begin", second), ("end", second), ("finished", second));
                Frame();
                Require(trace.Count == 6 && world.Get(first).Read(2) == firstBefore + 1 && world.Get(second).Read(2) == secondBefore + 1,
                    "Resuming native processing lost or repeated a deferred activation.");
            }
            Require(reports.Count == 0 && cell.References.All(reference => world.Get(reference.FormKey).ScriptError is null),
                "The native activation allocation audit concealed a source failure.");
        }
        finally { tree.Paused = wasPaused; root.Free(); }
    }

    private static byte[] IdleReferenceClosureFixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        byte[] Placed(uint id, uint basis) => Record("REFR", id,
            Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]));
        var references = Placed(0x900, 2).Concat(Placed(0x901, 2))
            .Concat(Enumerable.Range(0x1000, 1024).SelectMany(id => Placed((uint)id, 1))).ToArray();
        var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
        BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); references.CopyTo(group, 24);
        const string source = "float order\nshort activations\nbegin GameMode\nif activations == 0\nset order to order * 10 + 1\nendif\nend\n" +
            "begin OnLoad\nset order to order * 10 + 2\nend\n" +
            "begin OnActivate\nset order to order * 10 + 3\nset activations to activations + 1\nend";
        return Record("TES4", 0, Field("HEDR", header)).Concat(Record("STAT", 1))
            .Concat(Record("ACTI", 2, Field("SCRI", BitConverter.GetBytes(0x500u))))
            .Concat(Record("SCPT", 0x500, Local(1, "order"), Local(2, "activations"), Field("SCTX", Encoding.ASCII.GetBytes(source))))
            .Concat(Record("CELL", 0x800, Field("DATA", [1]))).Concat(group).ToArray();
    }
}
