using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerThreadContextEntry(string Source, Guid Process, Guid Factory, Guid Main,
    long MainOrdinal, int Thread, uint BeforeKind, uint CurrentKind, byte Initialized, bool InitializerReturned, bool ScopeEntered);
internal sealed record FalloutPlayerThreadContextExit(string Source, Guid Process, Guid Factory, Guid Main,
    long MainOrdinal, int Thread, uint BeforeKind, uint RestoredKind, byte Initialized);
internal sealed record FalloutPlayerThreadModelCacheRead(string Source, Guid Process, Guid Factory, int Thread,
    FalloutFormKey Actor, FalloutFormKey? CachedReference, FalloutSourceSceneNode? Model, bool Matched);

// Typed C# ownership of these two current-thread fields. Original DLLs still
// require a complete independent x86 TLS image; this state never supplies a
// partial native buffer or substitutes a copied loader image for that owner.
internal sealed partial class FalloutPlayerThreadContextState
{
    [ThreadStatic] private static Dictionary<Guid, FalloutPlayerThreadContextState>? _current;
    private readonly FalloutPlayerThreadContextSource _source;
    private readonly Guid _process, _factory = Guid.NewGuid();
    private int? _thread;
    private byte _initialized;
    private uint _kind;
    private bool _retired;
    private readonly FalloutFormKey? _cachedReference;
    private readonly FalloutSourceSceneNode? _cachedModel;
    internal FalloutPlayerThreadContextEntry? Entry { get; private set; }
    internal FalloutPlayerThreadContextExit? Exit { get; private set; }
    internal object State => new
    {
        source = _source.Identity,
        process = _process,
        factory = _factory,
        thread = _thread,
        initialized = _initialized,
        kind = _kind,
        entry = Entry,
        cachedReference = _cachedReference,
        cachedModel = _cachedModel,
        retired = _retired,
        nativeModuleTls = "complete-independent-owner-required",
        scopeExit = Exit,
        scopeEntered = Entry is not null && Exit is null
    };
    internal FalloutPlayerThreadContextState(FalloutPlayerThreadContextSource source, Guid process)
    {
        source.Validate();
        if (process == Guid.Empty) throw new ArgumentException("Thread context requires its actual source Main process.", nameof(process));
        _source = source; _process = process; _initialized = source.InitialInitialized; _kind = source.InitialKind;
        if (source.InitialModel != 0 || source.InitialReference != 0)
            throw new NotSupportedException("Current-thread model cache has no admitted nonnull loader factory.");
        _cachedReference = null; _cachedModel = null;
    }
    internal FalloutPlayerThreadContextEntry EnterPlayer(Guid main, long ordinal)
    {
        if (_retired || Entry is not null && (Exit is null || Entry.Main == main || ordinal <= Entry.MainOrdinal) ||
            main == Guid.Empty || ordinal < 1 ||
            _thread is { } previousThread && previousThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player context lost or replayed its current source thread/scope lifetime.");
        _current ??= [];
        if (_current.TryGetValue(_process, out var existing) && !ReferenceEquals(existing, this))
            throw new InvalidOperationException("Another live context owns this actual Main/current-thread process.");
        _current[_process] = this; _thread = Environment.CurrentManagedThreadId;
        // The selected on-demand source initializer sets this byte first and
        // has a proven empty initializer range. It allocates no proxy objects.
        var fields = CurrentKindFields();
        if (fields.Entered is not null)
            throw new InvalidOperationException("Player cannot enter across an unreturned source menu allocation.");
        fields.Initialized = _initialized = 1;
        var before = fields.Kind; fields.Kind = _kind = _source.PlayerKind;
        Entry = new(_source.Identity, _process, _factory, main, ordinal, _thread.Value,
            before, _kind, _initialized, true, true);
        Exit = null;
        return Entry;
    }
    internal FalloutPlayerThreadContextExit ExitPlayer(Guid main, long ordinal)
    {
        if (_retired || Exit is not null || Entry is not { ScopeEntered: true, InitializerReturned: true } entry ||
            entry.Main != main || entry.MainOrdinal != ordinal || _thread != Environment.CurrentManagedThreadId ||
            _kind != entry.CurrentKind || _initialized != 1 || CurrentKindFields().Entered is not null || _current is null ||
            !_current.TryGetValue(_process, out var current) || !ReferenceEquals(current, this))
            throw new InvalidOperationException("Player context exit lost its entered source call, current thread or prior kind.");
        // The epilogue restores the value captured by this invocation. It is
        // reached only after the Player body returns; failure and retirement
        // preserve an entered scope rather than issuing this store in finally.
        var before = _kind; CurrentKindFields().Kind = _kind = entry.BeforeKind;
        Exit = new(_source.Identity, _process, _factory, main, ordinal, _thread.Value,
            before, _kind, _initialized);
        return Exit;
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player thread context cannot retire on a foreign source thread.");
        if (_current?.TryGetValue(_process, out var existing) == true)
        {
            if (!ReferenceEquals(existing, this)) throw new InvalidOperationException("Retirement changed the actual source thread owner.");
            _current.Remove(_process);
        }
        // Retirement releases the C# current owner. It never invents the
        // original scope-exit store or marks the failed Player virtual returned.
        _kindFields.Clear();
        _retired = true;
    }
    internal FalloutPlayerThreadModelCacheRead ReadPlayerModelCache(FalloutFormKey actor)
    {
        if (_retired || _thread != Environment.CurrentManagedThreadId || _initialized != 1 ||
            Entry is not { ScopeEntered: true, InitializerReturned: true } || Exit is not null ||
            _current is null || !_current.TryGetValue(_process, out var current) || !ReferenceEquals(current, this) ||
            string.IsNullOrWhiteSpace(actor.OwnerPlugin) || actor.ObjectId == 0)
            throw new InvalidOperationException("Player model cache lost its actual current thread and source scope.");
        return new(_source.Identity, _process, _factory, _thread.Value, actor, _cachedReference, _cachedModel, _cachedReference == actor);
    }
}
