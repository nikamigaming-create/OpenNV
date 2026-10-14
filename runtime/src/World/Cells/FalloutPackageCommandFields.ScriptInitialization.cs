using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    private readonly List<FalloutScriptExtraInitializationFieldOperation> _scriptInitializationOperations = [];
    private FalloutReferenceScriptExtraInitializer? _scriptInitializer;
    private FalloutReferenceScriptExtraInitializerSnapshot? _savedScriptInitialization;
    internal FalloutReferenceScriptExtraInitializerSnapshot? SavedScriptInitialization => _savedScriptInitialization;
    internal string? ScriptInitializationWriterBoundary => _operations.LastOrDefault(value =>
        value.InvalidatedLane == FalloutPackageCommandFieldLane.ScriptExtra)?.InvalidationBoundary ??
        _operations.FirstOrDefault(value => !value.Returned && value.Kind is
            "script-Extra-setter" or "script-event-list-setter" or "package-event-marker")?.Failure ??
        _scriptInitializationOperations.FirstOrDefault(value => !value.Returned)?.Failure;
    internal IReadOnlyList<FalloutPackageCommandFieldStore> ScriptInitializationStores(
        FalloutReferenceScriptExtraInitializationInvocation invocation) =>
        _scriptInitializationOperations.Where(value => value.Invocation == invocation).SelectMany(value => value.Stores).ToArray();

    // This binding grants only Script Extra operations. It cannot create a
    // command scope, write reference position or alter constructor admission.
    internal void BindScriptExtraInitializer(FalloutReferenceScriptExtraInitializer initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer); RequireOwner();
        if (_scriptInitializer is not null || initializer.Source.Fields.Identity != Source.Identity ||
            initializer.FieldsFactory != Factory || initializer.Process != Process || initializer.Actor != Actor ||
            initializer.Epoch != Epoch || initializer.Constructor != Constructor)
            throw new InvalidOperationException("Script initializer changed its actual reference field factory.");
        _scriptInitializer = initializer;
    }

    internal FalloutScriptExtraInitializationFieldOperation BeginScriptInitialization(
        FalloutReferenceScriptExtraInitializationInvocation invocation, Guid initializerFactory) =>
        ExecuteScriptInitialization(invocation, initializerFactory, FalloutScriptExtraInitializationFieldOperationKind.Begin,
            scope =>
            {
                scope.Boundary = FalloutReferenceScriptExtraInitializer.EnteredBoundary;
                _availability[FalloutPackageCommandFieldLane.ScriptExtra] = new(FalloutPackageCommandFieldLane.ScriptExtra,
                false, FalloutReferenceScriptExtraInitializer.EnteredBoundary);
            });

    internal FalloutScriptExtraInitializationFieldOperation ReadScriptExtraForInitialization(
        FalloutReferenceScriptExtraInitializationInvocation invocation, Guid initializerFactory) =>
        ExecuteScriptInitialization(invocation, initializerFactory, FalloutScriptExtraInitializationFieldOperationKind.Read, _ => { });

    internal FalloutScriptExtraInitializationFieldOperation AttachScriptForInitialization(
        FalloutReferenceScriptExtraInitializationInvocation invocation, Guid initializerFactory, FalloutFormKey script) =>
        ExecuteScriptInitialization(invocation, initializerFactory, FalloutScriptExtraInitializationFieldOperationKind.Attach, scope =>
        {
            ValidateForm(script); scope.Script = script;
            if (_values.ScriptExtra is null)
            {
                var identity = Guid.NewGuid();
                StoreScriptInitialization(scope, FalloutPackageCommandField.ScriptConstruction, @object: identity);
                StoreScriptInitialization(scope, FalloutPackageCommandField.ScriptForm, form: script);
                StoreScriptInitialization(scope, FalloutPackageCommandField.ScriptConstructorEventList);
                StoreScriptInitialization(scope, FalloutPackageCommandField.ScriptPresence, bits: 1, @object: identity);
            }
            else StoreScriptInitialization(scope, FalloutPackageCommandField.ScriptForm, form: script);
        });

    internal FalloutScriptExtraInitializationFieldOperation PublishScriptEventListForInitialization(
        FalloutReferenceScriptExtraInitializationInvocation invocation, Guid initializerFactory,
        Guid eventList, Guid actualEventFactory, IReadOnlyList<FalloutPackageScriptEventRow?> rows) =>
        ExecuteScriptInitialization(invocation, initializerFactory, FalloutScriptExtraInitializationFieldOperationKind.EventListPublication, scope =>
        {
            ArgumentNullException.ThrowIfNull(rows);
            if (_values.ScriptExtra is null || eventList == Guid.Empty || actualEventFactory == Guid.Empty)
                throw new InvalidDataException("Script initialization list publication lacks an attached Extra and returned list factory.");
            ValidateRows(rows); scope.EventList = eventList; scope.EventFactory = actualEventFactory;
            scope.Rows = Array.AsReadOnly(rows.ToArray());
            StoreScriptInitialization(scope, FalloutPackageCommandField.ScriptEventList, @object: eventList);
        });

    internal FalloutScriptExtraInitializationFieldOperation AdmitScriptInitialization(
        FalloutReferenceScriptExtraInitializationInvocation invocation, Guid initializerFactory) =>
        ExecuteScriptInitialization(invocation, initializerFactory, FalloutScriptExtraInitializationFieldOperationKind.Admit, _ => { });

    internal FalloutScriptExtraInitializationFieldOperation InvalidateScriptInitialization(
        FalloutReferenceScriptExtraInitializationInvocation invocation, Guid initializerFactory, string boundary) =>
        ExecuteScriptInitialization(invocation, initializerFactory, FalloutScriptExtraInitializationFieldOperationKind.UnownedWriter, scope =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(boundary); scope.Boundary = boundary;
            _availability[FalloutPackageCommandFieldLane.ScriptExtra] = new(FalloutPackageCommandFieldLane.ScriptExtra, false, boundary);
        });

    private FalloutScriptExtraInitializationFieldOperation ExecuteScriptInitialization(
        FalloutReferenceScriptExtraInitializationInvocation invocation, Guid initializerFactory,
        FalloutScriptExtraInitializationFieldOperationKind kind, Action<ScriptInitializationFieldScope> action)
    {
        RequireOwner(); ArgumentNullException.ThrowIfNull(invocation);
        if (_scriptInitializer is null || initializerFactory != _scriptInitializer.Factory)
            throw new InvalidOperationException("Script Extra fields have no bound actual initializer capability.");
        var scope = new ScriptInitializationFieldScope(invocation, initializerFactory, kind, Next(), _fault,
            CloneScriptExtra(_values.ScriptExtra)); _busy = true;
        try
        {
            GuardScriptInitialization(scope); action(scope); GuardScriptInitialization(scope);
            // Availability is published after the final actual caller guard.
            // It is not an original reference-field or constructor flag store.
            if (kind == FalloutScriptExtraInitializationFieldOperationKind.Admit) Admit(FalloutPackageCommandFieldLane.ScriptExtra);
            return CompleteScriptInitialization(scope, true, null);
        }
        catch (Exception error)
        {
            var failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            _failure ??= failure; CompleteScriptInitialization(scope, false, failure); throw;
        }
        finally { _busy = false; }
    }

    private void GuardScriptInitialization(ScriptInitializationFieldScope scope)
    {
        var invocation = scope.Invocation;
        if (_retired || _scriptInitializer is not { } initializer || initializer.Factory != scope.InitializerFactory ||
            invocation.Actor != Actor || invocation.Process != Process || invocation.Epoch != Epoch || invocation.Constructor != Constructor ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || _thread is { } thread && thread != invocation.ThreadId || _fault != scope.Fault)
            throw new InvalidOperationException("Script Extra fields lost their narrow initializer, Actor, process, constructor or source thread.");
        initializer.RequireCurrent(invocation);
        if (_retired || _fault != scope.Fault || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Script Extra field guard swallowed reentry or changed its owner.");
        _thread ??= invocation.ThreadId;
    }

    private void StoreScriptInitialization(ScriptInitializationFieldScope scope, FalloutPackageCommandField field,
        uint? bits = null, FalloutFormKey? form = null, Guid? @object = null)
    {
        if (field is not (FalloutPackageCommandField.ScriptConstruction or FalloutPackageCommandField.ScriptPresence or
            FalloutPackageCommandField.ScriptForm or FalloutPackageCommandField.ScriptConstructorEventList or FalloutPackageCommandField.ScriptEventList))
            throw new InvalidOperationException("Script initializer attempted an unrelated command-field store.");
        GuardScriptInitialization(scope);
        var store = new FalloutPackageCommandFieldStore(field, Next(), bits, form, @object);
        _values = ApplyStore(_values, store, scope.Rows, scope.EventFactory); scope.Stores.Add(store);
    }

    private FalloutScriptExtraInitializationFieldOperation CompleteScriptInitialization(ScriptInitializationFieldScope scope,
        bool returned, string? failure)
    {
        var receipt = new FalloutScriptExtraInitializationFieldOperation(scope.Invocation, _scriptInitializer!.Source.Identity,
            scope.InitializerFactory, Factory, scope.Kind, scope.Entered, Next(), scope.Before, CloneScriptExtra(_values.ScriptExtra),
            Array.AsReadOnly(scope.Stores.ToArray()), returned, failure, scope.Script, scope.EventList, scope.EventFactory,
            scope.Rows, scope.Boundary);
        _scriptInitializationOperations.Add(receipt); return receipt;
    }

    private sealed class ScriptInitializationFieldScope(FalloutReferenceScriptExtraInitializationInvocation invocation,
        Guid initializerFactory, FalloutScriptExtraInitializationFieldOperationKind kind, long entered, long fault,
        FalloutPackageScriptExtra? before)
    {
        internal FalloutReferenceScriptExtraInitializationInvocation Invocation { get; } = invocation;
        internal Guid InitializerFactory { get; } = initializerFactory;
        internal FalloutScriptExtraInitializationFieldOperationKind Kind { get; } = kind;
        internal long Entered { get; } = entered;
        internal long Fault { get; } = fault;
        internal FalloutPackageScriptExtra? Before { get; } = before;
        internal List<FalloutPackageCommandFieldStore> Stores { get; } = [];
        internal FalloutFormKey? Script { get; set; }
        internal Guid? EventList { get; set; }
        internal Guid? EventFactory { get; set; }
        internal IReadOnlyList<FalloutPackageScriptEventRow?>? Rows { get; set; }
        internal string? Boundary { get; set; }
    }

    private static FalloutPackageScriptExtra? CloneScriptExtra(FalloutPackageScriptExtra? extra) =>
        extra is null ? null : extra with { Rows = Array.AsReadOnly(extra.Rows.ToArray()) };
    private static FalloutScriptExtraInitializationFieldOperation CloneScriptInitializationOperation(
        FalloutScriptExtraInitializationFieldOperation value) => value with
        {
            Before = CloneScriptExtra(value.Before),
            After = CloneScriptExtra(value.After),
            Stores = Array.AsReadOnly(value.Stores.ToArray()),
            Rows = value.Rows is { } rows ? Array.AsReadOnly(rows.ToArray()) : null
        };
}
