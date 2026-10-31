using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private RuntimeCreaturePoseObservation? _creaturePoseObservation;

    private bool DispatchCreaturePose(JsonElement command, ulong request)
    {
        if (command.GetProperty("op").GetString() != "actor.pose") return false;
        if (_creaturePoseObservation is { Active: true }) throw new InvalidOperationException("An actor pose observation is already active.");
        if (DisplayServer.GetName() == "headless") throw new NotSupportedException("Native actor pose observation requires a display.");
        var text = command.GetProperty("reference").GetString() ?? throw new ArgumentException("Pose observation has no reference.");
        var parts = text.Split(':');
        if (parts.Length != 2 || !uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var objectId)) throw new ArgumentException("Expected plugin:hex-object-id.");
        var reference = new FalloutFormKey(parts[0], objectId);
        var seconds = command.GetProperty("seconds").GetDouble();
        if (!double.IsFinite(seconds) || seconds is < .1 or > 8) throw new ArgumentException("Pose observation must last 0.1–8 seconds.");
        var descendants = GetTree().Root.FindChildren("*", "", true, false);
        using var descendantsOwner = (Godot.Collections.Array)descendants;
        var matches = descendants.OfType<CharacterBody3D>().Where(actor => actor.IsInsideTree() && (actor switch
        {
            RuntimeNativeCreature creature => creature.Appearance.Reference == reference,
            RuntimeNativeNpc npc => npc.Appearance.Reference == reference,
            _ => false
        })).ToArray();
        if (matches.Length != 1) throw new NotSupportedException($"Pose reference {reference} has {matches.Length} resident NPC/creature owners.");
        var path = Path.Combine(_directory, $"{request:D10}.pose.json");
        _creaturePoseObservation = new(matches[0], seconds, report =>
        {
            try { AtomicWrite(path, JsonSerializer.Serialize(report, Json)); }
            finally { _creaturePoseObservation = null; }
        });
        try { _creaturePoseObservation.Start(GetTree().Root); }
        catch
        {
            _creaturePoseObservation?.Stop("observation-start-failed");
            throw;
        }
        return true;
    }
}

internal sealed class RuntimeCreaturePoseObservation
{
    private const int MaximumSamples = 8192;
    private static readonly JsonSerializerOptions OwnerJson = new(JsonSerializerDefaults.Web);
    private const int MaximumPoseBytes = 32 * 1024 * 1024;
    private sealed record Sample(double Milliseconds, string Step, ulong DrawFrame, ulong PhysicsFrame, bool SceneTreePaused,
        bool ActorProcessing, bool ActorPhysicsProcessing, bool OnFloor, bool VisibleInTree, long Ticks,
        float[] Body, float[] SkeletonWorld, float[] Bones, float[] GlobalBones, JsonElement Owners);
    private readonly CharacterBody3D _actor;
    private readonly Func<RuntimeNativeNifSkeleton> _skeleton;
    private readonly Func<object> _owners;
    private readonly string _actorKind, _skeletonPath, _baseSource;
    private readonly ulong _bodyRid;
    private readonly ulong _actorInstance, _skeletonInstance;
    private readonly int[] _boneParents;
    private readonly float[] _boneRest;
    private readonly float _units;
    private int _poseBytes;
    private readonly double _seconds;
    private readonly Action<object> _publish;
    private readonly string _reference;
    private readonly string[] _bones;
    private readonly List<Sample> _samples = [];
    private readonly int _sampleLimit;
    private readonly RuntimeCreaturePoseStepObserver _observer;
    private long _started;
    private double _captureMilliseconds;
    private bool _observerExiting;
    internal bool Active { get; private set; }

    internal RuntimeCreaturePoseObservation(CharacterBody3D actor, double seconds, Action<object> publish)
    {
        _actor = actor; _seconds = seconds; _publish = publish; _bodyRid = actor.GetRid().Id;
        switch (actor)
        {
            case RuntimeNativeCreature creature:
                _reference = creature.Appearance.Reference!.Value.ToString();
                _baseSource = creature.Appearance.Creature.ToString();
                _actorKind = "CREA"; _skeletonPath = creature.Appearance.SkeletonPath;
                _skeleton = () => creature.Skeleton; _owners = () => creature.PoseOwners;
                break;
            case RuntimeNativeNpc npc:
                _reference = npc.Appearance.Reference!.Value.ToString();
                _baseSource = npc.Appearance.Npc.ToString();
                _actorKind = "NPC_"; _skeletonPath = npc.Appearance.SkeletonPath;
                _skeleton = () => npc.Skeleton; _owners = () => npc.PoseOwners;
                break;
            default: throw new NotSupportedException("Pose observation requires an actual NPC or creature owner.");
        }
        var skeleton = _skeleton().Node;
        _actorInstance = actor.GetInstanceId(); _skeletonInstance = skeleton.GetInstanceId(); _units = _skeleton().UnitsToMetres;
        _bones = Enumerable.Range(0, skeleton.GetBoneCount()).Select(index => skeleton.GetBoneName(index).ToString()).ToArray();
        if (_bones.Length == 0) throw new NotSupportedException("Actual actor skeleton has no bones to observe.");
        _boneParents = Enumerable.Range(0, _bones.Length).Select(skeleton.GetBoneParent).ToArray();
        _boneRest = new float[checked(_bones.Length * 12)];
        for (var index = 0; index < _bones.Length; ++index) WriteMatrix(skeleton.GetBoneRest(index), _boneRest.AsSpan(index * 12, 12));
        if (_boneRest.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Source skeleton rest contains a nonfinite matrix.");
        _poseBytes = checked(_boneRest.Length * sizeof(float));
        _sampleLimit = Math.Min(MaximumSamples, MaximumPoseBytes / checked((_bones.Length * 24 + 24) * sizeof(float) + 4096));
        if (_sampleLimit < 2) throw new NotSupportedException("Source skeleton exceeds the bounded pose observation memory budget.");
        _observer = new()
        {
            Name = "NativeActorPoseObservation",
            ProcessPriority = int.MaxValue,
            ProcessPhysicsPriority = int.MaxValue,
            ProcessMode = Node.ProcessModeEnum.Always,
            Physics = _ => Observe("post-physics"),
            Render = _ => Observe("post-draw"),
            Heartbeat = _ => CheckDeadline()
        };
    }

    internal void Start(Node root)
    {
        _started = Stopwatch.GetTimestamp(); Active = true;
        _actor.TreeExiting += ActorExiting;
        _observer.TreeExiting += ObserverExiting;
        root.AddChild(_observer);
    }

    private void ActorExiting() => Stop("actor-unloaded");
    private void ObserverExiting() { _observerExiting = true; Stop("observer-owner-retired"); }
    private void CheckDeadline()
    {
        if (Active && Stopwatch.GetElapsedTime(_started).TotalSeconds >= _seconds) Stop("completed");
    }
    private void Observe(string step)
    {
        if (!Active) return;
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.IsInsideTree() || _actor.IsQueuedForDeletion()) { Stop("actor-unloaded"); return; }
        if (_samples.Count >= _sampleLimit) { Stop("sample-memory-limit"); return; }
        if (Stopwatch.GetElapsedTime(_started).TotalSeconds >= _seconds) { Stop("completed"); return; }
        var started = Stopwatch.GetTimestamp();
        try
        {
            var skeleton = _skeleton().Node;
            if (skeleton.GetInstanceId() != _skeletonInstance || skeleton.GetBoneCount() != _bones.Length) throw new InvalidDataException("Source skeleton changed during pose observation.");
            var matrices = new float[checked(_bones.Length * 12)];
            var globals = new float[matrices.Length];
            for (var index = 0; index < _bones.Length; ++index)
            {
                if (skeleton.GetBoneName(index).ToString() != _bones[index] || skeleton.GetBoneParent(index) != _boneParents[index])
                    throw new InvalidDataException("Source bone identity changed during pose observation.");
                WriteMatrix(skeleton.GetBonePose(index), matrices.AsSpan(index * 12, 12));
                WriteMatrix(skeleton.GetBoneGlobalPose(index), globals.AsSpan(index * 12, 12));
            }
            var body = new float[12]; WriteMatrix(_actor.GlobalTransform, body);
            var skeletonWorld = new float[12]; WriteMatrix(skeleton.GlobalTransform, skeletonWorld);
            if (matrices.Any(value => !float.IsFinite(value)) || globals.Any(value => !float.IsFinite(value)) ||
                body.Any(value => !float.IsFinite(value)) || skeletonWorld.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException("Actor pose contains a nonfinite matrix.");
            var ownerBytes = JsonSerializer.SerializeToUtf8Bytes(_owners(), OwnerJson);
            var bytes = checked((matrices.Length + globals.Length + body.Length + skeletonWorld.Length) * sizeof(float) + ownerBytes.Length);
            if (bytes > MaximumPoseBytes - _poseBytes) { Stop("sample-memory-limit"); return; }
            using var owners = JsonDocument.Parse(ownerBytes);
            _samples.Add(new(Stopwatch.GetElapsedTime(_started).TotalMilliseconds, step, checked((ulong)Engine.GetFramesDrawn()), Engine.GetPhysicsFrames(),
                _observer.GetTree().Paused, _actor.IsProcessing(), _actor.IsPhysicsProcessing(), _actor.IsOnFloor(), _actor.IsVisibleInTree(),
                Stopwatch.GetTimestamp(), body, skeletonWorld, matrices, globals, owners.RootElement.Clone()));
            _poseBytes += bytes;
        }
        catch (Exception error) { Stop("sampling-failed", error.Message); }
        finally { _captureMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

    internal void Stop(string reason, string? error = null)
    {
        if (!Active) return;
        Active = false;
        _observer.Physics = null; _observer.Render = null; _observer.Heartbeat = null;
        if (GodotObject.IsInstanceValid(_actor)) _actor.TreeExiting -= ActorExiting;
        _observer.TreeExiting -= ObserverExiting;
        try
        {
            _publish(new
            {
                schema = "opennv-native-actor-pose/v2",
                reference = _reference,
                actorKind = _actorKind,
                baseSource = _baseSource,
                bodyRid = _bodyRid,
                actorInstance = _actorInstance,
                skeletonInstance = _skeletonInstance,
                skeletonPath = _skeletonPath,
                sourceUnitsToMeters = _units,
                monotonicFrequency = Stopwatch.Frequency,
                startedTicks = _started,
                requestedSeconds = _seconds,
                observedMilliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                reason,
                error,
                sampleLimit = _sampleLimit,
                physicsSamples = _samples.Count(sample => sample.Step == "post-physics"),
                drawSamples = _samples.Count(sample => sample.Step == "post-draw"),
                boneNames = _bones,
                boneParents = _boneParents,
                boneRest = _boneRest,
                boneMatrixMeaning = "Bones=Skeleton3D pose; GlobalBones=Skeleton3D global pose relative to skeleton; World=SkeletonWorld*GlobalBones",
                coordinateSystem = "Godot meters; source(x,y,z) converts to Godot(x,z,-y)*sourceUnitsToMeters",
                phaseBoundary = "post-physics final-priority callback; post-draw RenderingServer.FramePostDraw; counters are Godot counters, not retail frame joins",
                retainedPosePayloadBytes = _poseBytes,
                matrixLayout = "basis-X,Y,Z,origin;12-floats-per-bone",
                captureMilliseconds = _captureMilliseconds,
                physicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
                maximumFramesPerSecond = Engine.MaxFps,
                samples = _samples,
                recording = false,
                gameplayMutations = false,
                boundary = "Read-only all bones of the actual shared actor Skeleton3D and existing owner clocks. Native named NiNode descendants, attachments, GPU skin matrices and final pixels are separate unjoined lanes. Diagnostic overhead included; retail temporal alignment unverified."
            });
        }
        catch (Exception publicationError) { GD.PushError("OPENNV_ACTOR_POSE_PUBLICATION_FAILED " + publicationError.Message); }
        finally
        {
            _samples.Clear();
            if (!_observerExiting && GodotObject.IsInstanceValid(_observer) && !_observer.IsQueuedForDeletion())
            {
                if (_observer.IsInsideTree()) _observer.QueueFree();
                else _observer.Free();
            }
        }
    }

    private static void WriteMatrix(Transform3D matrix, Span<float> result)
    {
        result[0] = matrix.Basis.X.X; result[1] = matrix.Basis.X.Y; result[2] = matrix.Basis.X.Z;
        result[3] = matrix.Basis.Y.X; result[4] = matrix.Basis.Y.Y; result[5] = matrix.Basis.Y.Z;
        result[6] = matrix.Basis.Z.X; result[7] = matrix.Basis.Z.Y; result[8] = matrix.Basis.Z.Z;
        result[9] = matrix.Origin.X; result[10] = matrix.Origin.Y; result[11] = matrix.Origin.Z;
    }
}
