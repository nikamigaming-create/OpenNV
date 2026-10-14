using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutEarlyControllerFactory? _sourceEarlyControllerFactory;
    private FalloutCharacterCollisionGroups? _sourceEarlyControllerGroups;
    private FalloutEarlyControllerInvocation? _enteredEarlyControllerConstruction;
    private FalloutEarlyControllerConstructionCall? _sourceEarlyControllerConstruction;
    private FalloutSourceSceneNode? _sourceConstructorReferenceModel;
    private bool _sourceConstructorReferenceModelConstructed;
    private FalloutPluginRecord? _sourceReferenceCurrentBase;
    private bool _sourceReferenceBaseConstructed;
    private string? _sourceEarlyControllerHelperFailure;

    internal object? SourceEarlyControllerState => _sourceEarlyControllerFactory is null ? null : new
    {
        factory = _sourceEarlyControllerFactory.Capture(),
        constructorBaseStored = _sourceReferenceBaseConstructed,
        currentBase = _sourceReferenceCurrentBase?.FormKey,
        baseAssignment = _sourceReferenceBaseAssignment,
        helperFailure = _sourceEarlyControllerHelperFailure
    };

    internal void PrepareStandaloneEarlyPlayerController(float sourceUnitsToMetres)
    {
        if (!HasStandalonePlayerScene) return;
        RequireStandaloneControlPublication();
        if (_sourceEarlyControllerFactory is not null || _sourceEarlyControllerGroups is not null)
            throw new InvalidOperationException("Early Player physics construction was replayed.");
        if (_processCommonRestore is not null)
            throw new NotSupportedException("source-early-controller-cold-native-resource-owner-unbound");
        var fields = _sourcePackageFields[_enginePlayer];
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-early-controller-owned-image-unbound");
        var source = FalloutExecutableStringTable.ReadEarlyController(owned.FalloutExecutablePath,
            fields.Source, scene.ThreadContext.Source);
        var settings = FalloutPhysicalEffectiveSettings.Read(name => FalloutGameSettingFloats.ReadRetained(records,
            name, nameof(PrepareStandaloneEarlyPlayerController)));
        var inputs = new FalloutPhysicalConstructorInputs(source.Fields.Controls.Main.EngineSha256, settings);
        _sourceEarlyControllerGroups = new(inputs);
        // This is the source-qualified inherited reference model field. The
        // original ctor explicitly clears it before a model can be published.
        _sourceConstructorReferenceModel = null; _sourceConstructorReferenceModelConstructed = true;
        // ReadEarlyController qualifies the full inherited constructor tail,
        // including its independent final Base-null store.
        _sourceReferenceCurrentBase = null; _sourceReferenceBaseConstructed = true;
        // The same qualified reference constructor owns ParentCELL. Its field
        // exists before later CELL-list/native publication and package queries.
        ConstructSourcePlayerLinkedParent(false);
        var factory = new FalloutEarlyControllerFactory(source, inputs, _sourceEarlyControllerGroups,
            scene.ThreadContext, sourceUnitsToMetres, RequireActualEarlyControllerConstruction,
            ReadActualConstructorReferenceModel);
        _sourceEarlyControllerFactory = factory;
        var actor = ActorProcesses.Read(_enginePlayer);
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        var invocation = new FalloutEarlyControllerInvocation(Guid.NewGuid(), actor.Source, scene.Stack,
            common.Process, ProcessRuntime.CharacterControllerSourceProcess, scene.ThreadContext.Process,
            actor.Epoch, actor.Level!.Value, scene.Constructor, Environment.CurrentManagedThreadId);
        _enteredEarlyControllerConstruction = invocation;
        try
        {
            var raw = ReadEarlyControllerConstructorPosition(invocation);
            var projected = inputs.ProjectPlayer(FalloutPhysicalVector3.FromValues(
                BitConverter.UInt32BitsToSingle(raw.X), BitConverter.UInt32BitsToSingle(raw.Y),
                BitConverter.UInt32BitsToSingle(raw.Z)), factory.Shared.ReadCenter());
            using var controller = factory.Construct(invocation, projected);
            _sourceEarlyControllerConstruction = factory.LastCall;
            if (_sourceEarlyControllerConstruction is not { Returned: true } returned ||
                returned.Invocation != invocation || returned.Controller != controller.Controller.Identity)
                throw new InvalidDataException("Actual early controller omitted its returned native constructor receipt.");
            ProcessCommon.PublishEarlyController(invocation, controller, RequireActualEarlyControllerConstruction);
            var actual = controller.Controller;
            actual.SetActorProperty(invocation, scene, () => RequireActualEarlyControllerConstruction(invocation));
            var scale = ReadEarlyControllerReferenceScale(invocation);
            actual.StoreActorScale(invocation, factory.Shared.ActorScale(scale,
                ReadActualConstructorBase(invocation), ReadActualSourceBaseHeight));
            actual.SetHelperFlags(invocation, factory.Shared.ReadHelperFlag());
            actual.SetPlayerPropertySelector(invocation);
            actual.SetPlayerGroup(invocation);
        }
        catch (Exception error)
        {
            _sourceEarlyControllerHelperFailure ??= error.Message;
            throw;
        }
        finally
        {
            _sourceEarlyControllerConstruction = factory.LastCall;
            _enteredEarlyControllerConstruction = null;
        }
    }

    private void RequireActualEarlyControllerConstruction(FalloutEarlyControllerInvocation invocation)
    {
        RequireStandaloneControlPublication();
        if (_enteredEarlyControllerConstruction != invocation || _enteredPackageCommand is not null ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || _sourceEarlyControllerFactory is null)
            throw new InvalidOperationException("Early controller left its actual source constructor scope.");
        ProcessCommon.RequireEarlyControllerAssociation(invocation);
        var actor = ActorProcesses.Read(invocation.Actor.Reference);
        var scene = CampaignStandalonePlayerScene;
        if (actor.Retired || actor.Source != invocation.Actor || actor.Epoch != invocation.Epoch ||
            invocation.Constructor != scene.Constructor || invocation.RuntimeProcess != ProcessRuntime.CharacterControllerSourceProcess ||
            invocation.ThreadProcess != scene.ThreadContext.Process || invocation.Stack != scene.Stack)
            throw new InvalidOperationException("Early controller changed its independent actual source owner lineages.");
    }

    private FalloutSourceSceneNode? ReadActualConstructorReferenceModel(FalloutEarlyControllerInvocation invocation)
    {
        RequireActualEarlyControllerConstruction(invocation);
        if (!_sourceConstructorReferenceModelConstructed || !_sourceReferenceBaseConstructed ||
            _sourceReferenceCurrentBase is not null || _sourceReferenceBaseAssignment is not null ||
            _sourceEarlyControllerFactory?.ConstructionPhase != FalloutEarlyControllerConstructionPhase.GroupAllocated)
            throw new NotSupportedException("source-early-reference-model-current-writer-unbound");
        return _sourceConstructorReferenceModel;
    }

    private FalloutPluginRecord? ReadActualConstructorBase(FalloutEarlyControllerInvocation invocation)
    {
        RequireActualEarlyControllerConstruction(invocation);
        if (!_sourceReferenceBaseConstructed)
            throw new NotSupportedException("source-reference-current-Base-constructor-unbound");
        return _sourceReferenceCurrentBase;
    }
    private uint ReadActualSourceBaseHeight(FalloutPluginRecord currentBase) =>
        throw new NotSupportedException("source-current-Base-height-getter-unbound");

    private void RetireStandaloneEarlyControllerFactory()
    {
        _sourceEarlyControllerFactory?.Retire(); _sourceEarlyControllerFactory = null;
        _sourceEarlyControllerGroups?.Dispose(); _sourceEarlyControllerGroups = null;
        _sourceConstructorReferenceModelConstructed = false; _sourceConstructorReferenceModel = null;
        _sourceReferenceBaseConstructed = false; _sourceReferenceCurrentBase = null;
    }
}
