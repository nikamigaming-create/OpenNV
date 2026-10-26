using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics.Parity;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private sealed record CreaturePoseSample(string Step, string Owner, Transform3D Body, Transform3D[] Bones,
        double DefaultSeconds, double? PackageSeconds, double? EventSeconds, double? ScriptSeconds,
        bool PackageOwnsPose, bool Moving, bool OnFloor, string? Package, string? Animation);
    private sealed record CreaturePoseDifference(int Bone, string Name, float MaximumAbsoluteMatrixComponentDelta,
        float[] Before, float[] After, float[] BodyBefore, float[] BodyAfter);
    private sealed class CreaturePoseWindow(string name)
    {
        internal string Name { get; } = name;
        internal int PhysicsSamples, RenderSamples, OwnerChanges, StationaryPackageSamples, EventPackageSamples, ScriptSamples;
        internal double PhysicsSeconds, RenderSeconds;
        internal CreaturePoseSample? First, Last;
        internal CreaturePoseDifference? LargestChange;
    }

    private async Task OwnedCreaturePoseHandoff(string dataRoot, string actorId)
    {
        if (DisplayServer.GetName() == "headless")
            throw new ArgumentException("Creature pose publication requires a display; headless package motion skips skeletal publication.");
        var identity = actorId.Split(':');
        if (identity.Length != 2) throw new ArgumentException("Creature pose audit requires plugin:hex-object-id for a winning ACRE.");
        var caller = new FalloutFormKey(identity[0], Convert.ToUInt32(identity[1], 16));
        var fixture = new Node3D(); AddChild(fixture);
        var previousFps = Engine.MaxFps; var previousPhysics = Engine.PhysicsTicksPerSecond;
        var windowId = GetWindow().GetWindowId(); var previousVsync = DisplayServer.WindowGetVsyncMode(windowId);
        FalloutReferenceWorld? world = null;
        FalloutPluginStack? retainedRecords = null;
        RuntimeCreaturePoseStepObserver? observer = null;
        try
        {
            // These are explicit diagnostic cadence settings, not throughput or
            // display-presentation acceptance. Actual sample counts are reported.
            Engine.MaxFps = 144; Engine.PhysicsTicksPerSecond = 60;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled, windowId);
            RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
                activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
            var content = RuntimeLiveContentSource.Current!;
            var records = FalloutPluginStack.Load(content.PluginSources); retainedRecords = records;
            var declaration = records.GetEffective(caller);
            if (declaration.Signature != "ACRE") throw new ArgumentException("Pose subject is not a source ACRE.");
            var sourceHash = SHA256.HashData(declaration.ReadData());
            var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(declaration) ??
                throw new InvalidDataException("Source creature has no CELL."));
            world = new(records); world.LoadCell(cell);
            if (!world.IsEnabled(caller)) throw new NotSupportedException("The source creature is disabled in this initial-state fixture.");
            var globals = FalloutGlobalState.Read(records); var quests = new FalloutQuestState(records);
            var time = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(content.FalloutExecutablePath));
            var state = world.Get(caller); world.InitializeActorTemplates(caller, 1, globals);
            var placed = cell.References.Single(reference => reference.FormKey == caller);
            var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
            Vector3 Source(Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
            Vector3 Native(Vector3 point) => GamebryoCoordinate.ConvertVector(point) * units;
            var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                Native(new(placed.Position[0], placed.Position[1], placed.Position[2])));
            var navigation = CellNavigationGraph.LoadOwned(records, cell.Cell.FormKey);
            var floorPoint = Native(navigation.FindNearestPoint(Source(placement.Origin)));
            var floor = new StaticBody3D { Position = new(placement.Origin.X, floorPoint.Y - .05f, placement.Origin.Z) };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, .1f, 200) } }); fixture.AddChild(floor);
            var context = new NativeActorCombatContext(() => null,
                () => throw new NotSupportedException("Isolated creature pose fixture has no player vitals."),
                (_, _) => throw new NotSupportedException("Isolated creature pose fixture cannot damage the player."),
                (from, to) => navigation.FindPath(Source(from), Source(to)).Select(Native).ToArray(),
                _ => true, () => 1, globals, .4f, 9.81f, PlayerCell: () => cell.Cell.FormKey,
                DialogueBusy: _ => false);
            var actor = RuntimeNativeCreature.Create(records, content, placed, state, units);
            actor.Transform = placement; fixture.AddChild(actor);
            var combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                world, state, records, content, 2, 3, context);
            actor.Combat = combat;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new NotSupportedException("Isolated pose fixture reached a source effect without its campaign owner."), Globals: globals));
            actor.ExecutePackageEvent = scripts.ExecutePackageEvent;
            actor.ConfigureAi(records, quests, world, time, globals);
            var camera = new Camera3D { Position = placement.Origin + new Vector3(3, 1.5f, 3), Current = true };
            fixture.AddChild(camera); camera.LookAt(placement.Origin + Vector3.Up * .5f);
            fixture.AddChild(new DirectionalLight3D { RotationDegrees = new(-45, -30, 0) });
            var skeleton = actor.Skeleton.Node;
            CreaturePoseSample Capture(string step)
            {
                var bones = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();
                if (bones.Append(actor.GlobalTransform).Any(transform => Matrix(transform).Any(value => !float.IsFinite(value))))
                    throw new InvalidDataException("Source creature published a nonfinite body or bone matrix.");
                var eventSeconds = state.PackageIdle?.Clock.SourceSeconds;
                var scriptSeconds = state.CreatureScriptIdle?.Animation.Clock.SourceSeconds;
                var owner = combat.OwnsPose ? "combat" : scriptSeconds is not null ? "script-idle" :
                    eventSeconds is not null ? combat.PackageOwnsPose ? "package+event-idle" : "default+event-idle" :
                    combat.PackageOwnsPose ? "package" : "default";
                return new(step, owner, actor.GlobalTransform, bones, state.Animation.ElapsedSeconds,
                    state.PackageMotion?.Seconds, eventSeconds, scriptSeconds, combat.PackageOwnsPose,
                    combat.PackageMoving, actor.IsOnFloor(), state.QueryCurrentPackage?.Invoke()?.ToString(), state.PackageMotion?.Animation);
            }
            CreaturePoseDifference Difference(CreaturePoseSample before, CreaturePoseSample after)
            {
                var largest = -1f; var selected = 0;
                for (var bone = 0; bone < before.Bones.Length; ++bone)
                {
                    var delta = Matrix(before.Bones[bone]).Zip(Matrix(after.Bones[bone])).Max(pair => MathF.Abs(pair.First - pair.Second));
                    if (delta <= largest) continue;
                    largest = delta; selected = bone;
                }
                return new(selected, skeleton.GetBoneName(selected).ToString(), largest,
                    Matrix(before.Bones[selected]), Matrix(after.Bones[selected]), Matrix(before.Body), Matrix(after.Body));
            }
            var sourceWindow = new CreaturePoseWindow("source-ai-initial-state");
            var packageWindow = new CreaturePoseWindow("controlled-stationary-selected-PACK-request");
            CreaturePoseWindow? active = sourceWindow;
            Exception? samplingError = null;
            void Sample(string step, double delta)
            {
                if (active is null || samplingError is not null) return;
                try
                {
                    var sample = Capture(step);
                    if (step == "physics") { active.PhysicsSamples++; active.PhysicsSeconds += delta; }
                    else { active.RenderSamples++; active.RenderSeconds += delta; }
                    if (sample.PackageOwnsPose && !sample.Moving) active.StationaryPackageSamples++;
                    if (sample.PackageOwnsPose && sample.EventSeconds is not null) active.EventPackageSamples++;
                    if (sample.ScriptSeconds is not null) active.ScriptSamples++;
                    if (active.Last is { } previous)
                    {
                        if (previous.Owner != sample.Owner) active.OwnerChanges++;
                        var difference = Difference(previous, sample);
                        if (active.LargestChange is null || difference.MaximumAbsoluteMatrixComponentDelta > active.LargestChange.MaximumAbsoluteMatrixComponentDelta)
                            active.LargestChange = difference;
                    }
                    active.First ??= sample; active.Last = sample;
                }
                catch (Exception error) { samplingError = error; }
            }
            FalloutPluginRecord? componentPackage = null;
            observer = new RuntimeCreaturePoseStepObserver { ProcessPriority = int.MaxValue, ProcessPhysicsPriority = int.MaxValue };
            observer.Physics = delta =>
            {
                if (samplingError is not null) return;
                try
                {
                    time.AdvanceSimulation((float)delta);
                    if (componentPackage is not null)
                        combat.AdvancePackageMotion(componentPackage, actor.GlobalPosition, 0, false, delta, requireArrivalHeight: true);
                    Sample("physics", delta);
                }
                catch (Exception error) { samplingError = error; }
            };
            observer.Render = delta => Sample("render", delta); fixture.AddChild(observer);
            async Task ObserveWindow()
            {
                var timer = Stopwatch.StartNew();
                while (timer.Elapsed.TotalSeconds < 4 && samplingError is null)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (samplingError is not null) throw new InvalidOperationException("Creature pose sampling failed.", samplingError);
            }
            await ObserveWindow();
            active = null;
            var sourceAi = JsonSerializer.SerializeToElement(actor.AiState);
            var selected = state.QueryCurrentPackage?.Invoke();
            string? componentUnavailable = null;
            CreaturePoseDifference? retainedPackageRender = null, handoff = null;
            CreaturePoseSample? handoffBefore = null, handoffAfter = null;
            if (selected is null || combat.OwnsPose || state.ScriptError is not null || state.CreatureScriptIdle is not null || actor.Error is not null)
                componentUnavailable = "No clean selected source PACK/component slot; source failure or idle ownership is retained.";
            else
            {
                componentPackage = records.GetEffective(selected.Value);
                actor.SetPhysicsProcess(false); active = packageWindow;
                try
                {
                    await ObserveWindow();
                    active = null; componentPackage = null;
                    handoffBefore = Capture("stationary-package-before-zero-time-render");
                    actor._Process(0);
                    retainedPackageRender = Difference(handoffBefore, Capture("stationary-package-after-zero-time-render"));
                    combat.StopPackageMotion(); actor._Process(0);
                    handoffAfter = Capture("default-after-zero-time-stop-handoff");
                    handoff = Difference(handoffBefore, handoffAfter);
                }
                catch (Exception error) when (error is InvalidOperationException or NotSupportedException or InvalidDataException)
                {
                    componentUnavailable = error.ToString();
                }
                finally { componentPackage = null; actor.SetPhysicsProcess(true); }
            }
            observer.Physics = null; observer.Render = null; active = null;
            object Summary(CreaturePoseWindow window) => new
            {
                window.Name,
                window.PhysicsSamples,
                window.RenderSamples,
                window.PhysicsSeconds,
                window.RenderSeconds,
                window.OwnerChanges,
                window.StationaryPackageSamples,
                window.EventPackageSamples,
                window.ScriptSamples,
                first = window.First is null ? null : ClockSummary(window.First),
                last = window.Last is null ? null : ClockSummary(window.Last),
                window.LargestChange
            };
            if (!sourceHash.SequenceEqual(SHA256.HashData(declaration.ReadData())))
                throw new InvalidDataException("Owned source creature declaration changed during pose observation.");
            GD.Print("OPENNV_NATIVE_CREATURE_POSE_OBSERVATION " + JsonSerializer.Serialize(new
            {
                reference = caller.ToString(),
                cell = cell.Cell.FormKey.ToString(),
                skeleton = actor.Appearance.SkeletonPath,
                targetPhysicsHz = 60,
                targetRenderHz = 144,
                displayRefreshHz = DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen(windowId)),
                recording = false,
                sourceReadonly = true,
                sourceAi,
                source = Summary(sourceWindow),
                component = Summary(packageWindow),
                componentUnavailable,
                retainedPackageRender,
                zeroElapsedHandoff = handoff,
                handoffBefore = handoffBefore is null ? null : ClockSummary(handoffBefore),
                handoffAfter = handoffAfter is null ? null : ClockSummary(handoffAfter),
                actorError = actor.Error,
                sourceError = state.ScriptError,
                eventIdleOverlapObserved = sourceWindow.EventPackageSamples + packageWindow.EventPackageSamples > 0,
                scriptIdleObserved = sourceWindow.ScriptSamples + packageWindow.ScriptSamples > 0,
                fixture = "owned-source-actor-and-capsule/source-initial-quests/owned-NAVM/synthetic-flat-floor/no-target-proxies; controlled stationary request uses selected PACK",
                boundary = "Pose matrices and actual engine cadence only; missing source phases remain uncovered. Ordinary Saloon, authored room collision, retail blending and final-pixel parity are unverified."
            }));
        }
        finally
        {
            if (observer is not null) { observer.Physics = null; observer.Render = null; }
            try { fixture.Free(); }
            finally
            {
                world?.Dispose(); retainedRecords?.Dispose(); RuntimeLiveContentSource.Clear();
                Engine.MaxFps = previousFps; Engine.PhysicsTicksPerSecond = previousPhysics;
                DisplayServer.WindowSetVsyncMode(previousVsync, windowId);
            }
        }
    }

    private static object ClockSummary(CreaturePoseSample sample) => new
    {
        sample.Step,
        sample.Owner,
        sample.DefaultSeconds,
        sample.PackageSeconds,
        sample.EventSeconds,
        sample.ScriptSeconds,
        sample.PackageOwnsPose,
        sample.Moving,
        sample.OnFloor,
        sample.Package,
        sample.Animation,
        body = Matrix(sample.Body)
    };
    private static float[] Matrix(Transform3D value) =>
        [value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z, value.Basis.Y.X, value.Basis.Y.Y, value.Basis.Y.Z,
            value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z, value.Origin.X, value.Origin.Y, value.Origin.Z];
}
