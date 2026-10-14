using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    // Call from PrepareStandaloneReferencePositionOwners after qualifying the
    // same sources used by its fresh branch. The caller returns immediately.
    private void RestoreStandaloneReferencePositionOwners(FalloutReferencePositionSource source,
        FalloutReferenceFormFileSource files, FalloutInitialPlacementCellsSource cells,
        FalloutStandaloneReferenceOwnersSnapshot saved)
    {
        var scene = CampaignStandalonePlayerScene;
        var fields = _sourcePackageFields[_enginePlayer];
        _sourcePlayerReferencePosition = new(source, records, scene.Stack, fields.Actor,
            scene.Process, scene.Constructor, RequireActualReferencePosition, saved.Position);
        _sourceReferenceChanges = new(source, scene.Stack, scene.Process, _sourceReferenceChangeConstructor,
            RequireActualReferencePosition, saved.Changes,
            scene.ThreadContext.RetainCurrentFrameReferenceChangeRead);
        _sourceReferenceFormFiles = new(files, records, scene.Stack, scene.Process, scene.Constructor,
            fields.Actor, RequireActualReferencePosition, saved.FormFiles);
        scene.ThreadContext.BindReferenceChangeFlags(source, saved.ThreadReferenceFlags);
        _sourceInitialPlacementCells = new(cells, records, scene.Stack, ActualSourceProcessIdentity,
            _sourceInitialPlacementConstructor, RequireActualInitialPlacementCells, saved.InitialCells);
        RestoreStandaloneReferenceParent(saved.Parent ??
            throw new NotSupportedException("source-cold-World-ParentCELL-field-history-unbound"));
        RestoreStandaloneReferenceBase(saved);
        // No grid constructor, source prefix, Base setter, startup XYZ/yaw,
        // Script initializer or Saved18 initialization is entered on this path.
    }

    private void RestoreStandaloneReferenceParent(FalloutReferenceParentCellSnapshot saved)
    {
        var actor = ReadCombatActorIdentity(_enginePlayer);
        var scene = CampaignStandalonePlayerScene;
        if (_sourceLinkedPlayerParentHistory is not null || _sourceLinkedPlayerParentConstructed)
            throw new InvalidOperationException("Cold ParentCELL field was already published.");
        var reference = _sourcePlayerReferencePosition ?? throw new InvalidOperationException("Cold reference fields are absent.");
        _ = new FalloutReferenceParentCellValidation(saved);
        if (saved.Actor != actor || saved.Stack != scene.Stack)
            throw new InvalidDataException("Cold ParentCELL changed its actual source Actor/stack.");
        // These are the existing World fields. The source list authenticates
        // membership separately; it does not supply a replacement field value.
        _sourceLinkedPlayerParent = saved.Cell;
        _sourceLinkedPlayerParentRevision = saved.Sequence;
        _sourceLinkedPlayerParentHistory = new(reference.Source, scene.Stack, ActualSourceProcessIdentity,
            _sourceLinkedPlayerParentFactory, scene.Constructor, actor, saved.Sequence, saved.Cell, saved);
        _sourceLinkedPlayerParentConstructed = true;
        _sourceCellLinks?.RequireRuntimeActorParent(actor, CurrentSourcePlayerLinkedCell);
    }

    private void RestoreStandaloneReferenceBase(FalloutStandaloneReferenceOwnersSnapshot saved)
    {
        var reference = _sourcePlayerReferencePosition!;
        var assignment = saved.BaseAssignment ?? throw new InvalidDataException("Cold reference has no actual Base assignment prefix.");
        if (!assignment.Returned || !assignment.PointerStored || !assignment.FlagReturned || assignment.Failure is not null ||
            assignment.Inputs is null || assignment.Invocation.Actor != reference.Actor || assignment.After != reference.Actor.Base ||
            assignment.ActorResponse != saved.ActorResponse)
            throw new InvalidDataException("Cold Base assignment is unreturned or differs from retained actual fields.");
        var current = records.GetEffective(assignment.After);
        var inputs = FalloutReferenceBaseAssignmentInputs.Read(reference.Source, records, reference.Actor);
        if (inputs != assignment.Inputs || saved.ActorResponse != (inputs.ActorResponse ? (byte)1 : (byte)0))
            throw new InvalidDataException("Cold Base/Actor response changed its exact winning dependencies.");
        _sourceReferenceCurrentBase = current; _sourceReferenceBaseConstructed = true;
        _sourceReferenceBaseAssignment = assignment; _sourceReferenceActorResponse = saved.ActorResponse;
    }

    // Call instead of the original early constructor on the cold branch, after
    // reference, Parent and base field binding. Component APIs own all actual
    // resource allocation, retained history, failed prefixes and retirement.
    private void RestoreStandaloneEarlyPlayerController(FalloutEarlyControllerSource source,
        FalloutPhysicalConstructorInputs inputs, float sourceUnitsToMetres, FalloutStandaloneReferenceOwnersSnapshot saved)
    {
        var scene = CampaignStandalonePlayerScene;
        if (saved.HelperFailure is not null) throw new InvalidOperationException(saved.HelperFailure);
        _sourceEarlyControllerGroups = new(inputs, saved.CollisionGroups);
        scene.ThreadContext.RetainColdPhysicsInitializationScopes(saved.PhysicsScopes);
        var factory = new FalloutEarlyControllerFactory(source, inputs, _sourceEarlyControllerGroups,
            scene.ThreadContext, sourceUnitsToMetres, RequireActualEarlyControllerConstruction,
            _ => throw new InvalidOperationException("Cold resources cannot enter original model construction."), saved.EarlyController);
        _sourceEarlyControllerFactory = factory;
        var actor = ActorProcesses.Read(_enginePlayer);
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        var previousField = _processCommonRestore!.Current.Single(value => value.Source == actor.Source && value.Epoch == actor.Epoch).EarlyController ??
            throw new InvalidDataException("Cold Common omitted its actual early-controller field.");
        var controller = saved.EarlyController.Controllers.Single(value => value.Identity == previousField.Controller && !value.Retired);
        var current = new FalloutEarlyControllerInvocation(Guid.NewGuid(), actor.Source, scene.Stack,
            common.Process, ProcessRuntime.CharacterControllerSourceProcess, scene.ThreadContext.Process, actor.Epoch,
            actor.Level!.Value, scene.Constructor, Environment.CurrentManagedThreadId);
        var cold = new FalloutEarlyControllerColdInvocation(Guid.NewGuid(), current);
        _enteredEarlyControllerConstruction = current;
        try
        {
            using var supplied = factory.RebindColdController(cold, controller, scene, RequireCold, () => RequireCold(cold));
            var binding = factory.LastColdBinding ?? throw new InvalidDataException("Cold resource owner omitted its actual binding receipt.");
            ProcessCommon.RebindColdEarlyController(binding, supplied, RequireCold);
            _sourceEarlyControllerConstruction = factory.LastCall;
        }
        catch (Exception error) { _sourceEarlyControllerHelperFailure ??= error.Message; throw; }
        finally { _enteredEarlyControllerConstruction = null; }
        void RequireCold(FalloutEarlyControllerColdInvocation invocation)
        {
            if (invocation != cold) throw new InvalidOperationException("Cold early resources changed their actual binding scope.");
            RequireActualEarlyControllerConstruction(current);
        }
    }
}
