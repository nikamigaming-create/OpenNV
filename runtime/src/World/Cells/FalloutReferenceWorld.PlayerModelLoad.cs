using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerReturnedModel(FalloutReferenceModelDataNode Node,
    FalloutActorSourceModel Source, Action RequireLifetime);

internal sealed partial class FalloutReferenceWorld
{
    private FalloutLoadedAnimationSource? _sourceLoadedPlayerAnimation;
    private FalloutPlayerPostAnimationSource? _sourcePlayerPostAnimation;
    private FalloutPlayerLoadedAnimationState? _loadedPlayerAnimation;
    private FalloutPlayerFirstModelState? _playerFirstModel;
    private FalloutPlayerModelLoadGlobals? _playerModelGlobals;
    private FalloutPlayerModelLoadContinuationState? _playerModelContinuation;
    private FalloutPlayerModelLoadStartupState? _playerModelStartup;
    private FalloutInstallationSettings? _playerModelSettings;
    private FalloutPlayerInventory? _playerModelInventory;
    private FalloutReferenceModelDataInvocation? _enteredPlayerModelLoad;
    private FalloutPlayerModelStartupInvocation? _enteredPlayerModelStartup;
    private FalloutNewGameBootstrap? _enteredPlayerModelBootstrap;
    private string? _playerModelLoadFailure;

    internal object? SourcePlayerModelLoadState => _sourceLoadedPlayerAnimation is null ? null : new
    {
        startup = _playerModelStartup?.Capture(),
        first = _playerFirstModel?.Capture(),
        animation = _loadedPlayerAnimation?.Capture(),
        globals = _playerModelGlobals?.Capture(),
        continuation = _playerModelContinuation?.LastCall,
        failure = _playerModelLoadFailure
    };
    internal FalloutInstallationSettings? StandalonePlayerModelSettings => _playerModelSettings;
    private string? StandalonePlayerModelLoadSaveBlocker => !HasStandalonePlayerScene ? null :
        _playerModelLoadFailure ?? _playerModelStartup?.SaveBlocker ?? _playerFirstModel?.SaveBlocker ??
        _loadedPlayerAnimation?.SaveBlocker ?? "source-Player-enclosing-model-loader-unreturned";

    internal void PrepareStandalonePlayerModelLoading(FalloutPlayerInventory inventory)
    {
        RequireStandaloneControlPublication();
        if (_sourceLoadedPlayerAnimation is not null) throw new InvalidOperationException("Player model loader is already constructed.");
        var content = records.OwnedSource ?? throw new NotSupportedException("source-Player-model-owned-image-unbound");
        var movement = _sourcePlayerActorMovement?.Source ?? throw new NotSupportedException("source-Player-model-movement-owner-unbound");
        var scene = CampaignStandalonePlayerScene;
        var tail = FalloutExecutableStringTable.ReadPlayerReturnTail(content.FalloutExecutablePath, movement, scene.Source);
        var animation = FalloutExecutableStringTable.ReadLoadedPlayerAnimation(content.FalloutExecutablePath, tail);
        _sourceLoadedPlayerAnimation = animation;
        _sourcePlayerPostAnimation = FalloutExecutableStringTable.ReadPlayerPostAnimation(content.FalloutExecutablePath, tail);
        var continuation = FalloutExecutableStringTable.ReadPlayerModelLoadContinuation(content.FalloutExecutablePath, animation);
        var prerequisites = FalloutExecutableStringTable.ReadPlayerModelLoadPrerequisites(content.FalloutExecutablePath,
            continuation, movement.Cells.Reference);
        _playerModelSettings = FalloutInstallationSettings.Read(content);
        _playerModelInventory = inventory;
        _playerModelGlobals = new(continuation, scene.Process, RequireActualPlayerModelLoad);
        _playerFirstModel = new(continuation, scene.Process, scene.Constructor, _playerModelGlobals, RequireActualPlayerModelLoad);
        _loadedPlayerAnimation = new(animation, records, _sourcePackageFields[_enginePlayer], ProcessCommon,
            _sourceReferenceScriptInitializer ?? throw new NotSupportedException("source-reference-flags-owner-unbound"),
            scene.ThreadContext, RequireActualPlayerModelLoad, RequirePlayerModelLoadLifetime);
        var magic = new FalloutActorModelMagicResetState(continuation, records, inventory, scene.ThreadContext,
            RequireActualPlayerModelLoad, actual =>
            {
                RequirePlayerModelLoadLifetime();
                if (!ReferenceEquals(actual, _playerModelInventory)) throw new InvalidOperationException("Model loader changed the actual inventory.");
            });
        _playerModelContinuation = new(continuation, ProcessCommon, scene.ThreadContext, _playerModelGlobals, magic, RequireActualPlayerModelLoad);
        _playerModelStartup = new(prerequisites, records, _playerModelSettings, scene.Stack, ActualSourceProcessIdentity,
            RequireActualPlayerModelStartup, RequireActualPlayerModelLoad, RequirePlayerModelLoadLifetime);
    }

    internal void StartStandaloneCharacterGeneration(FalloutNewGameBootstrap bootstrap, Action start)
    {
        if (!HasStandalonePlayerScene) { start(); return; }
        RequireStandaloneControlPublication();
        if (_enteredPlayerModelStartup is not null) throw new InvalidOperationException("Character generation reentered its actual Start.");
        var owner = _playerModelStartup ?? throw new NotSupportedException("source-Player-model-startup-owner-unbound");
        _enteredPlayerModelStartup = new(Guid.NewGuid(), ActualSourceProcessIdentity, Environment.CurrentManagedThreadId,
            FalloutPlayerModelStartupIngress.CharacterGenerationStart, bootstrap.Quest.FormKey);
        _enteredPlayerModelBootstrap = bootstrap;
        try
        {
            owner.StoreEnteredCharacterGeneration(_enteredPlayerModelStartup, bootstrap);
            start();
        }
        finally { _enteredPlayerModelStartup = null; _enteredPlayerModelBootstrap = null; }
    }

    // The outer Player load encloses the independent first factory and the
    // inherited Character/Actor/Reference load. The inherited factory must
    // return only after Reference stored its returned third-person model.
    internal void LoadStandalonePlayerModels(FalloutScriptSession session, Action<FalloutScriptSession> requireSession,
        Func<FalloutLoadedAnimationFirstSkeletonRead, FalloutPlayerReturnedModel> firstFactory,
        Func<Action<FalloutPlayerReturnedModel>, FalloutPlayerReturnedModel> inheritedFactory)
    {
        RequireStandaloneControlPublication();
        if (_enteredPlayerModelLoad is not null || _playerModelLoadFailure is not null)
            throw new InvalidOperationException("Player model load repeated an entered or failed source call.");
        var actor = ActorProcesses.Read(_enginePlayer);
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        var scene = CampaignStandalonePlayerScene;
        var load = new FalloutReferenceModelDataInvocation(Guid.NewGuid(), actor.Source, scene.Process,
            scene.Constructor, common.Process, actor.Epoch, Environment.CurrentManagedThreadId, FalloutReferenceModelDataIngress.ThreeDLoad);
        _enteredPlayerModelLoad = load;
        try
        {
            RequireActualPlayerModelLoad(load);
            var animation = _loadedPlayerAnimation!;
            animation.EnterPlayerLoad(load);
            var gate = _playerModelStartup!.ReadEarlyGate(load, _sourceReferenceChanges ??
                throw new NotSupportedException("source-reference-change-owner-unbound"));
            if (gate.InventoryPreparationRequired)
                throw new NotSupportedException("source-Player-model-load-worn-inventory-preparation-unowned");
            var skeleton = animation.ReadFirstSkeletonForPlayerLoad(load);
            var first = firstFactory(skeleton);
            _playerFirstModel!.PublishAndInitialize(load, first.Node, (_, name) =>
                throw new NotSupportedException("source-Player-first-model-missing-node-diagnostic-unowned:" + name));
            var firstContext = animation.ConstructFirst(load, first.Node);
            var entry = scene.EnterAnimationLoadView(_sourcePlayerPostAnimation!, load, RequireActualPlayerModelLoad);
            var third = inheritedFactory(model => animation.ConstructCommonModelResources(load, model.Node));
            var owners = new FalloutLoadedAnimationUpdateOwners(scene.ActorMover,
                frame =>
                {
                    RequirePlayerModelLoadLifetime();
                    var current = ActorProcesses.Read(_enginePlayer);
                    if (frame.PlayerProcess != scene.Process || frame.PlayerConstructor != scene.Constructor ||
                        frame.ThreadId != Environment.CurrentManagedThreadId || frame.Common.Actor != current.Source ||
                        frame.Common.Epoch != current.Epoch || current.Retired)
                        throw new InvalidOperationException("Animation frame scale changed its actual Player/Common lifetime.");
                    return WithActualReferencePosition(FalloutReferencePositionIngress.CopyOrLoad,
                        invocation => _sourcePlayerReferencePosition!.ReadReferenceScale(invocation).Bits);
                }, ReadPlayerAnimationLoadScale);
            animation.InitializeCommon(load, third.Node, ReadPlayerModelCurrentBase(load),
                Resources(third, FalloutLoadedAnimationRole.Common), owners, third.RequireLifetime);
            scene.RestoreAnimationLoadView(_sourcePlayerPostAnimation!, load, entry, RequireActualPlayerModelLoad);
            animation.BindFirst(load, first.Node, Resources(first, FalloutLoadedAnimationRole.FirstPerson), owners, first.RequireLifetime);
            _playerModelContinuation!.ContinueThroughSharedShader(load, first.Node, third.Node, firstContext,
                ReadPlayerModelCurrentBase, invocation =>
                {
                    RequireActualPlayerModelLoad(invocation);
                    return RequireRace(ActorRace(invocation.Actor.Reference));
                }, _ => throw new NotSupportedException("source-Player-model-missing-Camera3rd-diagnostic-unowned"));
            // This is a reached boundary, not a successful enclosing return.
            throw new NotSupportedException("source-Player-model-load-following-controller-PipBoy-toddler-children-unowned");

            FalloutLoadedAnimationResources Resources(FalloutPlayerReturnedModel model, FalloutLoadedAnimationRole role) =>
                new(_sourceLoadedPlayerAnimation!, records, records.OwnedSource!, model.Source, model.RequireLifetime,
                    role, role == FalloutLoadedAnimationRole.FirstPerson ? session : null,
                    role == FalloutLoadedAnimationRole.FirstPerson ? requireSession : null);
        }
        catch (Exception error) { _playerModelLoadFailure ??= error.Message; throw; }
        finally { _enteredPlayerModelLoad = null; }
    }

    private FalloutLoadedAnimationBaseRead ReadPlayerModelCurrentBase(FalloutReferenceModelDataInvocation load)
    {
        RequireActualPlayerModelLoad(load);
        return new(_sourceReferenceCurrentBase ?? throw new NotSupportedException("source-current-reference-Base-unbound"),
            _sourceReferenceBaseAssignment ?? throw new NotSupportedException("source-current-reference-Base-assignment-unbound"));
    }
    private uint ReadPlayerAnimationLoadScale(FalloutReferenceModelDataInvocation load)
    {
        RequireActualPlayerModelLoad(load);
        return WithActualReferencePosition(FalloutReferencePositionIngress.CopyOrLoad,
            invocation => _sourcePlayerReferencePosition!.ReadReferenceScale(invocation).Bits);
    }
    private void RequireActualPlayerModelLoad(FalloutReferenceModelDataInvocation load)
    {
        RequirePlayerModelLoadLifetime();
        var actor = ActorProcesses.Read(_enginePlayer);
        var scene = CampaignStandalonePlayerScene;
        var common = ProcessCommon.ReadAnimationLoadOwner(load);
        if (_enteredPlayerModelLoad != load || load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad ||
            load.ThreadId != Environment.CurrentManagedThreadId || load.Actor != actor.Source || actor.Retired ||
            load.Epoch != actor.Epoch || load.ReferenceProcess != scene.Process || load.ReferenceConstructor != scene.Constructor ||
            load.CommonProcess != common.Process)
            throw new InvalidOperationException("Player model load left its actual outer Player/Common/thread invocation.");
    }
    private void RequireActualPlayerModelStartup(FalloutPlayerModelStartupInvocation invocation, FalloutNewGameBootstrap? bootstrap)
    {
        RequirePlayerModelLoadLifetime();
        if (_enteredPlayerModelStartup != invocation || !ReferenceEquals(_enteredPlayerModelBootstrap, bootstrap) ||
            bootstrap is null || invocation.Process != ActualSourceProcessIdentity ||
            invocation.ThreadId != Environment.CurrentManagedThreadId ||
            invocation.Ingress != FalloutPlayerModelStartupIngress.CharacterGenerationStart)
            throw new InvalidOperationException("Player startup byte left its actual character-generation Start.");
    }
    private void RequirePlayerModelLoadLifetime()
    {
        RequireStandaloneControlPublication();
        records.RequireLiveLoadReceipt(records.LoadReceipt);
        if (_playerModelSettings is null || _playerModelInventory is null)
            throw new InvalidOperationException("Player model loader lost its actual settings or inventory.");
    }
    internal bool RetainsStandaloneFirstModel(Guid factory) => _playerFirstModel?.RetainsModel(factory) == true;
    internal bool CleanupUnpublishedStandaloneFirstModel(FalloutReferenceModelDataNode node) =>
        (_playerFirstModel ?? throw new InvalidOperationException("First-model cleanup lost its actual source field.")).CleanupUnpublished(node);
    internal void RetireStandalonePlayerLoadedModels() => NativeNifSceneRetirement.Drain("Player loaded model owners",
        [() => _loadedPlayerAnimation?.Retire(), () => _playerFirstModel?.Retire()]);
}
