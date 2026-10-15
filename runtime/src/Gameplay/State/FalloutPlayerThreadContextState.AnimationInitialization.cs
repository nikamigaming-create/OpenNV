using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutLoadedAnimationScopeKind { PlayerLoad, Bind }
internal sealed record FalloutSourceAnimationKindScope(FalloutSourceThreadKindScope Scope,
    FalloutLoadedAnimationScopeKind Kind, Guid? Parent);

internal sealed partial class FalloutPlayerThreadContextState
{
    private readonly Dictionary<Guid, FalloutSourceAnimationKindScope> _animationKinds = [];
    internal FalloutSourceAnimationKindScope EnterAnimationInitialization(Guid invocation, Guid ownerFactory,
        FalloutLoadedAnimationScopeKind kind, Action requireActual)
    {
        requireActual();
        if (_retired || invocation == Guid.Empty || ownerFactory == Guid.Empty || !Enum.IsDefined(kind) ||
            _animationKinds.ContainsKey(invocation) || _kindScopeIndices.ContainsKey(invocation) ||
            _scriptKindScopes.ContainsKey(invocation) || HasPhysicsInitializationScope(invocation) ||
            _thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Animation initialization lost its actual current thread or repeated its scope.");
        RequireCurrentKindOwner(); var fields = CurrentKindFields(); var parent = fields.Entered;
        if (parent is { } entered && (kind != FalloutLoadedAnimationScopeKind.Bind ||
            !_animationKinds.TryGetValue(entered, out var outer) || outer.Kind != FalloutLoadedAnimationScopeKind.PlayerLoad ||
            outer.Scope.Returned || outer.Scope.Failure is not null || fields.Kind != FalloutLoadedAnimationSource.PlayerLoadThreadKind))
            throw new InvalidOperationException("Animation binding crossed an unrelated or failed source kind scope.");
        fields.Initialized = 1; var before = fields.Kind;
        fields.Kind = kind == FalloutLoadedAnimationScopeKind.PlayerLoad ?
            FalloutLoadedAnimationSource.PlayerLoadThreadKind : FalloutLoadedAnimationSource.BindThreadKind;
        fields.Entered = invocation;
        if (_thread == Environment.CurrentManagedThreadId) { _initialized = 1; _kind = fields.Kind; }
        var result = new FalloutSourceAnimationKindScope(new(_source.Identity, _process, _factory, ownerFactory,
            invocation, Environment.CurrentManagedThreadId, before, fields.Kind, 1, false, null, null), kind, parent);
        _animationKinds.Add(invocation, result); return result;
    }
    internal FalloutSourceAnimationKindScope ReturnAnimationInitialization(FalloutSourceAnimationKindScope input, Action requireActual)
    {
        requireActual(); var scope = input.Scope; var fields = CurrentKindFields();
        if (!_animationKinds.TryGetValue(scope.Invocation, out var current) || current != input || _retired ||
            scope.Process != _process || scope.Factory != _factory || scope.Source != _source.Identity ||
            scope.Thread != Environment.CurrentManagedThreadId || fields.Entered != scope.Invocation ||
            fields.Kind != scope.EnteredKind || scope.Returned || scope.Failure is not null)
            throw new InvalidOperationException("Animation return lost its actual entered kind or nested child.");
        fields.Kind = scope.BeforeKind; fields.Entered = input.Parent;
        if (_thread == Environment.CurrentManagedThreadId) { _initialized = fields.Initialized; _kind = fields.Kind; }
        var result = input with { Scope = scope with { Returned = true, RestoredKind = fields.Kind } };
        _animationKinds[scope.Invocation] = result;
        if (input.Parent is null && _thread is null && _current?.TryGetValue(_process, out var owner) == true && ReferenceEquals(owner, this))
            _current.Remove(_process);
        return result;
    }
    internal FalloutSourceAnimationKindScope RetainAnimationInitializationFailure(FalloutSourceAnimationKindScope input, Exception error)
    {
        var scope = input.Scope;
        if (!_animationKinds.TryGetValue(scope.Invocation, out var current) || current != input ||
            scope.Returned || scope.Thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Failed animation initialization replaced its actual entered scope.");
        var result = input with { Scope = scope with { Failure = error.Message } };
        _animationKinds[scope.Invocation] = result; return result;
    }
    internal IReadOnlyList<FalloutSourceAnimationKindScope> CaptureAnimationInitializationScopes() => _animationKinds.Values.ToArray();
}
