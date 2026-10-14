using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutSourceReferencePositionState? _sourcePlayerReferencePosition;
    private FalloutSourceReferenceChangeManager? _sourceReferenceChanges;
    private FalloutReferenceFormFileAssociationState? _sourceReferenceFormFiles;
    private FalloutSourceInitialPlacementCells? _sourceInitialPlacementCells;
    private readonly Guid _sourceInitialPlacementConstructor = Guid.NewGuid();
    private FalloutReferencePositionInvocation? _enteredReferencePosition;
    private readonly Guid _sourceReferenceChangeConstructor = Guid.NewGuid();

    internal object? SourceReferencePositionState => _sourcePlayerReferencePosition is null ? null : new
    {
        reference = _sourcePlayerReferencePosition.Capture(),
        changes = _sourceReferenceChanges?.Capture(),
        formFiles = _sourceReferenceFormFiles?.Capture(),
        initialCells = _sourceInitialPlacementCells?.Capture()
    };

    internal void PrepareStandaloneReferencePositionOwners()
    {
        if (!HasStandalonePlayerScene) return;
        RequireStandaloneControlPublication();
        if (_sourcePlayerReferencePosition is not null || _sourceReferenceChanges is not null)
            throw new InvalidOperationException("Source reference position construction was replayed.");
        if (_processCommonRestore is not null)
            throw new NotSupportedException("source-reference-position-cold-World-bindings-unbound");
        var fields = _sourcePackageFields[_enginePlayer];
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-reference-position-owned-image-unbound");
        var source = FalloutExecutableStringTable.ReadReferencePosition(owned.FalloutExecutablePath, fields.Source);
        _sourcePlayerReferencePosition = new(source, records, scene.Stack, fields.Actor,
            scene.Process, scene.Constructor, RequireActualReferencePosition);
        _sourceReferenceChanges = new(source, scene.Stack, scene.Process, _sourceReferenceChangeConstructor,
            RequireActualReferencePosition);
        var formFiles = FalloutExecutableStringTable.ReadReferenceFormFileAssociation(owned.FalloutExecutablePath, source);
        _sourceReferenceFormFiles = new(formFiles, records, scene.Stack, scene.Process, scene.Constructor,
            fields.Actor, RequireActualReferencePosition);
        scene.ThreadContext.BindReferenceChangeFlags(source);
        var cells = FalloutExecutableStringTable.ReadInitialPlacementCells(owned.FalloutExecutablePath, source);
        _sourceInitialPlacementCells = new(cells, records, scene.Stack, ActualSourceProcessIdentity,
            _sourceInitialPlacementConstructor, RequireActualInitialPlacementCells);
        WithActualReferencePosition(FalloutReferencePositionIngress.ControllerConstruction, invocation =>
            _sourceInitialPlacementCells.ConstructGrid(invocation,
                FalloutInitialPlacementCellsSource.ReadGridInput(FalloutInstallationSettings.Read(owned))));
    }

    private T WithActualReferencePosition<T>(FalloutReferencePositionIngress ingress,
        Func<FalloutReferencePositionInvocation, T> action)
    {
        if (_enteredReferencePosition is not null)
            throw new InvalidOperationException("Source reference owner reentered its actual caller.");
        var owner = _sourcePlayerReferencePosition ?? throw new NotSupportedException("source-reference-position-owner-unbound");
        var actor = ActorProcesses.Read(owner.Actor.Reference);
        var common = ProcessCommon.ReadSceneQueries(owner.Actor.Reference, actor.Epoch);
        var invocation = new FalloutReferencePositionInvocation(Guid.NewGuid(), actor.Source, owner.Process,
            owner.Constructor, common.Process, actor.Epoch, Environment.CurrentManagedThreadId, ingress,
            ingress == FalloutReferencePositionIngress.PackageCommand ? _enteredPackageCommand?.SourceCommand : null);
        _enteredReferencePosition = invocation;
        try { RequireActualReferencePosition(invocation); return action(invocation); }
        finally { _enteredReferencePosition = null; }
    }

    private void RequireActualInitialPlacementCells(FalloutReferencePositionInvocation invocation)
    {
        RequireActualReferencePosition(invocation);
        if (_sourceInitialPlacementCells is not { } cells || cells.Process != ActualSourceProcessIdentity ||
            cells.Constructor != _sourceInitialPlacementConstructor || cells.Stack != CampaignStandalonePlayerScene.Stack)
            throw new InvalidOperationException("Initial placement lost its independent current World/TES field owner.");
    }

    private void RequireActualReferencePosition(FalloutReferencePositionInvocation invocation)
    {
        invocation.ValidatePackageCaller();
        RequireStandaloneControlPublication();
        if (_enteredReferencePosition != invocation || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            _sourcePlayerReferencePosition is not { } owner || owner.Actor != invocation.Actor ||
            owner.Process != invocation.ReferenceProcess || owner.Constructor != invocation.ReferenceConstructor)
            throw new InvalidOperationException("Reference fields left their actual source caller, constructor or thread.");
        var actor = ActorProcesses.Read(invocation.Actor.Reference);
        var common = ProcessCommon.ReadSceneQueries(invocation.Actor.Reference, invocation.Epoch);
        if (actor.Retired || actor.Source != invocation.Actor || actor.Epoch != invocation.Epoch ||
            common.Process != invocation.CommonProcess || common.Actor != invocation.Actor || common.Stack != owner.Stack ||
            CampaignStandalonePlayerScene.Constructor != invocation.ReferenceConstructor)
            throw new InvalidOperationException("Reference fields changed their independent current Common/reference lifetimes.");
        if (invocation.Ingress == FalloutReferencePositionIngress.ControllerConstruction &&
            _enteredEarlyControllerConstruction is { } controller)
        {
            if (controller.Actor != invocation.Actor || controller.CommonProcess != invocation.CommonProcess ||
                controller.Constructor != invocation.ReferenceConstructor || controller.Epoch != invocation.Epoch)
                throw new InvalidDataException("Physical descriptor borrowed another source reference constructor.");
        }
        else if (invocation.Ingress == FalloutReferencePositionIngress.PackageCommand)
        {
            var command = _enteredPackageCommand ?? throw new InvalidOperationException("Reference package borrow has no real entered command.");
            if (command.Actor != invocation.Actor || command.Process != invocation.CommonProcess || command.Epoch != invocation.Epoch)
                throw new InvalidDataException("Reference package borrow changed its actual source command.");
            if (command.SourceCommand != invocation.PackageCommand)
                throw new InvalidDataException("Reference child changed its actual entered package caller.");
            RequireActualSourcePackageCommand(command);
        }
    }

    private FalloutPackageRawPosition ReadEarlyControllerConstructorPosition(FalloutEarlyControllerInvocation constructor)
    {
        RequireActualEarlyControllerConstruction(constructor);
        return WithActualReferencePosition(FalloutReferencePositionIngress.ControllerConstruction,
            invocation => _sourcePlayerReferencePosition!.ReadConstructorPosition(invocation).Position);
    }
    private uint ReadEarlyControllerReferenceScale(FalloutEarlyControllerInvocation constructor)
    {
        RequireActualEarlyControllerConstruction(constructor);
        return WithActualReferencePosition(FalloutReferencePositionIngress.ControllerConstruction,
            invocation => _sourcePlayerReferencePosition!.ReadReferenceScale(invocation).Bits);
    }

    internal void InitializeStandaloneReferenceStartupPositions()
    {
        if (!HasStandalonePlayerScene) return;
        var owner = _sourcePlayerReferencePosition ??
            throw new NotSupportedException("source-reference-position-owner-unbound");
        var consumers = new SourceReferencePositionConsumers(this);
        PublishInitialReferenceBase();
        WithActualReferencePosition(FalloutReferencePositionIngress.ConstructorStartup,
            invocation => owner.StoreConstructorStartupPosition(invocation, consumers));
        InitializeStandaloneReferenceScript();
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-reference-position-owned-image-unbound");
        WithActualReferencePosition(FalloutReferencePositionIngress.ConfiguredStartup,
            invocation => owner.InitializeConstructedPlayerFields(invocation, FalloutInstallationSettings.Read(owned)));
        WithActualReferencePosition(FalloutReferencePositionIngress.ConfiguredStartup,
            invocation => owner.StoreConfiguredStartupPosition(invocation, consumers));
    }

    private void RetireStandaloneReferencePositionOwners()
    {
        _sourcePlayerReferencePosition?.Retire(); _sourcePlayerReferencePosition = null;
        _sourceReferenceChanges?.Retire(); _sourceReferenceChanges = null;
        _sourceReferenceFormFiles?.Retire(); _sourceReferenceFormFiles = null;
        _sourceInitialPlacementCells?.Retire(); _sourceInitialPlacementCells = null;
    }

    private FalloutStandaloneReferenceOwnersSnapshot? CaptureStandaloneReferenceOwners()
    {
        if (_sourceEarlyControllerFactory is null) return null;
        if (_sourceEarlyControllerGroups is null || _sourcePlayerReferencePosition is null || _sourceReferenceChanges is null ||
            _sourceReferenceFormFiles is null || _sourceInitialPlacementCells is null)
            throw new NotSupportedException("source-standalone-reference-owner-graph-incomplete");
        var thread = CampaignStandalonePlayerScene.ThreadContext;
        return new(_sourceEarlyControllerFactory.Capture(), _sourceEarlyControllerGroups.Capture(),
            thread.CapturePhysicsInitializationScopes(), _sourcePlayerReferencePosition.Capture(),
            _sourceReferenceChanges.Capture(), thread.CaptureReferenceChangeFlags(), _sourceEarlyControllerHelperFailure,
            _sourceReferenceBaseAssignment, _sourceReferenceActorResponse, _sourceReferenceFormFiles.Capture(),
            _sourceInitialPlacementCells.Capture(),
            _sourceLinkedPlayerParentHistory?.Capture(_sourceLinkedPlayerParentRevision, _sourceLinkedPlayerParent),
            _sourceDataHandlerLoading?.Capture());
    }
}
