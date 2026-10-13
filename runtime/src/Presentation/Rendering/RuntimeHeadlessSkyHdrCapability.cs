using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

// A separate capability provider for a host with no renderer. Construction and
// retirement complete on the product thread; no render callback or RID exists.
internal sealed class RuntimeHeadlessSkyHdrCapability : IRuntimeSkyHdrCapability
{
    private readonly FalloutSkyHdrSource _source;
    private readonly Guid _process;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private bool _entered, _retired;
    private long _changed;
    private Task? _retirement;

    public Guid Identity { get; } = Guid.NewGuid();
    public bool Returned { get { RequireThread(); return _entered && !_retired; } }
    public bool Retired { get { RequireThread(); return _retired; } }
    public Task Retirement
    {
        get
        {
            RequireThread();
            return _retirement ?? throw new InvalidOperationException("Headless HDR retirement has not entered.");
        }
    }

    internal RuntimeHeadlessSkyHdrCapability(FalloutSkyHdrSource source, Guid process)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (process == Guid.Empty)
            throw new InvalidDataException("Headless HDR has no current source process.");
        if (DisplayServer.GetName() != "headless" || RenderingServer.GetRenderingDevice() is not null)
            throw new InvalidOperationException("Headless HDR requires a host with no native rendering device.");
        _source = source;
        _process = process;
    }

    public void Begin()
    {
        RequireThread();
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_entered) throw new InvalidOperationException("Headless HDR construction was replayed.");
        _entered = true;
        ++_changed;
    }

    public FalloutSkyHdrCapability Read()
    {
        RequireThread();
        ObjectDisposedException.ThrowIf(_retired, this);
        if (!_entered) throw new InvalidOperationException("Headless HDR construction has not returned.");
        return new(Identity, _process, _source.EngineSha256, _source.Contract, _changed,
            false, false, FalloutSkyHdrCapability.HeadlessProducer,
            FalloutSkyHdrCapabilityProvider.HeadlessNoRenderer);
    }

    public void BeginRetirement()
    {
        RequireThread();
        if (_retired) return;
        _retired = true;
        ++_changed;
        _retirement = Task.CompletedTask;
    }

    private void RequireThread()
    {
        if (System.Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Headless HDR left its actual product thread.");
    }
}
