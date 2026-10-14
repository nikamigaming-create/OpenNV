using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Owns only the inherited Script branch and its reference flag/template cells.
// A returned call is never a receipt for the enclosing reference initializer.
internal sealed partial class FalloutReferenceScriptExtraInitializer
{
    internal const string Schema = "opennv-reference-script-extra-initializer/v1";
    internal const string EnteredBoundary = "source-reference-Script-Extra-initializer-unreturned";
    internal const string EventListBoundary = "source-Script-event-list-and-local-variable-factory-unbound";
    private readonly FalloutPluginStack _records;
    private readonly FalloutActorPackageCommandFields _fields;
    private readonly FalloutPlayerThreadContextState _threadContext;
    private readonly Action<FalloutReferenceScriptExtraInitializationInvocation> _requireActualInitialization;
    private readonly List<FalloutReferenceScriptExtraInitializerPublication> _publications = [];
    private readonly List<FalloutReferenceScriptExtraInitializationCall> _calls = [];
    private readonly HashSet<Guid> _invocations = [];
    private FalloutReferenceScriptExtraInitializationInvocation? _current;
    private uint _referenceFlags = FalloutReferenceScriptExtraInitializerSource.ConstructorFlags;
    private FalloutFormKey? _template;
    private long _sequence, _fault, _enteredFault;
    private int? _thread;
    private bool _busy;
    private string? _writerBoundary;
    private string? _failure;
    private bool _returnedInitialization;
    internal FalloutReferenceScriptExtraInitializerSource Source { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid FieldsFactory => _fields.Factory;
    internal Guid Process => _fields.Process;
    internal Guid Constructor => _fields.Constructor;
    internal long Epoch => _fields.Epoch;
    internal FalloutCombatActorIdentity Actor => _fields.Actor;
    internal FalloutReferenceScriptExtraInitializationCall? LastCall { get; private set; }
    internal string? SaveBlocker => _busy ? EnteredBoundary : _writerBoundary ?? _failure;

    internal FalloutReferenceScriptExtraInitializer(FalloutReferenceScriptExtraInitializerSource source,
        FalloutPluginStack records, FalloutActorPackageCommandFields fields, FalloutPlayerThreadContextState threadContext,
        Action<FalloutReferenceScriptExtraInitializationInvocation> requireActualInitialization,
        FalloutReferenceScriptExtraInitializerSnapshot? saved = null)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(threadContext); ArgumentNullException.ThrowIfNull(requireActualInitialization);
        source.Validate(); fields.Actor.Validate(); threadContext.Source.Validate();
        if (source.Fields.Identity != fields.Source.Identity || !fields.Actor.EnginePlayer ||
            fields.Actor.ReferenceSha256 != source.Fields.Controls.Main.EngineSha256 ||
            threadContext.Source.Main.Identity != source.Fields.Controls.Main.Identity)
            throw new InvalidDataException("Script initializer lacks its actual Player, fields, source and current-thread process.");
        Source = source; _records = records; _fields = fields; _threadContext = threadContext;
        _requireActualInitialization = requireActualInitialization;
        RequireBaseSource();
        saved ??= fields.SavedScriptInitialization;
        if (saved is null)
        {
            if (fields.SavedScriptInitialization is not null)
                throw new InvalidDataException("Cold Script fields omitted their original initializer.");
            _publications.Add(new(Process, Factory, FieldsFactory, Constructor, Epoch, threadContext.Process, threadContext.Factory,
                PublishedSequence: _sequence));
        }
        else
        {
            Validate(saved, source, fields.Stack, fields.Actor);
            if (saved.Process == Process || saved.Factory == Factory || saved.Epoch != Epoch ||
                fields.SavedScriptInitialization is not { } baseline || !EquivalentSnapshot(saved, baseline))
                throw new InvalidDataException("Cold Script initializer reused a process or replaced its fields-owned source history.");
            _referenceFlags = saved.ReferenceFlags; _template = saved.Template; _writerBoundary = saved.WriterBoundary;
            _publications.AddRange(saved.Publications); _calls.AddRange(saved.Calls.Select(CloneCall));
            _referenceFlagWrites.AddRange(saved.ReferenceFlagWrites ?? []);
            _failure = _calls.FirstOrDefault(value => !value.Returned)?.Failure;
            _returnedInitialization = _calls.Any(value => value.Returned && value.InvalidatedWriter is null);
            foreach (var call in _calls) _invocations.Add(call.Invocation.Initialization);
            foreach (var write in _referenceFlagWrites) _invocations.Add(write.Invocation.Call);
            _sequence = Math.Max(_calls.LastOrDefault()?.Changed ?? 0, _referenceFlagWrites.LastOrDefault()?.Changed ?? 0);
            _failure ??= _referenceFlagWrites.FirstOrDefault(value => !value.Returned)?.Failure;
            LastCall = _calls.LastOrDefault();
            _publications.Add(new(Process, Factory, FieldsFactory, Constructor, Epoch, threadContext.Process, threadContext.Factory,
                saved.Process, saved.Factory, _sequence));
            threadContext.RetainColdScriptInitializationScopes(_calls.Where(value => value.ThreadScope is not null)
                .Select(value => value.ThreadScope!).ToArray());
        }
        fields.BindScriptExtraInitializer(this);
    }

    internal FalloutReferenceScriptExtraInitializationCall Initialize(FalloutReferenceScriptExtraInitializationInvocation invocation)
    {
        var scope = Enter(invocation); _busy = true;
        try
        {
            RequireCurrent(invocation);
            Add(_fields.BeginScriptInitialization(invocation, Factory));
            if (_writerBoundary is not null) throw new NotSupportedException(_writerBoundary);
            if (_fields.ScriptInitializationWriterBoundary is { } fieldWriter) throw new NotSupportedException(fieldWriter);
            if (_records.TryGetWinner(Actor.Reference, out _))
                throw new NotSupportedException("source-engine-reference-record-initialization-writer-unbound");
            if (_failure is not null)
                throw new NotSupportedException("source-reference-Script-Extra-prior-failed-initializer-prefix");
            Step(FalloutReferenceScriptExtraInitializationStep.InitializedFlagRead, word: _referenceFlags);
            if ((_referenceFlags & FalloutReferenceScriptExtraInitializerSource.InitializedFlag) != 0)
            {
                // Only a prior returned independent branch can supply this
                // skip. Flag8 from an earlier failed prefix cannot do so.
                if (!_returnedInitialization)
                    throw new NotSupportedException("source-reference-initialized-flag-without-returned-Script-owner");
            }
            else
            {
                RequireCurrent(invocation);
                scope.Step = FalloutReferenceScriptExtraInitializationStep.InitializedFlagStore;
                _referenceFlags |= FalloutReferenceScriptExtraInitializerSource.InitializedFlag;
                scope.Reads.Add(new(FalloutReferenceScriptExtraInitializationStep.InitializedFlagStore, _referenceFlags));
                Step(FalloutReferenceScriptExtraInitializationStep.FlagPredicate, word: _referenceFlags);
                if ((_referenceFlags & FalloutReferenceScriptExtraInitializerSource.ScriptSuppressionFlag) == 0)
                {
                    scope.Step = FalloutReferenceScriptExtraInitializationStep.ThreadScope;
                    scope.ThreadScope = _threadContext.EnterScriptInitialization(invocation.Initialization, Factory,
                        () => RequireCurrent(invocation));
                    Step(FalloutReferenceScriptExtraInitializationStep.ThreadScope, word: scope.ThreadScope.EnteredKind);
                    scope.Step = FalloutReferenceScriptExtraInitializationStep.ExistingExtra;
                    var existing = _fields.ReadScriptExtraForInitialization(invocation, Factory); Add(existing);
                    scope.Before = existing.Before;
                    Step(FalloutReferenceScriptExtraInitializationStep.ExistingExtra, form: existing.Before?.Script,
                        present: existing.Before is not null);
                    if (existing.Before?.Script is null)
                    {
                        Step(FalloutReferenceScriptExtraInitializationStep.Template, form: _template, present: _template is not null);
                        var selected = _records.GetEffective(_template ?? Actor.Base);
                        scope.Selected = ReadForm(selected);
                        Step(FalloutReferenceScriptExtraInitializationStep.SelectedForm, form: selected.FormKey);
                        Step(FalloutReferenceScriptExtraInitializationStep.Script, form: scope.Selected.Script,
                            present: scope.Selected.Script is not null);
                        if (scope.Selected.Script is { } script)
                        {
                            scope.Step = FalloutReferenceScriptExtraInitializationStep.ScriptAttachment;
                            Add(_fields.AttachScriptForInitialization(invocation, Factory, script));
                            Step(FalloutReferenceScriptExtraInitializationStep.ScriptAttachment, form: script);
                            scope.Step = FalloutReferenceScriptExtraInitializationStep.EventListFactory;
                            // Attachment precedes the independent original
                            // Script list and local-variable constructor.
                            throw new NotSupportedException(EventListBoundary);
                        }
                    }
                    scope.Step = FalloutReferenceScriptExtraInitializationStep.ThreadRestore;
                    scope.ThreadScope = _threadContext.ReturnScriptInitialization(scope.ThreadScope,
                        () => RequireCurrent(invocation));
                    Step(FalloutReferenceScriptExtraInitializationStep.ThreadRestore, word: scope.ThreadScope.RestoredKind);
                }
            }
            RequireCurrent(invocation); scope.Step = FalloutReferenceScriptExtraInitializationStep.Returned;
            Add(_fields.AdmitScriptInitialization(invocation, Factory));
            return Complete(scope, true, null);
        }
        catch (Exception error)
        {
            if (scope.ThreadScope is { Returned: false, Failure: null } entered)
                scope.ThreadScope = _threadContext.RetainScriptInitializationFailure(entered, error);
            scope.Stores.Clear(); scope.Stores.AddRange(_fields.ScriptInitializationStores(invocation));
            Complete(scope, false, string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message); throw;
        }
        finally { _current = null; _busy = false; }

        void Step(FalloutReferenceScriptExtraInitializationStep step, uint? word = null, FalloutFormKey? form = null, bool? present = null)
        { RequireCurrent(invocation); scope.Step = step; scope.Reads.Add(new(step, word, form, present)); }
        void Add(FalloutScriptExtraInitializationFieldOperation operation) => scope.Stores.AddRange(operation.Stores);
    }

    internal FalloutReferenceScriptExtraInitializationCall ObserveUnownedWriter(
        FalloutReferenceScriptExtraInitializationInvocation invocation, FalloutReferenceScriptExtraWriter writer, string boundary)
    {
        if (!Enum.IsDefined(writer)) throw new InvalidDataException("Script Extra observation has no selected writer lane.");
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        var scope = Enter(invocation); _busy = true; scope.Step = FalloutReferenceScriptExtraInitializationStep.UnownedWriter;
        scope.Writer = writer; scope.WriterBoundary = boundary;
        _writerBoundary = boundary;
        try
        {
            RequireCurrent(invocation);
            _fields.InvalidateAnimationNodeMembershipFromInitializer(invocation, Factory, writer, boundary);
            var invalidated = _fields.InvalidateScriptInitialization(invocation, Factory, boundary);
            scope.Stores.AddRange(invalidated.Stores); return Complete(scope, true, null);
        }
        catch (Exception error) { Complete(scope, false, error.Message); throw; }
        finally { _current = null; _busy = false; }
    }

    internal FalloutReferenceScriptExtraInitializerSnapshot Capture()
    {
        if (_busy) { _fault = checked(_fault + 1); throw new InvalidOperationException("Entered Script initializer cannot be captured."); }
        var snapshot = new FalloutReferenceScriptExtraInitializerSnapshot(Schema, Source, _fields.Stack, Actor, Process,
            Factory, FieldsFactory, Constructor, Epoch, _referenceFlags, _template, _writerBoundary,
            Array.AsReadOnly(_publications.ToArray()), Array.AsReadOnly(_calls.Select(CloneCall).ToArray()),
            _referenceFlagWrites.Count == 0 ? null : Array.AsReadOnly(_referenceFlagWrites.ToArray()));
        Validate(snapshot, Source, _fields.Stack, Actor); return snapshot;
    }

    internal void RequireCurrent(FalloutReferenceScriptExtraInitializationInvocation invocation)
    {
        if (!_busy || _current != invocation || _fault != _enteredFault || invocation.Actor != Actor || invocation.Process != Process ||
            invocation.Epoch != Epoch || invocation.Constructor != Constructor || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            _thread is { } thread && thread != invocation.ThreadId)
            throw new InvalidOperationException("Script initializer lost its actual entered Player, constructor, process or thread.");
        _requireActualInitialization(invocation);
        if (_current != invocation || _fault != _enteredFault || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Script initializer guard swallowed reentry or changed its current owner.");
        _thread ??= invocation.ThreadId;
    }

    private InitializationScope Enter(FalloutReferenceScriptExtraInitializationInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (_busy) { _fault = checked(_fault + 1); throw new InvalidOperationException("Script initializer reentered its actual source call."); }
        if (invocation.Actor != Actor || invocation.Process != Process || invocation.Epoch != Epoch || invocation.Constructor != Constructor ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || _thread is { } thread && thread != invocation.ThreadId ||
            invocation.Initialization == Guid.Empty || !_invocations.Add(invocation.Initialization))
            throw new InvalidOperationException("Script initializer omitted or replayed its original source invocation.");
        _current = invocation; _enteredFault = _fault;
        return new(invocation, checked(++_sequence), _referenceFlags, _template);
    }

    private FalloutReferenceScriptExtraInitializationCall Complete(InitializationScope scope, bool returned, string? failure)
    {
        var call = new FalloutReferenceScriptExtraInitializationCall(Source, scope.Invocation, Factory, FieldsFactory,
            scope.Entered, checked(++_sequence), scope.Step, scope.FlagsBefore, _referenceFlags, scope.Template, scope.Before,
            scope.Selected, Array.AsReadOnly(scope.Reads.ToArray()), scope.ThreadScope, Array.AsReadOnly(scope.Stores.ToArray()),
            returned, failure, scope.Writer, scope.WriterBoundary);
        _calls.Add(call); LastCall = call;
        if (!returned) _failure ??= failure;
        else if (scope.Writer is null) _returnedInitialization = true;
        return call;
    }

    private void RequireBaseSource()
    {
        var record = _records.GetEffective(Actor.Base);
        var digest = Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant();
        if (record.FormKey != Actor.Base || record.Signature != Actor.BaseSignature || record.Flags != Actor.BaseFlags || digest != Actor.BaseSha256)
            throw new InvalidDataException("Script initialization substituted a stale or nonwinning Player base.");
    }
    private FalloutReferenceScriptExtraFormSource ReadForm(FalloutPluginRecord selected)
    {
        var script = FalloutScriptLocals.AttachedScript(_records, selected);
        return new(selected.FormKey, selected.Signature, selected.Flags,
            Convert.ToHexString(SHA256.HashData(selected.ReadData())).ToLowerInvariant(), script?.FormKey);
    }
    private sealed class InitializationScope(FalloutReferenceScriptExtraInitializationInvocation invocation,
        long entered, uint flagsBefore, FalloutFormKey? template)
    {
        internal FalloutReferenceScriptExtraInitializationInvocation Invocation { get; } = invocation;
        internal long Entered { get; } = entered;
        internal uint FlagsBefore { get; } = flagsBefore;
        internal FalloutFormKey? Template { get; } = template;
        internal FalloutReferenceScriptExtraInitializationStep Step = FalloutReferenceScriptExtraInitializationStep.Entered;
        internal FalloutPackageScriptExtra? Before;
        internal FalloutReferenceScriptExtraFormSource? Selected;
        internal FalloutSourceThreadKindScope? ThreadScope;
        internal List<FalloutReferenceScriptExtraInitializationRead> Reads { get; } = [];
        internal List<FalloutPackageCommandFieldStore> Stores { get; } = [];
        internal FalloutReferenceScriptExtraWriter? Writer;
        internal string? WriterBoundary;
    }
}
