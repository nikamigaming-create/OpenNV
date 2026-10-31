using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private Func<FalloutFormKey, Node3D>? _rigResolve;
    private Func<JsonElement, object>? _rigControl;
    private RuntimeObjectPoseObservation? _objectPoseObservation;

    internal void ConfigureRig(Func<FalloutFormKey, Node3D> resolve, Func<JsonElement, object> control) =>
        (_rigResolve, _rigControl) = (resolve, control);

    private bool DispatchRig(JsonElement command, ulong request)
    {
        var operation = command.GetProperty("op").GetString()!;
        if (operation.StartsWith("rig.", StringComparison.Ordinal))
        {
            var mutating = operation is not ("rig.source" or "rig.status");
            if (mutating && _inputRecording is not null)
                throw new InvalidOperationException("Finish the ordinary input segment before controlled rig preparation/actions.");
            if (mutating && _inputPlayback?.Active == true)
                _inputPlayback.Stop("Playback yielded to controlled rig preparation.", ReleaseAll);
            if (mutating) { _bot?.Stop(); _campaignBot.Stop(); ReleaseAll(); _replayCheckpointPrepared = false; }
            if (operation == "rig.quiesce")
            {
                _bot?.Stop(); ReleaseAll();
                _objectPoseObservation?.Stop("rig-quiesced");
                _creaturePoseObservation?.Stop("rig-quiesced");
            }
            var state = (_rigControl ?? throw new NotSupportedException("No active shared rig control owner."))(command);
            AtomicWrite(Path.Combine(_directory, $"{request:D10}.rig.json"), JsonSerializer.Serialize(new
            {
                request,
                operation,
                state,
                ordinaryInput = false,
                evidenceKind = "controlled-rig-not-campaign-acceptance"
            }, Json));
            return true;
        }
        if (operation != "object.pose") return false;
        if (_objectPoseObservation is { Active: true }) throw new InvalidOperationException("An object pose observation is active.");
        if (DisplayServer.GetName() == "headless") throw new NotSupportedException("Object pose observation requires a display.");
        var reference = ParseRigReference(command.GetProperty("reference").GetString()!);
        var seconds = command.GetProperty("seconds").GetDouble();
        if (!double.IsFinite(seconds) || seconds is < .1 or > 8) throw new ArgumentException("Object pose requires 0.1–8 seconds.");
        var node = (_rigResolve ?? throw new NotSupportedException("No resident source reference resolver."))(reference);
        if (node is RuntimeNativeNpc or RuntimeNativeCreature) throw new ArgumentException("Use actor.pose for NPC/CREA skeletons.");
        var path = Path.Combine(_directory, $"{request:D10}.object-pose.json");
        _objectPoseObservation = new(node, reference, seconds, report =>
        {
            try { AtomicWrite(path, JsonSerializer.Serialize(report, Json)); }
            finally { _objectPoseObservation = null; }
        });
        _objectPoseObservation.Start(GetTree().Root);
        return true;
    }

    internal static FalloutFormKey ParseRigReference(string text)
    {
        var parts = text.Split(':');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
            !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException("Expected plugin:hex-object-id.");
        return new(parts[0], id);
    }
}

internal sealed class RuntimeObjectPoseObservation
{
    private const int MaximumBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Node3D _root;
    private readonly Node3D[] _nodes;
    private readonly ulong[] _instances;
    private readonly string[] _paths, _names;
    private readonly int[] _parents;
    private readonly RuntimeNifControllerPlayer[] _controllers;
    private readonly MeshInstance3D[] _morphs;
    private readonly Skeleton3D[] _skeletons;
    private readonly string _reference;
    private readonly double _seconds;
    private readonly Action<object> _publish;
    private readonly RuntimeCreaturePoseStepObserver _observer = new()
    {
        ProcessMode = Node.ProcessModeEnum.Always,
        ProcessPriority = int.MaxValue,
        ProcessPhysicsPriority = int.MaxValue
    };
    private readonly List<JsonElement> _samples = [];
    private long _started;
    private int _bytes;
    private double _cost;
    private bool _observerExiting;
    internal bool Active { get; private set; }

    internal RuntimeObjectPoseObservation(Node3D root, FalloutFormKey reference, double seconds, Action<object> publish)
    {
        _root = root; _reference = reference.ToString(); _seconds = seconds; _publish = publish;
        var graph = NodeTraversal.SelfAndDescendants<Node>(root).ToArray();
        _nodes = graph.OfType<Node3D>().ToArray();
        _instances = _nodes.Select(node => node.GetInstanceId()).ToArray();
        _paths = _nodes.Select(node => root.GetPathTo(node).ToString()).ToArray();
        _names = _nodes.Select(node => node.Name.ToString()).ToArray();
        _parents = _nodes.Select(node => Array.IndexOf(_nodes, node.GetParent())).ToArray();
        _controllers = graph.OfType<RuntimeNifControllerPlayer>().ToArray();
        _morphs = graph.OfType<MeshInstance3D>().Where(mesh => mesh.GetBlendShapeCount() != 0).ToArray();
        _skeletons = graph.OfType<Skeleton3D>().ToArray();
        if ((long)_nodes.Length * 24 * sizeof(float) > MaximumBytes / 2)
            throw new NotSupportedException("Object hierarchy exceeds bounded pose payload.");
    }

    internal void Start(Node parent)
    {
        _started = Stopwatch.GetTimestamp(); Active = true;
        _root.TreeExiting += RootExiting;
        _observer.TreeExiting += ObserverExiting;
        _observer.Physics = _ => Capture("post-physics");
        _observer.Render = _ => Capture("post-draw");
        _observer.Heartbeat = _ => { if (Stopwatch.GetElapsedTime(_started).TotalSeconds >= _seconds) Stop("duration-complete"); };
        try { parent.AddChild(_observer); }
        catch { Stop("observation-start-failed"); throw; }
    }

    private void Capture(string phase)
    {
        if (!Active) return;
        var began = Stopwatch.GetTimestamp();
        try
        {
            var local = new float[checked(_nodes.Length * 12)];
            var world = new float[local.Length];
            var visible = new bool[_nodes.Length];
            for (var index = 0; index < _nodes.Length; ++index)
            {
                var node = _nodes[index];
                if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || node.GetInstanceId() != _instances[index])
                    throw new InvalidOperationException("Actual source hierarchy retired during object pose observation.");
                Matrix(node.Transform, local.AsSpan(index * 12, 12));
                Matrix(node.GlobalTransform, world.AsSpan(index * 12, 12));
                visible[index] = node.IsVisibleInTree();
            }
            var sample = JsonSerializer.SerializeToElement(new
            {
                phase,
                ticks = Stopwatch.GetTimestamp(),
                drawFrame = Engine.GetFramesDrawn(),
                physicsFrame = Engine.GetPhysicsFrames(),
                paused = _root.GetTree().Paused,
                local,
                world,
                visible,
                controllers = _controllers.Select(controller => new
                {
                    instance = controller.GetInstanceId(),
                    controller.SourceSha256,
                    controller.SourceController,
                    owner = controller.Observation
                }).ToArray(),
                morphs = _morphs.Select(mesh => new
                {
                    instance = mesh.GetInstanceId(),
                    path = _root.GetPathTo(mesh).ToString(),
                    values = Enumerable.Range(0, mesh.GetBlendShapeCount()).Select(mesh.GetBlendShapeValue).ToArray()
                }).ToArray(),
                skeletons = _skeletons.Select(skeleton => new
                {
                    instance = skeleton.GetInstanceId(),
                    path = _root.GetPathTo(skeleton).ToString(),
                    names = Enumerable.Range(0, skeleton.GetBoneCount()).Select(index => skeleton.GetBoneName(index).ToString()).ToArray(),
                    parents = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBoneParent).ToArray(),
                    local = Enumerable.Range(0, skeleton.GetBoneCount()).Select(index => MatrixArray(skeleton.GetBonePose(index))).ToArray(),
                    global = Enumerable.Range(0, skeleton.GetBoneCount()).Select(index => MatrixArray(skeleton.GetBoneGlobalPose(index))).ToArray()
                }).ToArray()
            }, Json);
            var size = System.Text.Encoding.UTF8.GetByteCount(sample.GetRawText());
            if (_samples.Count >= 8192 || size > MaximumBytes - _bytes) { Stop("payload-bound"); return; }
            _bytes += size; _samples.Add(sample);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or ArgumentException)
        { Stop("observation-failed", exception.Message); }
        finally { _cost += Stopwatch.GetElapsedTime(began).TotalMilliseconds; }
    }

    private void RootExiting() => Stop("source-root-retired");
    private void ObserverExiting() { _observerExiting = true; Stop("observer-retired"); }

    internal void Stop(string reason, string? error = null)
    {
        if (!Active) return;
        Active = false; _observer.Physics = null; _observer.Render = null; _observer.Heartbeat = null;
        if (GodotObject.IsInstanceValid(_root)) _root.TreeExiting -= RootExiting;
        _observer.TreeExiting -= ObserverExiting;
        try
        {
            _publish(new
            {
                schema = "opennv-native-object-pose/v1",
                reference = _reference,
                reason,
                error,
                sourceRootInstance = _root.GetInstanceId(),
                monotonicFrequency = Stopwatch.Frequency,
                startedTicks = _started,
                requestedSeconds = _seconds,
                observedMilliseconds = Stopwatch.GetElapsedTime(_started).TotalMilliseconds,
                paths = _paths,
                names = _names,
                parents = _parents,
                instances = _instances,
                matrixLayout = "basis-X,Y,Z,origin;12 floats;Godot meters",
                captureMilliseconds = _cost,
                retainedPayloadBytes = _bytes,
                samples = _samples,
                gameplayMutations = false,
                recording = false,
                physicsSamples = _samples.Count(sample => sample.GetProperty("phase").GetString() == "post-physics"),
                drawSamples = _samples.Count(sample => sample.GetProperty("phase").GetString() == "post-draw"),
                hierarchyMembership = "actual source subtree at observation start; later inserted nodes are not joined",
                boundary = "Actual Node3D transforms, controller clocks and morph weights; shader parameters, skin matrices, audio and final pixels are independent evidence lanes."
            });
        }
        finally
        {
            _samples.Clear();
            if (!_observerExiting && GodotObject.IsInstanceValid(_observer) && !_observer.IsQueuedForDeletion())
            { if (_observer.IsInsideTree()) _observer.QueueFree(); else _observer.Free(); }
        }
    }

    private static void Matrix(Transform3D value, Span<float> output)
    {
        var data = new[] { value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z, value.Basis.Y.X, value.Basis.Y.Y,
            value.Basis.Y.Z, value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z, value.Origin.X, value.Origin.Y, value.Origin.Z };
        if (data.Any(number => !float.IsFinite(number))) throw new InvalidDataException("Object transform is not finite.");
        data.AsSpan().CopyTo(output);
    }

    private static float[] MatrixArray(Transform3D value)
    {
        var result = new float[12]; Matrix(value, result); return result;
    }
}
