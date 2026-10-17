using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerModelLoadThreadKind { WeaponSetter, WornEntry }
internal sealed record FalloutPlayerModelLoadKindScope(FalloutSourceThreadKindScope Scope, Guid Parent,
    FalloutReferenceModelDataInvocation Load, FalloutPlayerModelLoadThreadKind Kind);
internal sealed record FalloutPlayerCollisionModeRead(string Source, Guid Process, Guid Factory, int Thread,
    FalloutReferenceModelDataInvocation Load, uint Mode);
internal sealed partial class FalloutPlayerThreadContextState
{
    internal FalloutPlayerCollisionModeRead ReadModelLoadCollisionMode(FalloutReferenceModelDataInvocation load,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        requireActual(load); RequireCurrentKindOwner();
        var fields = CurrentKindFields();
        if (_retired || load.ThreadId != Environment.CurrentManagedThreadId || fields.Entered is not { } parent ||
            !_animationKinds.TryGetValue(parent, out var outer) || outer.Kind != FalloutLoadedAnimationScopeKind.PlayerLoad ||
            outer.Scope.Returned || outer.Scope.Failure is not null || fields.Kind != FalloutLoadedAnimationSource.PlayerLoadThreadKind)
            throw new InvalidOperationException("Collision mode read left its actual entered model-load thread.");
        fields.Initialized = 1;
        var result = new FalloutPlayerCollisionModeRead(_source.Identity, _process, _factory, load.ThreadId, load, fields.CollisionMode);
        requireActual(load); return result;
    }
    private readonly Dictionary<Guid, FalloutPlayerModelLoadKindScope> _modelLoadKinds = [];
    internal FalloutPlayerModelLoadKindScope EnterPlayerLoadWeaponSetter(FalloutReferenceModelDataInvocation load,
        Guid invocation, Guid ownerFactory, Action<FalloutReferenceModelDataInvocation> requireActual)
        => EnterPlayerModelLoadKind(load, invocation, ownerFactory, FalloutPlayerModelLoadThreadKind.WeaponSetter, requireActual);
    internal FalloutPlayerModelLoadKindScope EnterPlayerLoadWornEntry(FalloutReferenceModelDataInvocation load,
        Guid invocation, Guid ownerFactory, Action<FalloutReferenceModelDataInvocation> requireActual)
        => EnterPlayerModelLoadKind(load, invocation, ownerFactory, FalloutPlayerModelLoadThreadKind.WornEntry, requireActual);
    private FalloutPlayerModelLoadKindScope EnterPlayerModelLoadKind(FalloutReferenceModelDataInvocation load,
        Guid invocation, Guid ownerFactory, FalloutPlayerModelLoadThreadKind kind, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        requireActual(load); RequireCurrentKindOwner(); var fields = CurrentKindFields();
        if (_retired || load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || load.ThreadId != Environment.CurrentManagedThreadId ||
            invocation == Guid.Empty || ownerFactory == Guid.Empty || _modelLoadKinds.ContainsKey(invocation) ||
            _animationKinds.ContainsKey(invocation) || _kindScopeIndices.ContainsKey(invocation) ||
            _scriptKindScopes.ContainsKey(invocation) || HasPhysicsInitializationScope(invocation) ||
            fields.Entered is not { } parent || !_animationKinds.TryGetValue(parent, out var outer) ||
            outer.Kind != FalloutLoadedAnimationScopeKind.PlayerLoad || outer.Scope.Returned || outer.Scope.Failure is not null ||
            fields.Kind != FalloutLoadedAnimationSource.PlayerLoadThreadKind)
            throw new InvalidOperationException("Player model-load child left its actual entered outer model load/thread.");
        var before = fields.Kind; fields.Initialized = 1; fields.Kind = kind switch
        {
            FalloutPlayerModelLoadThreadKind.WeaponSetter => FalloutPlayerModelLoadContinuationSource.WeaponThreadKind,
            FalloutPlayerModelLoadThreadKind.WornEntry => FalloutPlayerModelLoadContinuationSource.WornEntryThreadKind,
            _ => throw new InvalidOperationException("Unselected Player loader thread kind."),
        }; fields.Entered = invocation;
        if (_thread == Environment.CurrentManagedThreadId) { _initialized = 1; _kind = fields.Kind; }
        var result = new FalloutPlayerModelLoadKindScope(new(_source.Identity, _process, _factory, ownerFactory,
            invocation, Environment.CurrentManagedThreadId, before, fields.Kind, 1, false, null, null), parent, load, kind);
        _modelLoadKinds.Add(invocation, result); return result;
    }
    internal FalloutPlayerModelLoadKindScope ReturnPlayerModelLoadKind(FalloutPlayerModelLoadKindScope input,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        requireActual(input.Load); var fields = CurrentKindFields(); var scope = input.Scope;
        if (_retired || !_modelLoadKinds.TryGetValue(scope.Invocation, out var current) || current != input ||
            fields.Entered != scope.Invocation || fields.Kind != scope.EnteredKind || scope.Returned || scope.Failure is not null ||
            scope.Thread != Environment.CurrentManagedThreadId || scope.Process != _process || scope.Factory != _factory)
            throw new InvalidOperationException("Player model-load child return replaced a real scope or swallowed a nested child.");
        fields.Kind = scope.BeforeKind; fields.Entered = input.Parent;
        if (_thread == Environment.CurrentManagedThreadId) { _initialized = fields.Initialized; _kind = fields.Kind; }
        var result = input with { Scope = scope with { Returned = true, RestoredKind = fields.Kind } };
        _modelLoadKinds[scope.Invocation] = result; return result;
    }
    internal FalloutPlayerModelLoadKindScope RetainPlayerModelLoadKindFailure(FalloutPlayerModelLoadKindScope input, Exception error)
    {
        if (!_modelLoadKinds.TryGetValue(input.Scope.Invocation, out var current) || current != input ||
            input.Scope.Returned || input.Scope.Thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Failed model-load child lost its real entered source scope.");
        var result = input with { Scope = input.Scope with { Failure = error.Message } };
        _modelLoadKinds[input.Scope.Invocation] = result; return result;
    }
    internal IReadOnlyList<FalloutPlayerModelLoadKindScope> CapturePlayerModelLoadScopes() => _modelLoadKinds.Values.ToArray();
}
