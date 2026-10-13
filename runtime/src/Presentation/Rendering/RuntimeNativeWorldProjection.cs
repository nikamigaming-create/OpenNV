using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

// This adapter supplies only renderer presence and the actual current target
// extent. Logical C# World/camera owners produce all FOV, pose and projection.
internal sealed class RuntimeNativeWorldProjection
{
    private readonly FalloutPlayerWorldOrientationSource _source;
    private readonly string _stack;
    private readonly Guid _world;
    private readonly bool _rendererPresent;
    private readonly Viewport? _target;
    private readonly ulong? _targetIdentity;
    private readonly Func<bool> _living;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private bool _retired;
    internal Guid Factory { get; } = Guid.NewGuid();

    internal RuntimeNativeWorldProjection(FalloutPlayerWorldOrientationSource source, string stack, Guid world,
        Viewport? actualTarget, Func<bool> actualTargetLiving)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(actualTargetLiving);
        if (world == Guid.Empty || !actualTargetLiving())
            throw new InvalidOperationException("World projection target has no actual current gameplay world.");
        _source = source; _stack = stack; _world = world; _living = actualTargetLiving;
        _rendererPresent = !RuntimeInputPresentationSelection.UsesHeadlessPresentation(DisplayServer.GetName());
        if (!_rendererPresent)
        {
            if (RenderingServer.GetRenderingDevice() is not null)
                throw new InvalidOperationException("Headless projection selected a host with a native rendering device.");
            return;
        }
        _target = actualTarget ?? throw new InvalidOperationException("Rendered projection has no actual current target owner.");
        RequireNativeTarget(); _targetIdentity = _target.GetInstanceId();
    }

    internal FalloutWorldProjectionTargetRead Read()
    {
        RequireCurrent();
        if (!_rendererPresent)
            return new(_source.Identity, _stack, _world, Factory, false, null, null, "actual-headless-no-native-renderer");
        RequireNativeTarget();
        if (_target!.GetInstanceId() != _targetIdentity)
            throw new InvalidOperationException("Projection target changed its actual native owner identity.");
        Vector2I size;
        if (_target is SubViewport viewport) size = viewport.Size;
        else
        {
            // The root Window may have a different logical/content scale size.
            // Its actual viewport texture reports the current render extent.
            var texture = _target.GetTexture();
            if (!GodotObject.IsInstanceValid(texture))
                throw new InvalidOperationException("Projection target lost its current rendered texture.");
            size = new(texture.GetWidth(), texture.GetHeight());
        }
        if (size.X <= 0 || size.Y <= 0)
            throw new NotSupportedException("source-current-render-target-positive-extent-unavailable");
        RequireCurrent(); RequireNativeTarget();
        return new(_source.Identity, _stack, _world, Factory, true, checked((uint)size.X), checked((uint)size.Y),
            "actual-current-native-viewport-render-target-extent");
    }

    internal void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != System.Environment.CurrentManagedThreadId || !_living())
            throw new InvalidOperationException("Projection target left its actual native thread/world lifetime.");
        if (_rendererPresent == RuntimeInputPresentationSelection.UsesHeadlessPresentation(DisplayServer.GetName()))
            throw new InvalidOperationException("Projection target changed its actual presentation backend.");
    }
    internal void Retire()
    {
        if (_retired) return;
        RequireCurrent(); _retired = true;
    }
    private void RequireNativeTarget()
    {
        if (!GodotObject.IsInstanceValid(_target) || _target!.IsQueuedForDeletion() || !_target.IsInsideTree())
            throw new InvalidOperationException("Projection target lost its actual current native viewport lifetime.");
    }
}
