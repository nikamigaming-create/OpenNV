using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void DoorMotionConstructionLifetime(FalloutPluginStack records)
    {
        GD.Print($"OPENNV_NATIVE_DOOR_CONSTRUCTION_ASSEMBLY mvid={typeof(RuntimeNativeDoorMotion).Assembly.ManifestModule.ModuleVersionId}");
        var beforeOrphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        var roots = new List<Node3D>();
        var nodes = new List<Node>();
        var sources = new List<(byte[] Bytes, byte[] Hash)>();
        using var world = new FalloutReferenceWorld(records);
        var cell = FalloutCellSceneReader.Read(records, Key(0x800));
        world.LoadCell(cell);
        var state = world.Get(Key(0xb40));
        var changes = 0;

        (Node3D Root, RuntimeNifControllerPlayer Controller) Build(bool loop = false, bool attached = true, bool auxiliary = false)
        {
            var bytes = AnimatedActivatorNifFixture(loop);
            sources.Add((bytes, SHA256.HashData(bytes)));
            var native = RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(bytes), 1);
            roots.Add(native.Root);
            nodes.AddRange(NodeTraversal.SelfAndDescendants<Node>(native.Root));
            var controller = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(native.Root).Single();
            controller.SetProcess(false);
            if (auxiliary)
            {
                var other = new RuntimeNifControllerPlayer { SourceController = 10, SourceSha256 = new('a', 64) };
                other.Configure([new("Auxiliary", 2, 1, 0, .2f, [])]);
                other.SetProcess(false); native.Root.AddChild(other); nodes.Add(other);
            }
            if (attached) AddChild(native.Root);
            return (native.Root, controller);
        }

        void Refuse(Node parent, IReadOnlyList<RuntimeNifControllerPlayer> candidates, string error)
        {
            var expectedState = JsonSerializer.Serialize(state.Capture());
            var expectedClocks = JsonSerializer.Serialize(candidates.Select(controller => controller.Observation));
            var orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
            var nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            for (var attempt = 0; attempt < 8; ++attempt)
            {
                string? failure = null;
                try
                {
                    var unexpected = RuntimeNativeDoorMotion.Attach(parent, state, candidates, () => changes++);
                    unexpected.Free();
                }
                catch (Exception rejected) when (rejected is InvalidDataException or NotSupportedException or InvalidOperationException)
                { failure = rejected.Message; }
                Require(failure?.Contains(error, StringComparison.Ordinal) == true,
                    "Door construction changed or accepted its original source refusal: " + error);
                Require(JsonSerializer.Serialize(state.Capture()) == expectedState && changes == 0 &&
                    JsonSerializer.Serialize(candidates.Select(controller => controller.Observation)) == expectedClocks &&
                    Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) == orphans &&
                    Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) == nodeCount,
                    "Rejected door construction retained a native Node or changed source state, history or clock.");
            }
        }

        void Saved(FalloutObjectAnimationSnapshot clock, bool moving = true)
        {
            state.DoorOpen = false;
            state.DoorMotion = new(clock.Controller, clock.Sha256, false, moving);
            state.ObjectAnimations = [clock];
        }

        try
        {
            var first = Build();
            var second = Build();
            Refuse(first.Root, [], "Door has no unique source Open/Close controller.");
            Refuse(first.Root, [first.Controller, second.Controller], "Door has no unique source Open/Close controller.");
            var looping = Build(loop: true);
            Refuse(looping.Root, [looping.Controller], "Door motion requires a finite managed source animation sequence.");

            var singleRoot = new Node3D(); roots.Add(singleRoot); nodes.Add(singleRoot); AddChild(singleRoot);
            var single = new RuntimeNifControllerPlayer
            { SourceController = first.Controller.SourceController, SourceSha256 = first.Controller.SourceSha256 };
            single.Configure([new("Open", 2, 1, 0, .2f, [])]);
            single.SetProcess(false); singleRoot.AddChild(single); nodes.Add(single);
            Refuse(singleRoot, [single], "Door has no unique source Open/Close controller.");

            var clock = new FalloutObjectAnimationSnapshot(first.Controller.SourceController,
                first.Controller.SourceSha256, "Close", .05, false);
            Saved(clock with { Controller = clock.Controller + 1 });
            Refuse(first.Root, [first.Controller], "Saved door motion differs from its winning model controller.");
            Saved(clock with { Sha256 = new('c', 64) });
            Refuse(first.Root, [first.Controller], "Saved door motion differs from its winning model controller.");
            Saved(clock); state.ObjectAnimations = [];
            Refuse(first.Root, [first.Controller], "Saved door motion has no retained source animation clock.");
            Saved(clock with { ElapsedSeconds = -1 });
            Refuse(first.Root, [first.Controller], "Saved object animation state is invalid.");
            Saved(clock, moving: false);
            Refuse(first.Root, [first.Controller], "Saved settled door has an unfinished source animation.");
            Saved(clock with { ElapsedSeconds = first.Controller.SequenceRange("Close").StopTime, StartPending = true }, moving: false);
            Refuse(first.Root, [first.Controller], "Saved settled door has an unfinished source animation.");

            // A detached model receives no source initialization before Ready.
            var auxiliaryClock = new FalloutObjectAnimationSnapshot(10, new('a', 64), "Auxiliary", .04, false);
            state.DoorMotion = null; state.ObjectAnimations = [auxiliaryClock];
            var detached = Build(attached: false, auxiliary: true);
            var warm = RuntimeNativeDoorMotion.Attach(detached.Root, state,
                NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(detached.Root).ToArray(), () => changes++);
            nodes.Add(warm);
            Require(state.DoorMotion is null && detached.Controller.CaptureScriptState() is null &&
                detached.Controller.TextKeyCount == 0 && !warm.IsNodeReady(),
                "Detached door attachment initialized its source state before Ready.");
            AddChild(detached.Root); warm.SetProcess(false); detached.Controller.SetProcess(false);
            Require(state.DoorMotion?.OpenState == 3 && !detached.Controller.Playing && warm.IsNodeReady() &&
                detached.Controller.TextKeyCount == 0 && changes == 0,
                "Ready door initialization changed its retained endpoint or replayed source keys.");

            // Prefetch has a live model but no presentation registration. A
            // cancelled prediction must retain its actual clock, not leave a
            // DoorMotion whose only controller was freed with the staged node.
            warm.SetOpen(true);
            detached.Controller._Process(.05);
            var staged = state.Capture();
            FalloutReferenceSnapshot.Validate([staged]);
            Require(staged.DoorMotion is { Open: true, Moving: true } && staged.ObjectAnimations is { Count: 2 } &&
                staged.ObjectAnimations.Single(value => value.Controller == 10) == auxiliaryClock,
                "Unregistered staged door did not expose its real moving source clock.");
            detached.Root.Free();
            Require(state.CaptureObjectAnimations is null &&
                JsonSerializer.Serialize(state.Capture().ObjectAnimations) == JsonSerializer.Serialize(staged.ObjectAnimations),
                "Cancelling a staged door lost or advanced its retained clock.");
            FalloutReferenceSnapshot.Validate([state.Capture()]);

            var resumed = Build(auxiliary: true);
            var resumedMotion = RuntimeNativeDoorMotion.Attach(resumed.Root, state,
                NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(resumed.Root).ToArray(), () => changes++);
            nodes.Add(resumedMotion); resumedMotion.SetProcess(false); resumed.Controller.SetProcess(false);
            var stagedDoorClock = staged.ObjectAnimations!.Single(value => value.Controller == resumed.Controller.SourceController);
            Require(resumed.Controller.CaptureObjectState() == stagedDoorClock &&
                state.Capture().ObjectAnimations!.Single(value => value.Controller == 10) == auxiliaryClock &&
                resumed.Controller.TextKeyCount == 0 && changes == 0,
                "Cold staged-door restoration changed its fractional clock or replayed its prefix.");
            var newerClock = stagedDoorClock with { ElapsedSeconds = .075 };
            IReadOnlyList<FalloutObjectAnimationSnapshot> NewerCapture() => [newerClock, auxiliaryClock];
            state.BindObjectAnimationCapture(NewerCapture);
            resumed.Root.Free();
            Require(state.CaptureObjectAnimations == NewerCapture &&
                state.Capture().ObjectAnimations!.Single(value => value.Controller == newerClock.Controller) == newerClock,
                "Retiring an older door displaced its newer animation provider.");
            state.UnbindObjectAnimationCapture(NewerCapture);
            Require(state.ObjectAnimations!.Single(value => value.Controller == newerClock.Controller) == newerClock && state.CaptureObjectAnimations is null,
                "The last real door clock was not retained when its newer provider retired.");

            // Restore through the actual native source controller at its Ready
            // boundary; neither preflight nor restoration replays the prefix.
            Saved(clock);
            var expectedSaved = JsonSerializer.Serialize(state.Capture());
            var restored = RuntimeNativeDoorMotion.Attach(first.Root, state, [first.Controller], () => changes++);
            nodes.Add(restored); restored.SetProcess(false); first.Controller.SetProcess(false);
            Require(JsonSerializer.Serialize(state.Capture()) == expectedSaved &&
                first.Controller.CaptureScriptState() == clock && first.Controller.TextKeyCount == 0 && changes == 0,
                "Native door restoration changed its original fractional clock or emitted source completion.");
            foreach (var (bytes, hash) in sources)
                Require(hash.SequenceEqual(SHA256.HashData(bytes)), "Door construction changed its first-party NIF input.");
        }
        finally
        {
            foreach (var root in roots) if (GodotObject.IsInstanceValid(root)) root.Free();
        }
        Require(nodes.All(node => !GodotObject.IsInstanceValid(node)) &&
            Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) == beforeOrphans,
            "Door construction fixture retained an original native owner after cleanup.");
        GD.Print("OPENNV_NATIVE_DOOR_CONSTRUCTION_LIFETIME_PASS missingController=true ambiguousController=true " +
            "missingClose=true loopingRefused=true invalidClock=true sourceDrift=true settledUnfinishedRefused=true " +
            "noPrefixReplay=true detachedReady=true nativeColdClock=true cancelledPrefetchClock=true newerProviderPreserved=true repeatedRefusals=80 sourceBytes=unchanged " +
            "orphanNodes=unchanged recording=false campaign=unverified parity=unverified");
    }
}
