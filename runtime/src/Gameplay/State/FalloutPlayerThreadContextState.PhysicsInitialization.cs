using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerThreadContextState
{
    private readonly Dictionary<Guid, FalloutSourceThreadKindScope> _physicsKindScopes = [];
    private readonly HashSet<Guid> _physicsModelReads = [];
    internal bool HasPhysicsInitializationScope(Guid invocation) => _physicsKindScopes.ContainsKey(invocation);
    internal IReadOnlyList<FalloutSourceThreadKindScope> CapturePhysicsInitializationScopes() => _physicsKindScopes.Values.ToArray();

    internal FalloutPlayerThreadModelCacheRead ReadEarlyControllerModelCache(Guid invocation, Guid ownerFactory,
        FalloutFormKey actor, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent); requireCurrent();
        if (_retired || invocation == Guid.Empty || ownerFactory == Guid.Empty || !_physicsModelReads.Add(invocation) ||
            _thread is { } thread && thread != Environment.CurrentManagedThreadId ||
            string.IsNullOrWhiteSpace(actor.OwnerPlugin) || actor.ObjectId == 0)
            throw new InvalidOperationException("Early model getter lost its actual constructor/current-thread cache owner.");
        RequireCurrentKindOwner(); var fields = CurrentKindFields(); fields.Initialized = 1;
        if (_thread == Environment.CurrentManagedThreadId) _initialized = 1;
        var value = new FalloutPlayerThreadModelCacheRead(_source.Identity, _process, _factory,
            Environment.CurrentManagedThreadId, actor, _cachedReference, _cachedModel, _cachedReference == actor);
        requireCurrent();
        if (_thread is null && _current?.TryGetValue(_process, out var current) == true && ReferenceEquals(current, this))
            _current.Remove(_process);
        return value;
    }

    internal FalloutSourceThreadKindScope EnterPhysicsInitialization(Guid invocation, Guid ownerFactory, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent); requireCurrent();
        if (_retired || invocation == Guid.Empty || ownerFactory == Guid.Empty || _physicsKindScopes.ContainsKey(invocation) ||
            _scriptKindScopes.ContainsKey(invocation) || _kindScopeIndices.ContainsKey(invocation) ||
            _thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Controller physics initialization changed its actual source scope/thread owner.");
        RequireCurrentKindOwner(); var fields = CurrentKindFields();
        if (fields.Entered is not null) throw new InvalidOperationException("Controller initialization crossed an unreturned source kind scope.");
        fields.Initialized = 1; var before = fields.Kind;
        fields.Kind = FalloutEarlyControllerSource.PhysicsKind; fields.Entered = invocation;
        if (_thread == Environment.CurrentManagedThreadId) { _initialized = 1; _kind = fields.Kind; }
        var scope = new FalloutSourceThreadKindScope(_source.Identity, _process, _factory, ownerFactory, invocation,
            Environment.CurrentManagedThreadId, before, fields.Kind, 1, false, null, null);
        _physicsKindScopes.Add(invocation, scope); return scope;
    }

    internal FalloutSourceThreadKindScope ReturnPhysicsInitialization(FalloutSourceThreadKindScope scope, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent); requireCurrent(); var fields = CurrentKindFields();
        if (_retired || !_physicsKindScopes.TryGetValue(scope.Invocation, out var current) || current != scope ||
            scope.Source != _source.Identity || scope.Process != _process || scope.Factory != _factory ||
            scope.Thread != Environment.CurrentManagedThreadId || fields.Entered != scope.Invocation ||
            fields.Kind != FalloutEarlyControllerSource.PhysicsKind || scope.Returned || scope.Failure is not null)
            throw new InvalidOperationException("Controller return changed its actual entered source kind/prior value.");
        fields.Kind = scope.BeforeKind; fields.Entered = null;
        if (_thread == Environment.CurrentManagedThreadId) { _initialized = fields.Initialized; _kind = fields.Kind; }
        var returned = scope with { Returned = true, RestoredKind = fields.Kind };
        _physicsKindScopes[scope.Invocation] = returned;
        if (_thread is null && _current?.TryGetValue(_process, out var owner) == true && ReferenceEquals(owner, this)) _current.Remove(_process);
        return returned;
    }

    internal FalloutSourceThreadKindScope RetainPhysicsInitializationFailure(FalloutSourceThreadKindScope scope, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!_physicsKindScopes.TryGetValue(scope.Invocation, out var current) || current != scope ||
            scope.Thread != Environment.CurrentManagedThreadId || scope.Returned)
            throw new InvalidOperationException("Failed physics child changed its actual source kind scope.");
        var failed = scope with { Failure = error.Message }; _physicsKindScopes[scope.Invocation] = failed; return failed;
    }

    internal void RetainColdPhysicsInitializationScopes(IReadOnlyList<FalloutSourceThreadKindScope> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (_physicsKindScopes.Count != 0 || _kindFields.Count != 0 || Entry is not null || _retired)
            throw new InvalidOperationException("Cold physics history cannot replace a current source thread.");
        ValidatePhysicsInitializationScopes(saved, _source);
        foreach (var scope in saved)
        {
            if (scope.Process == _process || scope.Factory == _factory || _scriptKindScopes.ContainsKey(scope.Invocation) || _kindScopeIndices.ContainsKey(scope.Invocation))
                throw new InvalidDataException("Cold physics scope reused a prior live process/factory or independent invocation.");
            _physicsKindScopes.Add(scope.Invocation, scope);
        }
        // Historical scopes never replay initializer, entry or restore stores
        // into this fresh current-thread process.
    }

    internal static void ValidatePhysicsInitializationScopes(IReadOnlyList<FalloutSourceThreadKindScope> saved, FalloutPlayerThreadContextSource source)
    {
        ArgumentNullException.ThrowIfNull(saved); source.Validate(); var ids = new HashSet<Guid>();
        foreach (var scope in saved)
            if (scope is null || scope.Source != source.Identity || scope.Process == Guid.Empty || scope.Factory == Guid.Empty ||
                scope.OwnerFactory == Guid.Empty || scope.Invocation == Guid.Empty || !ids.Add(scope.Invocation) || scope.Thread < 1 ||
                scope.EnteredKind != FalloutEarlyControllerSource.PhysicsKind || scope.Initialized != 1 ||
                scope.Returned != (scope.RestoredKind is not null) || scope.Returned && (scope.RestoredKind != scope.BeforeKind || scope.Failure is not null) ||
                scope.Failure is not null && string.IsNullOrWhiteSpace(scope.Failure))
                throw new InvalidDataException("Physics initialization lost its actual source kind/thread/failed prefix.");
    }
}
