using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerThreadContextState
{
    private readonly Dictionary<Guid, FalloutSourceThreadKindScope> _scriptKindScopes = [];
    internal FalloutPlayerThreadContextSource Source => _source;
    internal Guid Process => _process;
    internal Guid Factory => _factory;

    // Shares the real kind cells with Player and menu scopes. Script receipts
    // belong to their initializer, independently of the menu's kind13 history.
    internal FalloutSourceThreadKindScope EnterScriptInitialization(Guid invocation, Guid ownerFactory, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent); requireCurrent();
        if (_retired || invocation == Guid.Empty || ownerFactory == Guid.Empty || _scriptKindScopes.ContainsKey(invocation) ||
            _kindScopeIndices.ContainsKey(invocation) ||
            _thread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Reference Script initialization changed its actual current-thread owner.");
        RequireCurrentKindOwner(); var fields = CurrentKindFields();
        if (fields.Entered is not null)
            throw new InvalidOperationException("Reference Script initialization crossed an unreturned source thread scope.");
        fields.Initialized = 1;
        var before = fields.Kind;
        fields.Kind = FalloutReferenceScriptExtraInitializerSource.ScriptThreadKind; fields.Entered = invocation;
        if (_thread == Environment.CurrentManagedThreadId) { _initialized = 1; _kind = fields.Kind; }
        var scope = new FalloutSourceThreadKindScope(_source.Identity, _process, _factory, ownerFactory, invocation,
            Environment.CurrentManagedThreadId, before, fields.Kind, 1, false, null, null);
        _scriptKindScopes.Add(invocation, scope); return scope;
    }

    internal FalloutSourceThreadKindScope ReturnScriptInitialization(FalloutSourceThreadKindScope scope, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent); requireCurrent();
        var fields = CurrentKindFields();
        if (_retired || !_scriptKindScopes.TryGetValue(scope.Invocation, out var current) || current != scope ||
            scope.Source != _source.Identity || scope.Process != _process || scope.Factory != _factory ||
            scope.Thread != Environment.CurrentManagedThreadId || fields.Entered != scope.Invocation ||
            fields.Kind != FalloutReferenceScriptExtraInitializerSource.ScriptThreadKind || scope.Returned || scope.Failure is not null)
            throw new InvalidOperationException("Reference Script return changed its original entered thread scope.");
        fields.Kind = scope.BeforeKind; fields.Entered = null;
        if (_thread == Environment.CurrentManagedThreadId) { _initialized = fields.Initialized; _kind = fields.Kind; }
        var returned = scope with { Returned = true, RestoredKind = fields.Kind };
        _scriptKindScopes[scope.Invocation] = returned;
        if (_thread is null && _current?.TryGetValue(_process, out var owner) == true && ReferenceEquals(owner, this))
            _current.Remove(_process);
        return returned;
    }

    internal FalloutSourceThreadKindScope RetainScriptInitializationFailure(FalloutSourceThreadKindScope scope, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!_scriptKindScopes.TryGetValue(scope.Invocation, out var current) || current != scope ||
            scope.Thread != Environment.CurrentManagedThreadId || scope.Returned)
            throw new InvalidOperationException("Failed reference Script child changed its entered thread scope.");
        var failed = scope with { Failure = error.Message };
        _scriptKindScopes[scope.Invocation] = failed; return failed;
    }

    internal void RetainColdScriptInitializationScopes(IReadOnlyList<FalloutSourceThreadKindScope> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (_scriptKindScopes.Count != 0 || _kindFields.Count != 0 || Entry is not null || _retired)
            throw new InvalidOperationException("Cold Script history cannot replace an entered current-thread owner.");
        ValidateScriptInitializationScopes(saved, _source);
        foreach (var scope in saved)
        {
            if (scope.Process == _process || scope.Factory == _factory || _kindScopeIndices.ContainsKey(scope.Invocation))
                throw new InvalidDataException("Cold Script scope reused a process, factory or menu invocation.");
            _scriptKindScopes.Add(scope.Invocation, scope);
        }
        // These are historical receipts only. No fresh thread cell, entered
        // scope, initializer byte or prior-kind restore is replayed here.
    }

    internal static void ValidateScriptInitializationScopes(IReadOnlyList<FalloutSourceThreadKindScope> saved,
        FalloutPlayerThreadContextSource source)
    {
        ArgumentNullException.ThrowIfNull(saved); source.Validate();
        var ids = new HashSet<Guid>();
        foreach (var scope in saved)
            if (scope is null || scope.Source != source.Identity || scope.Process == Guid.Empty || scope.Factory == Guid.Empty ||
                scope.OwnerFactory == Guid.Empty || scope.Invocation == Guid.Empty || !ids.Add(scope.Invocation) || scope.Thread < 1 ||
                scope.EnteredKind != FalloutReferenceScriptExtraInitializerSource.ScriptThreadKind || scope.Initialized != 1 ||
                scope.Returned != (scope.RestoredKind is not null) || scope.Returned &&
                (scope.RestoredKind != scope.BeforeKind || scope.Failure is not null) ||
                scope.Failure is not null && string.IsNullOrWhiteSpace(scope.Failure))
                throw new InvalidDataException("Script allocation lost its actual source kind, thread, owner or failed prefix.");
    }
}
