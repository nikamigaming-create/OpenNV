using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.InputSystem;

namespace OpenNV.Runtime.Gameplay.State;

// MenuMode, input and pre-draw script owners can run while the world is paused.
// Save preparation freezes their exact subtree, not the global pause policy.
internal sealed class RuntimeNativeSaveProducerPause : IDisposable
{
    private readonly IReadOnlyList<Node> _roots;
    private readonly List<(Node Node, ulong Id, Node.ProcessModeEnum Mode, IDisposable? InputPause)> _nodes = [];
    private bool _disposed;

    internal RuntimeNativeSaveProducerPause(IReadOnlyList<Node> roots)
    {
        _roots = roots;
        try
        {
            foreach (var node in Descendants().DistinctBy(node => node.GetInstanceId()))
            {
                if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || node.IsQueuedForDeletion())
                    throw new FalloutFiniteSoundSaveDrainInvalidatedException("A source producer retired before save preparation.");
                var mode = node.ProcessMode;
                var inputPause = node is RuntimeNativeScriptEvents events ? events.PauseForManualSave() : null;
                _nodes.Add((node, node.GetInstanceId(), mode, inputPause));
                if (inputPause is null) node.ProcessMode = Node.ProcessModeEnum.Disabled;
            }
        }
        catch { Dispose(); throw; }
    }

    private IEnumerable<Node> Descendants()
    {
        var pending = new Stack<Node>(_roots.Reverse());
        while (pending.TryPop(out var node))
        {
            if (!GodotObject.IsInstanceValid(node))
                throw new FalloutFiniteSoundSaveDrainInvalidatedException("A source producer was replaced during save preparation.");
            yield return node;
            foreach (var child in node.GetChildren().AsEnumerable().Reverse()) pending.Push(child);
        }
    }

    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nodes.Any(binding => !GodotObject.IsInstanceValid(binding.Node) || !binding.Node.IsInsideTree() ||
            binding.Node.IsQueuedForDeletion() || binding.Node.GetInstanceId() != binding.Id ||
            (binding.InputPause is null ? binding.Node.ProcessMode != Node.ProcessModeEnum.Disabled :
                binding.Node is not RuntimeNativeScriptEvents { SavePreparationPaused: true, ProcessMode: Node.ProcessModeEnum.Always })) ||
            !Descendants().Select(node => node.GetInstanceId()).Distinct().Order().SequenceEqual(_nodes.Select(binding => binding.Id).Order()))
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("Source-producer binding, generation or process flags changed while saving.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> errors = [];
        foreach (var (node, id, mode, inputPause) in _nodes.AsEnumerable().Reverse())
        {
            try { inputPause?.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                try
                {
                    if (GodotObject.IsInstanceValid(node) && node.GetInstanceId() == id) node.ProcessMode = mode;
                }
                catch (Exception error) { errors.Add(error); }
            }
        }
        if (errors.Count != 0) throw new AggregateException("Source-producer save-pause cleanup failed.", errors);
    }
}
