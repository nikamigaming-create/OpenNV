using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPackageRuntimeRegistry? _sourcePackageRegistry;
    private FalloutPackageCommandFieldsSource? _sourcePackageFieldsSource;
    private FalloutPackageAssignmentSource? _sourcePackageAssignmentSource;
    private readonly Dictionary<FalloutFormKey, FalloutActorPackageCommandFields> _sourcePackageFields = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutSourcePackageAssignmentCall> _sourcePackageCommands = [];
    private readonly Dictionary<Guid, FalloutSourcePackageAssignmentCall> _sourcePackageCommandIndex = [];
    private FalloutPackageCommandFieldInvocation? _enteredPackageCommand;
    private FalloutSourcePackageAssignmentCall? _currentPackageCommand;
    private FalloutSourcePackageAssignmentCall? _firstFailedPackageCommand;

    internal object? SourcePackageCommandState => _sourcePackageRegistry is null ? null : new
    {
        source = _sourcePackageAssignmentSource!.Identity,
        registry = _sourcePackageRegistry.Capture(),
        actors = _sourcePackageFields.Values.Select(fields => fields.State).ToArray(),
        current = _currentPackageCommand,
        calls = _sourcePackageCommands.ToArray(),
        referenceScriptInitialization = _sourceReferenceScriptInitializer?.Capture(),
        referencePosition = SourceReferencePositionState,
        earlyController = SourceEarlyControllerState,
        modelLoad = SourcePlayerModelLoadState,
        actorAssignment = _currentActorPackageAssignment,
        dataHandlerLoading = _sourceDataHandlerLoading?.Capture(),
        saveBlocker = SourcePackageCommandSaveBlocker
    };
    internal object? SourcePackageCommandSummary => _sourcePackageRegistry is null ? null : new
    {
        source = _sourcePackageAssignmentSource!.Identity,
        registry = _sourcePackageRegistry.Factory,
        current = _currentPackageCommand,
        saveBlocker = SourcePackageCommandSaveBlocker
    };
    internal string? SourcePackageCommandSaveBlocker => _enteredPackageCommand is not null ?
        "source-package-command-in-flight" : _firstFailedPackageCommand is { } failed ?
        "source-package-command:" + failed.Failure : _sourceReferenceScriptInitializer?.SaveBlocker ??
        _sourceEarlyControllerHelperFailure ?? _sourceEarlyControllerFactory?.SaveBlocker ??
        _sourceDataHandlerLoading?.SaveBlocker ?? _processCommon?.ActorAssignmentSaveBlocker ??
        _sourcePlayerReferencePosition?.SaveBlocker ?? _sourceReferenceChanges?.SaveBlocker ??
        _sourceReferenceFormFiles?.SaveBlocker ??
        _sourceInitialPlacementCells?.SaveBlocker ??
        _sourcePlayerModelData?.SaveBlocker ?? _sourcePlayerMoverSpeed?.SaveBlocker ??
        (HasStandalonePlayerScene ? CampaignStandalonePlayerScene.ThreadContext.ReferenceChangeFlagsSaveBlocker : null) ??
        _sourcePackageFields.Values.FirstOrDefault(fields => fields.OwnedFieldsSaveBlocker is not null)?.OwnedFieldsSaveBlocker;

    internal void PrepareStandaloneSourcePackageCommands()
    {
        if (!HasStandalonePlayerScene) return;
        RequireStandaloneControlPublication();
        if (_sourcePackageRegistry is not null || _sourcePackageFieldsSource is not null)
            throw new InvalidOperationException("Source package command composition was replayed.");
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-package-command-owned-image-unbound");
        var source = FalloutExecutableStringTable.ReadPackageCommandFields(owned.FalloutExecutablePath,
            FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        var assignment = FalloutExecutableStringTable.ReadPackageAssignment(owned.FalloutExecutablePath, source);
        if (FalloutExecutableStringTable.ReadActorMover(owned.FalloutExecutablePath, source.Controls) != scene.ActorMover.Source)
            throw new InvalidDataException("Source package command selected a different actual logical mover constructor/reset graph.");
        if (_processCommonRestore is { } restored)
        {
            ValidateSourcePackageCommandChildren(restored);
            if (restored.ReferenceOwners is not { } reference || _processRuntimeRestore is not { } runtime)
                throw new NotSupportedException("source-cold-package-reference-owner-graph-unbound");
            _ = new FalloutReferencePackageOwnerValidation(records, restored, reference.Position,
                reference.Changes, reference.ThreadReferenceFlags, reference.FormFiles, reference.Parent, runtime);
        }
        var registry = new FalloutPackageRuntimeRegistry(records, source.Controls.Main.EngineSha256, scene.Stack,
            _processCommonRestore?.PackageRegistry);
        try
        {
            var actor = ActorProcesses.Read(_enginePlayer);
            var process = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
            var saved = _processCommonRestore?.CommandFields?.SingleOrDefault(fields => fields.Actor == actor.Source && fields.Epoch == actor.Epoch);
            if (_processCommonRestore is not null && saved is null)
                throw new InvalidDataException("Cold Player omitted its command-time reference/Extra/animation owners.");
            var fields = new FalloutActorPackageCommandFields(source, scene.Stack, actor.Source, process.Process,
                actor.Epoch, scene.Capture().Constructor, RequireActualSourcePackageCommand,
                saved?.Construction ?? new("actual-selected-Player-command-field-constructor", false,
                    "source-Player-post-constructor-initialization-and-command-field-writers-unbound"), saved);
            _sourcePackageFields.Add(_enginePlayer, fields);
            ProcessCommon.BindSourcePackageRegistry(registry);
            if (_processCommonRestore?.PackageCommands is { } history)
            {
                _sourcePackageCommands.AddRange(history);
                foreach (var call in history) _sourcePackageCommandIndex.Add(call.Invocation.SourceCommand, call);
                _firstFailedPackageCommand = history.FirstOrDefault(call => call.Step != FalloutSourcePackageAssignmentStep.Returned);
            }
            _sourcePackageRegistry = registry; _sourcePackageFieldsSource = source; _sourcePackageAssignmentSource = assignment;
            scene.BindActorMoverPackageReset(actor.Source, RequireActualSourcePackageCommand,
                RequireReturnedActorMoverPackageResetJoin);
            PrepareStandaloneReferenceScriptInitialization(fields);
        }
        catch
        {
            // Common already borrows a published registry. Keep that actual
            // owner for failure diagnostics and ordinary World retirement.
            if (ReferenceEquals(_sourcePackageRegistry, registry)) throw;
            foreach (var fields in _sourcePackageFields.Values) fields.Retire();
            _sourcePackageFields.Clear(); registry.Dispose(); throw;
        }
    }

    internal FalloutSourcePackageAssignmentCall ExecuteSourceAddScriptPackage(FalloutReferenceScriptEffect effect, string producer,
        Action<FalloutSourcePackageAssignmentCall>? observe = null)
    {
        RequireStandaloneControlPublication(); ArgumentNullException.ThrowIfNull(effect);
        if (effect.Kind != FalloutReferenceEffectKind.ScriptPackage || effect.Argument is not { } package || effect.Target is not { } target)
            throw new InvalidDataException("Source AddScriptPackage requires its actual typed Actor and PACK command operands.");
        if (_enteredPackageCommand is not null) throw new NotSupportedException("source-package-command-nested-callback-owner-unbound");
        _currentPackageCommand = null;
        var actor = ActorProcesses.Read(target);
        if (!_sourcePackageFields.TryGetValue(target, out var fields))
            throw new NotSupportedException("source-package-command-actual-Actor-constructor-and-current-field-owner-unbound");
        var current = ProcessCommon.ReadSceneQueries(target, actor.Epoch);
        var invocation = new FalloutPackageCommandFieldInvocation(Guid.NewGuid(), actor.Source, current.Process,
            actor.Epoch, Environment.CurrentManagedThreadId);
        _enteredPackageCommand = invocation;
        try
        {
            if (!fields.HasConstructedPlayerAnimationInitialization)
                _ = fields.InitializeConstructedPlayerAnimation(invocation,
                    FalloutInstallationSettings.Read(CampaignPlayerRuntimeSource.OwnedSource));
            if (!fields.HasConstructedSavedPositionInitialization)
                _ = fields.InitializeConstructedPlayerSavedPosition(invocation,
                    _sourcePlayerReferencePosition?.LastPrefix ??
                        throw new NotSupportedException("source-package-SavedPosition-current-reference-prefix-unbound"));
            _ = FalloutSourcePackageAssignment.Execute(invocation, effect.Source, package, producer,
                new SourcePackageCommandConsumers(this, fields), call =>
                { _currentPackageCommand = call; observe?.Invoke(call); });
            return _currentPackageCommand ?? throw new InvalidDataException("Returned source command omitted its actual receipt.");
        }
        finally
        {
            if (_currentPackageCommand is { } call && call.Invocation == invocation)
            {
                _sourcePackageCommands.Add(call);
                _sourcePackageCommandIndex.Add(call.Invocation.SourceCommand, call);
                if (call.Step != FalloutSourcePackageAssignmentStep.Returned) _firstFailedPackageCommand ??= call;
            }
            _enteredPackageCommand = null;
        }
    }

    private void RequireActualSourcePackageCommand(FalloutPackageCommandFieldInvocation invocation)
    {
        RequireStandaloneControlPublication();
        if (invocation != _enteredPackageCommand || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            _sourcePackageRegistry is null || _sourcePackageFieldsSource is null || _sourcePackageAssignmentSource is null ||
            _sourcePackageRegistry.Stack != _processRuntimeStack || _sourcePackageFieldsSource.Controls.Main.EngineSha256 != CampaignMainScriptSource.EngineSha256)
            throw new InvalidOperationException("Source package child left its actual command/source/thread scope.");
        var actor = ActorProcesses.Read(invocation.Actor.Reference);
        var current = ProcessCommon.ReadSceneQueriesForPackageCommand(invocation);
        if (actor.Retired || actor.Source != invocation.Actor || actor.Epoch != invocation.Epoch || current.Process != invocation.Process)
            throw new InvalidOperationException("Source package command lost the real Actor/common process epoch.");
        _sourcePackageRegistry.RequireCurrent();
    }

    private FalloutActorMoverResetJoin RequireReturnedActorMoverPackageResetJoin(
        FalloutPackageCommandFieldInvocation invocation)
    {
        RequireActualSourcePackageCommand(invocation);
        if (_enteredActorMoverJoin is not { } join || _currentActorPackageAssignment is not
            { Step: FalloutActorAssignmentStep.MoverReset } call || call.Invocation != invocation ||
            call.PackageMutation != join.PackageStore || call.Linked?.MoverReceipt != join.LinkedReference ||
            call.HighReset?.MoverReceipt != join.HighReset)
            throw new NotSupportedException("source-Actor-package-linked-reference-and-High-reset-join-unbound");
        return join;
    }

    private FalloutProcessCommonSnapshot CaptureSourcePackageCommandChildren(FalloutProcessCommonSnapshot common)
    {
        if (_sourcePackageRegistry is null) return common;
        if (_enteredPackageCommand is not null) throw new NotSupportedException("source-package-command-snapshot-in-flight");
        var captured = common with
        {
            CommandFields = Array.AsReadOnly(_sourcePackageFields.Values.Select(fields => fields.Capture()).ToArray()),
            PackageCommands = Array.AsReadOnly(_sourcePackageCommands.ToArray()),
            ReferenceOwners = CaptureStandaloneReferenceOwners()
        };
        ValidateSourcePackageCommandChildren(captured);
        if (captured.ReferenceOwners is { } reference)
            _ = new FalloutReferencePackageOwnerValidation(records, captured, reference.Position,
                reference.Changes, reference.ThreadReferenceFlags, reference.FormFiles, reference.Parent,
                ProcessRuntime.Capture());
        return captured;
    }

    internal static void ValidateSourcePackageCommandChildren(FalloutProcessCommonSnapshot common)
    {
        if ((common.PackageRegistry is null) != (common.CommandFields is null) ||
            (common.CommandFields is null) != (common.PackageCommands is null))
            throw new InvalidDataException("Package cold graph omitted its actual registry, field owners or commands.");
        if (common.CommandFields is not { } fields) return;
        var lineage = common.Publications!.Select(publication => publication.Process).ToArray();
        foreach (var actor in fields)
            FalloutActorPackageCommandFields.Validate(actor, actor.Source, common.Stack, actor.Actor, lineage);
        var commands = new HashSet<Guid>();
        foreach (var call in common.PackageCommands!)
        {
            FalloutSourcePackageAssignment.Validate(call, common);
            if (!commands.Add(call.Invocation.SourceCommand) ||
                !fields.Any(actor => actor.Actor == call.Invocation.Actor && actor.Epoch == call.Invocation.Epoch &&
                    actor.Publications.Any(publication => publication.Process == call.Invocation.Process)))
                throw new InvalidDataException("Package cold graph duplicated a command or lost its actual Actor field factory.");
            var actor = fields.Single(value => value.Actor == call.Invocation.Actor && value.Epoch == call.Invocation.Epoch);
            RequireEvent(FalloutSourcePackageAssignmentStep.StartEvent, call.Package, FalloutSourcePackageAssignment.PackageStartedEvent);
            if (call.PreviousPackage?.Fields.Fields.Package is { } previous)
                RequireEvent(FalloutSourcePackageAssignmentStep.PreviousEvent, previous, FalloutSourcePackageAssignment.PackageChangedEvent);
            var current = common.Current.Concat(common.Retired).First(value => value.Source == call.Invocation.Actor && value.Epoch == call.Invocation.Epoch);
            if (call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.ProcessFlag) &&
                !current.RequestFlagStores.Any(store => store.Invocation == call.Invocation && !store.Requested))
                throw new InvalidDataException("Package command omitted its actual returned request-byte store.");
            if (call.ReturnedChildren.Contains(FalloutSourcePackageAssignmentStep.PackageFlag) &&
                !common.PackageRegistry!.Packages.Single(package => package.Form == call.Package).History.Any(store =>
                    store.Invocation == call.Invocation && store.Kind == FalloutPackageRuntimeMutationKind.ScriptAssignedFlag))
                throw new InvalidDataException("Package command omitted its actual shared runtime flag store.");
            void RequireEvent(FalloutSourcePackageAssignmentStep step, FalloutFormKey package, uint mask)
            {
                if (!call.ReturnedChildren.Contains(step) && !(call.FailedAt == step && call.ConsumerEntered)) return;
                var marks = actor.Operations.Where(operation => operation.Invocation == call.Invocation && operation.Kind == "package-event-marker").ToArray();
                if (!marks.Any(operation => operation.EventMark is { } mark && mark.Package == package && mark.Mask == mask &&
                    (call.ReturnedChildren.Contains(step) ? mark.Returned : !mark.Returned)))
                    throw new InvalidDataException("Package command omitted its entered/returned actual event-marker child receipt.");
            }
        }
        foreach (var operation in fields.SelectMany(actor => actor.Operations).Where(operation => operation.Kind == "package-event-marker"))
            if (!commands.Contains(operation.Invocation.SourceCommand))
                throw new InvalidDataException("Package event-marker history has no actual parent source command.");
    }

    private void RetireSourcePackageCommands()
    {
        if (_enteredPackageCommand is not null || _enteredReferenceScriptInitialization is not null ||
            _enteredDataHandlerLoading is not null || _enteredActorMoverJoin is not null)
            throw new NotSupportedException("source-package-command-or-reference-initialization-retirement-in-flight");
        foreach (var fields in _sourcePackageFields.Values) fields.Retire();
        _sourcePackageRegistry?.Dispose(); _sourcePackageRegistry = null;
        _sourcePackageFieldsSource = null; _sourcePackageAssignmentSource = null;
        _sourceActorPackageAssignment = null; _sourceDataHandlerLoading = null;
        _sourceReferenceFollower = null;
        _currentActorPackageAssignment = null;
        _sourcePackageGlobals = null; _sourcePackageGameHour = null; _requireSourcePackageGlobals = null;
    }
}
