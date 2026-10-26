using System.Text.Json;
using Godot;
using Environment = System.Environment;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private sealed class PipelineStatistics(int thread)
    {
        internal readonly RuntimeFramePhaseCounter Process = new(thread), Physics = new(thread), Draw = new(thread);
    }
    private static PipelineStatistics? ProcessPipelineStatistics;
    private PipelineStatistics? _pipeline;
    private RuntimeFramePhaseMarker? _pipelineHead, _pipelineTail;
    private bool _pipelineOrderingPending;
    private long _pipelineOrderingInvalidations;

    private void SetupPipelinePhases()
    {
        if (!IsInsideTree() || _pipelineHead is not null) return;
        var pipeline = _pipeline = ProcessPipelineStatistics ??= new(Environment.CurrentManagedThreadId);
        RuntimeFramePhaseMarker Marker(bool head) => new()
        {
            Name = head ? "NativeProcessPhaseHead" : "NativeProcessPhaseTail", Head = head,
            ProcessPhase = pipeline.Process, PhysicsPhase = pipeline.Physics, ProcessMode = ProcessModeEnum.Always,
            ProcessPriority = head ? int.MinValue : int.MaxValue, ProcessPhysicsPriority = head ? int.MinValue : int.MaxValue
        };
        _pipelineHead = Marker(true); _pipelineTail = Marker(false);
        var root = GetTree().Root; root.AddChild(_pipelineHead); root.AddChild(_pipelineTail);
        RestorePipelineOrdering(); GetTree().NodeAdded += PipelineNodeAdded;
    }
    private void PipelineNodeAdded(Node node)
    {
        if (node.GetParent() != GetTree().Root || _pipelineHead is null || _pipelineTail is null || _pipelineOrderingPending) return;
        _pipelineHead.CoverageValid = _pipelineTail.CoverageValid = false;
        _pipelineOrderingPending = true; _pipelineOrderingInvalidations++;
        Callable.From(RestorePipelineOrdering).CallDeferred();
    }
    private void RestorePipelineOrdering()
    {
        if (!IsInsideTree() || _pipelineHead is null || _pipelineTail is null) return;
        var root = GetTree().Root; root.MoveChild(_pipelineHead, 0); root.MoveChild(_pipelineTail, root.GetChildCount() - 1);
        _pipelineOrderingPending = false; _pipelineHead.CoverageValid = _pipelineTail.CoverageValid = true;
    }
    private object? CapturePipelinePhases() => _pipeline is null ? null : new
    {
        processCallbacks = _pipeline.Process.Capture(), physicsCallbacks = _pipeline.Physics.Capture(), drawSubmission = _pipeline.Draw.Capture(),
        markerOrderingPending = _pipelineOrderingPending, markerOrderingInvalidations = _pipelineOrderingInvalidations,
        scope = "process-lifetime cumulative main-thread head/tail node-callback spans; pre/post-draw host elapsed; inclusive, not additive, GPU/display presentation unverified"
    };
    private void RetirePipelinePhases()
    {
        GetTree().NodeAdded -= PipelineNodeAdded;
        foreach (var marker in new[] { _pipelineHead, _pipelineTail })
            if (marker is not null) { marker.SetProcess(false); marker.SetPhysicsProcess(false); marker.QueueFree(); }
        _pipeline?.Process.Close(); _pipeline?.Physics.Close(); _pipeline?.Draw.Close();
    }

    private bool DispatchProcessOwners(JsonElement command, ulong request)
    {
        if (command.GetProperty("op").GetString() != "process.owners") return false;
        var descendants = GetTree().Root.FindChildren("*", "", true, false);
        using var descendantsOwner = (Godot.Collections.Array)descendants;
        var groups = descendants.Prepend(GetTree().Root).Select(node => new
        {
            type = node.GetType().FullName ?? node.GetType().Name,
            process = node.IsProcessing(), physics = node.IsPhysicsProcessing(),
            internalProcess = node.IsProcessingInternal(), internalPhysics = node.IsPhysicsProcessingInternal(), canProcess = node.CanProcess(),
            threadGroup = node.ProcessThreadGroup
        }).GroupBy(node => node.type).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new
        {
            managedType = group.Key, nodes = group.Count(), processEnabled = group.Count(node => node.process),
            physicsEnabled = group.Count(node => node.physics), internalProcessEnabled = group.Count(node => node.internalProcess),
            internalPhysicsEnabled = group.Count(node => node.internalPhysics), canProcess = group.Count(node => node.canProcess),
            eligibleProcess = group.Count(node => node.process && node.canProcess), eligiblePhysics = group.Count(node => node.physics && node.canProcess),
            eligibleInternalProcess = group.Count(node => node.internalProcess && node.canProcess), eligibleInternalPhysics = group.Count(node => node.internalPhysics && node.canProcess),
            explicitSubThreadGroupNodes = group.Count(node => node.threadGroup == ProcessThreadGroupEnum.SubThread)
        }).ToArray();
        AtomicWrite(Path.Combine(_directory, $"{request:D10}.process-owners.json"), JsonSerializer.Serialize(new
        {
            schema = "opennv-process-owners/v1", process = Environment.ProcessId, threadId = Environment.CurrentManagedThreadId,
            drawFrame = Engine.GetFramesDrawn(), physicsFrame = Engine.GetPhysicsFrames(), sceneTreePaused = GetTree().Paused,
            groups, scope = "one explicit read-only graph snapshot; flags and eligibility counts, not individual callback timing"
        }, Json));
        return true;
    }
}
