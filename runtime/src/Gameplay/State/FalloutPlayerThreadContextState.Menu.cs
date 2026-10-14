using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSourceThreadKindScope(string Source, Guid Process, Guid Factory, Guid OwnerFactory,
    Guid Invocation, int Thread, uint BeforeKind, uint EnteredKind, byte Initialized,
    bool Returned, uint? RestoredKind, string? Failure);

internal sealed partial class FalloutPlayerThreadContextState
{
    private sealed class KindFields(uint kind, byte initialized)
    {
        internal uint Kind = kind;
        internal byte Initialized = initialized;
        internal Guid? Entered;
    }
    private readonly Dictionary<int, KindFields> _kindFields = [];
    private readonly List<FalloutSourceThreadKindScope> _kindScopes = [];
    private readonly Dictionary<Guid, int> _kindScopeIndices = [];

    private KindFields CurrentKindFields()
    {
        var thread = Environment.CurrentManagedThreadId;
        if (!_kindFields.TryGetValue(thread, out var fields))
            _kindFields.Add(thread, fields = new(_source.InitialKind, _source.InitialInitialized));
        return fields;
    }

    internal FalloutSourceThreadKindScope EnterMenuAllocation(Guid invocation, Guid ownerFactory, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent); requireCurrent();
        if (_retired || invocation == Guid.Empty || ownerFactory == Guid.Empty ||
            _thread is { } thread && thread != Environment.CurrentManagedThreadId ||
            _kindScopeIndices.ContainsKey(invocation) || _scriptKindScopes.ContainsKey(invocation) || HasPhysicsInitializationScope(invocation))
            throw new InvalidOperationException("Menu allocation changed its actual source thread, owner or invocation.");
        RequireCurrentKindOwner();
        var fields = CurrentKindFields();
        if (fields.Entered is not null)
            throw new InvalidOperationException("Menu allocation reentered an unfinished current-thread kind scope.");
        // The same on-demand initializer precedes the saved-kind read. Its
        // independent loaded kind is 59 on each source thread, not zero.
        fields.Initialized = 1;
        if (_thread == Environment.CurrentManagedThreadId) _initialized = fields.Initialized;
        var before = fields.Kind; fields.Kind = 13; fields.Entered = invocation;
        if (_thread == Environment.CurrentManagedThreadId) _kind = fields.Kind;
        var scope = new FalloutSourceThreadKindScope(_source.Identity, _process, _factory, ownerFactory, invocation,
            Environment.CurrentManagedThreadId, before, 13, 1, false, null, null);
        _kindScopeIndices.Add(invocation, _kindScopes.Count); _kindScopes.Add(scope); return scope;
    }

    internal FalloutSourceThreadKindScope ReturnMenuAllocation(FalloutSourceThreadKindScope scope, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent); requireCurrent();
        var fields = CurrentKindFields();
        if (_retired || !_kindScopeIndices.TryGetValue(scope.Invocation, out var index) || _kindScopes[index] != scope || scope.Source != _source.Identity ||
            scope.Process != _process || scope.Factory != _factory || scope.Thread != Environment.CurrentManagedThreadId ||
            fields.Entered != scope.Invocation || fields.Kind != 13 || scope.Returned || scope.Failure is not null)
            throw new InvalidOperationException("Menu allocation lost its entered source kind or prior value.");
        fields.Kind = scope.BeforeKind; fields.Entered = null;
        if (_thread == Environment.CurrentManagedThreadId) _kind = fields.Kind;
        var returned = scope with { Returned = true, RestoredKind = fields.Kind };
        _kindScopes[index] = returned;
        // A completed menu constructor may precede Player thread publication.
        // Keep its restored source TLS fields, but release the temporary C#
        // current-owner registration on that constructor's thread.
        if (_thread is null && _current?.TryGetValue(_process, out var current) == true && ReferenceEquals(current, this))
            _current.Remove(_process);
        return returned;
    }

    internal void RetainMenuAllocationFailure(FalloutSourceThreadKindScope scope, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!_kindScopeIndices.TryGetValue(scope.Invocation, out var index) || _kindScopes[index] != scope ||
            scope.Thread != Environment.CurrentManagedThreadId || scope.Returned)
            throw new InvalidOperationException("Failed menu allocation changed its entered source thread scope.");
        _kindScopes[index] = scope with { Failure = error.Message };
        // No prior-kind restoration is invented for a child that did not return.
    }

    internal IReadOnlyList<FalloutSourceThreadKindScope> CaptureKindScopes() => _kindScopes.ToArray();

    private void RequireCurrentKindOwner()
    {
        _current ??= [];
        if (_current.TryGetValue(_process, out var current) && !ReferenceEquals(current, this))
            throw new InvalidOperationException("Source kind allocation selected another live current-thread process owner.");
        _current[_process] = this;
    }

    internal void RetainColdKindScopes(IReadOnlyList<FalloutSourceThreadKindScope>? saved)
    {
        if (saved is null) return;
        if (_kindScopes.Count != 0 || _kindFields.Count != 0 || Entry is not null)
            throw new InvalidOperationException("Cold kind history cannot replace a published thread owner.");
        ValidateKindScopes(saved, _source);
        foreach (var scope in saved)
        {
            if (scope.Process == _process || scope.Factory == _factory)
                throw new InvalidDataException("Cold menu allocation reused its previous actual process or thread factory.");
            _kindScopeIndices.Add(scope.Invocation, _kindScopes.Count); _kindScopes.Add(scope);
        }
        // Historical source threads are never entered, initialized or restored
        // in this fresh process. New genuine calls acquire their own TLS cells.
    }

    internal static void ValidateKindScopes(IReadOnlyList<FalloutSourceThreadKindScope>? saved,
        FalloutPlayerThreadContextSource source)
    {
        source.Validate(); if (saved is null) return;
        var invocations = new HashSet<Guid>();
        foreach (var scope in saved)
            if (scope.Source != source.Identity || scope.Process == Guid.Empty || scope.Factory == Guid.Empty ||
                scope.OwnerFactory == Guid.Empty || scope.Invocation == Guid.Empty || !invocations.Add(scope.Invocation) ||
                scope.Thread < 1 || scope.EnteredKind != 13 || scope.Initialized != 1 ||
                scope.Returned != (scope.RestoredKind is not null) ||
                scope.Returned && (scope.RestoredKind != scope.BeforeKind || scope.Failure is not null) ||
                scope.Failure is not null && string.IsNullOrWhiteSpace(scope.Failure))
                throw new InvalidDataException("Menu allocation lost its actual thread, prior kind, return or failed prefix.");
    }
}
