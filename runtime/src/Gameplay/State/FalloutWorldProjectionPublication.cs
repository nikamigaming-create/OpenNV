using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutWorldProjectionPublicationSnapshot(string Source, string Stack, Guid CapturedWorld,
    Guid Factory, long Replacements, Guid? CurrentRootFactory, FalloutWorldProjectionSnapshot? Current);

// This is the actual shared C# current-root owner. Main and presentation borrow
// it; neither a settings row nor a native camera can publish a root here.
internal sealed class FalloutWorldProjectionPublication
{
    private readonly FalloutPlayerWorldOrientationSource _source;
    private readonly string _stack;
    private readonly Guid _world;
    private readonly Func<bool> _living;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Guid _factory = Guid.NewGuid();
    private FalloutWorldProjectionOwner? _current;
    private long _replacements;
    private Guid? _previousWorld, _previousRootFactory;
    internal FalloutPlayerWorldOrientationSource Source => _source;
    internal string Stack => _stack;
    internal Guid World => _world;
    internal Guid? CurrentRootFactory { get { RequireCurrent(); return _current?.Factory; } }
    internal string? SaveBlocker => _current?.SaveBlocker ?? (_current is null ? "source-current-World-root-publication-unowned" : null);
    internal object State => new
    {
        source = _source.Identity,
        stack = _stack,
        world = _world,
        factory = _factory,
        replacements = _replacements,
        currentRootFactory = _current?.Factory,
        current = _current?.State,
        previousWorld = _previousWorld,
        previousRootFactory = _previousRootFactory
    };
    internal FalloutWorldProjectionPublication(FalloutPlayerWorldOrientationSource source, string stack, Guid world, Func<bool> living)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(living);
        if (world == Guid.Empty) throw new ArgumentException("World root publication needs its actual source world lifetime.", nameof(world));
        _source = source; _stack = stack; _world = world; _living = living;
    }
    internal void PublishReturned(FalloutWorldProjectionOwner root)
    {
        RequireCurrent(); root.RequireCurrent();
        if (root.Source != _source || root.Stack != _stack || root.World != _world || !root.ConstructorReturned)
            throw new InvalidOperationException("World global root requires an actual returned source constructor in its current world.");
        if (ReferenceEquals(_current, root)) return;
        var previous = _current;
        previous?.Retire(); _current = root; _replacements = checked(_replacements + 1);
    }
    internal FalloutWorldProjectionOwner Current
    {
        get { RequireCurrent(); var root = _current ?? throw new NotSupportedException("source-current-World-root-publication-unowned"); root.RequireCurrent(); return root; }
    }
    internal void RequireCurrent()
    {
        if (_thread != Environment.CurrentManagedThreadId || !_living())
            throw new InvalidOperationException("World root publication lost its actual thread/world lifetime.");
    }
    internal FalloutWorldProjectionPublicationSnapshot Capture()
    {
        RequireCurrent(); return new(_source.Identity, _stack, _world, _factory, _replacements, _current?.Factory, _current?.Capture());
    }
    internal void Restore(FalloutWorldProjectionPublicationSnapshot saved, FalloutWorldProjectionOwner? freshRoot)
    {
        RequireCurrent();
        if (_current is not null || saved.Source != _source.Identity || saved.Stack != _stack || saved.CapturedWorld == _world ||
            saved.CapturedWorld == Guid.Empty || saved.Factory == Guid.Empty || saved.Replacements < 0 ||
            (saved.Current is null) != (saved.CurrentRootFactory is null) || (saved.Current is null) != (freshRoot is null))
            throw new InvalidDataException("Cold world publication changed source/current root or reused its old lifetime.");
        if (freshRoot is not null)
        {
            FalloutWorldProjectionOwner.Validate(saved.Current!); freshRoot.RequireCurrent();
            if (saved.CurrentRootFactory != saved.Current!.Factory || freshRoot.Source != _source || freshRoot.Stack != _stack ||
                saved.Current.CapturedWorld != saved.CapturedWorld || freshRoot.World != _world ||
                freshRoot.Factory == saved.CurrentRootFactory || !freshRoot.ConstructorReturned || saved.Replacements < 1)
                throw new InvalidDataException("Cold world publication selected a historical, foreign or incomplete root.");
            var rebound = freshRoot.Capture() with { CapturedWorld = saved.Current.CapturedWorld, Factory = saved.Current.Factory };
            if (System.Text.Json.JsonSerializer.Serialize(rebound) != System.Text.Json.JsonSerializer.Serialize(saved.Current))
                throw new InvalidDataException("Cold publication selected a returned root with different retained current source fields.");
        }
        _current = freshRoot; _replacements = saved.Replacements;
        _previousWorld = saved.CapturedWorld; _previousRootFactory = saved.CurrentRootFactory;
    }
    internal void RequireColdReferenceProjection(Guid previousWorld, Guid? previousRootFactory)
    {
        RequireCurrent();
        if (_previousWorld != previousWorld || _previousRootFactory != previousRootFactory || _previousWorld is null)
            throw new InvalidDataException("Cold reference orientation changed its actual captured World/root publication.");
    }
    internal void RetireCurrent() { RequireCurrent(); _current?.Retire(); _current = null; }
}
