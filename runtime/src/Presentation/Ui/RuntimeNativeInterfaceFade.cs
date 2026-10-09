using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

// The actual source UI factory supplies these two roots. A new arbitrary
// CanvasLayer or a calendar clock is not a substitute for either lease.
internal sealed record RuntimeNativeInterfaceFadeRoot(FalloutInterfaceFadeRoot Role, Control Root, string SourceOwner)
{
    internal void Require(Viewport viewport)
    {
        if (!Enum.IsDefined(Role) || string.IsNullOrWhiteSpace(SourceOwner) || !GodotObject.IsInstanceValid(Root) ||
            !Root.IsInsideTree() || Root.GetViewport() != viewport)
            throw new NotSupportedException("Interface fade has no actual selected UI-root/viewport publication.");
    }
}

internal sealed partial class RuntimeNativeInterfaceFade : Node
{
    private readonly FalloutInterfaceFade _owner;
    private readonly RuntimeLiveContentSource _source;
    private readonly Func<FalloutInterfaceFadeClock> _clock;
    private readonly IReadOnlyDictionary<FalloutInterfaceFadeRoot, RuntimeNativeInterfaceFadeRoot> _roots;
    private readonly IReadOnlyDictionary<FalloutInterfaceFadeRoot, ulong> _rootIds;
    private readonly Action<Exception> _failed;
    private readonly NativeOwnedInterfaceFadeQuad?[] _quads = new NativeOwnedInterfaceFadeQuad?[3];
    private bool _retired, _subscribed;
    private Exception? _attachmentFailure;
    internal object State => new
    {
        source = _owner.State,
        retired = _retired,
        roots = _roots.Values.Select(root => new { root.Role, root.SourceOwner, id = _rootIds[root.Role] }).ToArray(),
        geometry = _quads.Select(quad => quad?.State).ToArray(),
        unowned = "matched-retail-alpha-pixels,XR-root-surface-and-final-eye"
    };

    private RuntimeNativeInterfaceFade(FalloutInterfaceFade owner, RuntimeLiveContentSource source,
        Func<FalloutInterfaceFadeClock> sourceUiClock,
        IReadOnlyDictionary<FalloutInterfaceFadeRoot, RuntimeNativeInterfaceFadeRoot> roots, Action<Exception> failed)
    {
        _owner = owner; _source = source; _clock = sourceUiClock; _roots = roots; _failed = failed;
        _rootIds = roots.ToDictionary(root => root.Key, root => root.Value.Root.GetInstanceId());
        Name = "SourceInterfaceFade"; ProcessMode = ProcessModeEnum.Always;
    }

    internal static RuntimeNativeInterfaceFade Attach(Node parent, FalloutInterfaceFade owner,
        FalloutAdvancementRuntimeSource runtime, Func<FalloutInterfaceFadeClock> actualSourceUiClock,
        IReadOnlyList<RuntimeNativeInterfaceFadeRoot> actualSourceRoots, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(actualSourceUiClock); ArgumentNullException.ThrowIfNull(failed);
        runtime.RequireLivingRestSource(); owner.Source.Validate();
        if (!parent.IsInsideTree() || owner.Source.RuntimeSha256 != runtime.Receipt.SourceSha256 ||
            owner.Source.Declaration.EngineSha256 != runtime.Receipt.EngineSha256 || actualSourceRoots.Count != 2 ||
            actualSourceRoots.Select(root => root.Role).Distinct().Count() != 2)
            throw new NotSupportedException("Source interface fade cannot attach without its exact source and both native UI roots.");
        var viewport = parent.GetViewport(); foreach (var root in actualSourceRoots) root.Require(viewport);
        var result = new RuntimeNativeInterfaceFade(owner, runtime.OwnedSource, actualSourceUiClock,
            actualSourceRoots.ToDictionary(root => root.Role), failed);
        try
        {
            parent.AddChild(result);
            if (result.GetParent() != parent || !result.IsInsideTree() || result._attachmentFailure is not null)
                throw new InvalidOperationException("Source interface fade native attachment failed.", result._attachmentFailure);
            return result;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            try { result.Retire(); }
            catch (Exception cleanup) when (FalloutPlayerPhysicalActivity.Ordinary(cleanup)) { failures.Add(cleanup); }
            try { result.Free(); }
            catch (Exception cleanup) when (FalloutPlayerPhysicalActivity.Ordinary(cleanup)) { failures.Add(cleanup); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Interface fade attachment and native retirement failed.", failures);
        }
    }

    public override void _Ready()
    {
        try
        {
            RenderingServer.FramePostDraw += AfterDraw; _subscribed = true;
            _owner.BindNative(new(Publish, Write, RetireChannel));
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { _attachmentFailure ??= error; Fail(null, FalloutInterfaceFadeOperation.NativePublication, error); }
    }
    public override void _Process(double delta)
    {
        if (_retired || _owner.Failure is not null || !_owner.Channels.Any(channel =>
            channel.Direction != FalloutInterfaceFadeDirection.Absent)) return;
        try { _owner.Advance(_clock()); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { Fail(null, FalloutInterfaceFadeOperation.Frame, error); }
    }
    private void Publish(FalloutInterfaceFadeChannel channel)
    {
        if (_retired || _quads[channel.Channel] is not null)
            throw new InvalidOperationException("Interface fade geometry cannot publish twice.");
        var row = _owner.Source.Declaration.Rows[channel.Channel]; var root = _roots[row.Root]; root.Require(GetViewport());
        _quads[channel.Channel] = NativeOwnedInterfaceFadeQuad.Attach(root.Root, _source,
            _owner.Source.Textures[channel.Channel], channel,
            error => Fail(channel.Channel, FalloutInterfaceFadeOperation.NativePublication, error));
    }
    private void Write(FalloutInterfaceFadeChannel channel) => (_quads[channel.Channel] ??
        throw new InvalidOperationException("Interface fade lost its original native geometry.")).Write(channel);
    private void RetireChannel(FalloutInterfaceFadeChannel channel)
    {
        var quad = _quads[channel.Channel] ?? throw new InvalidOperationException("Interface fade has no original geometry to retire.");
        quad.RequireGeneration(channel); quad.Retire(); _quads[channel.Channel] = null;
    }
    private void AfterDraw()
    {
        if (_retired) return;
        foreach (var quad in _quads.Where(quad => quad is not null).Cast<NativeOwnedInterfaceFadeQuad>())
        {
            try
            {
                if (quad.SubmittedRevision is { } revision)
                    _owner.ObserveNativeDraw(quad.Channel, quad.Generation, revision);
            }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            { Fail(quad.Channel, FalloutInterfaceFadeOperation.NativePublication, error); }
        }
    }
    private void Fail(int? channel, FalloutInterfaceFadeOperation operation, Exception error)
    { _owner.ReportNativeFailure(channel, operation, error); _failed(error); }

    internal FalloutRestObservation ObserveSourceStart()
    {
        var identity = "actual-interface-fade:" + _owner.Source.Identity;
        if (_retired || !IsInsideTree() || _owner.Failure is not null)
            return new(FalloutRestFactState.Unowned, identity, "Source fade native publication is absent, retired or failed.");
        try
        {
            foreach (var root in _roots.Values) root.Require(GetViewport());
            _clock().Validate();
            return new(FalloutRestFactState.Satisfied, identity);
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            Fail(null, FalloutInterfaceFadeOperation.NativePublication, error);
            return new(FalloutRestFactState.Unowned, identity, error.GetType().Name + ": " + error.Message);
        }
    }

    internal void Retire()
    {
        if (_retired) return;
        _retired = true;
        var failures = new List<Exception>();
        void Release(int? channel, Action release)
        {
            try { release(); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            { failures.Add(error); _owner.ReportNativeFailure(channel, FalloutInterfaceFadeOperation.NativeRetirement, error); }
        }
        if (_subscribed) { Release(null, () => RenderingServer.FramePostDraw -= AfterDraw); _subscribed = false; }
        for (var channel = 0; channel < _quads.Length; ++channel)
        {
            var quad = _quads[channel]; _quads[channel] = null;
            if (quad is not null) Release(channel, quad.Retire);
        }
        Release(null, _owner.RetireNativeLifetime);
        if (failures.Count != 0) throw new AggregateException("Source interface fade retained native cleanup failures.", failures);
    }
    public override void _ExitTree()
    {
        try { Retire(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { _failed(error); }
    }
}
