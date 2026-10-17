using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutReferenceModelDataState? _sourcePlayerModelData;
    private FalloutPlayerMoverDeltaSource? _sourcePlayerMoverSource;
    private FalloutReferenceModelDataInvocation? _enteredReferenceModel;
    private bool _constructingReferenceModelField;
    private Guid? _enteredReferenceModelLoad;
    private FalloutActorRagdollControllerField? _sourceActorRagdollControllerField;

    private void ConstructStandaloneReferenceModelData(FalloutEarlyControllerSource early)
    {
        RequireStandaloneControlPublication();
        if (_sourcePlayerModelData is not null)
            throw new InvalidOperationException("The current reference data field is already constructed.");
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-reference-data-owned-image-unbound");
        var source = FalloutExecutableStringTable.ReadPlayerMoverDelta(owned.FalloutExecutablePath, early);
        var actor = ActorProcesses.Read(_enginePlayer);
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        _sourcePlayerMoverSource = source;
        var saved = _processCommonRestore?.ReferenceOwners?.ModelData;
        if (_processCommonRestore?.ReferenceOwners?.RagdollControllerField is not null)
            throw new NotSupportedException("source-cold-Actor-ragdoll-controller-field-binding-unowned");
        if (_processCommonRestore is not null && saved is null)
            throw new InvalidDataException("Cold reference data omitted its actual constructor and load history.");
        _sourcePlayerModelData = new(source, records, scene.Stack, scene.Process, common.Process,
            scene.Constructor, actor.Source, actor.Epoch, RequireActualReferenceModel, RequireReferenceModelLifetime, saved);
        if (saved is not null) return;
        _sourceActorRagdollControllerField = new(scene.Process, scene.Constructor, actor.Source);
        _constructingReferenceModelField = true;
        try
        {
            WithActualReferenceModel(FalloutReferenceModelDataIngress.ReferenceConstructor,
                invocation => _sourcePlayerModelData.ConstructReferenceField(invocation));
        }
        finally { _constructingReferenceModelField = false; }
    }

    internal void LoadStandaloneReferenceModel(Func<FalloutActorLogicalSceneSnapshot?, FalloutReferenceModelDataNode> loadActualThirdPerson)
    {
        ArgumentNullException.ThrowIfNull(loadActualThirdPerson);
        RequireStandaloneControlPublication();
        if (_enteredReferenceModelLoad is not null)
            throw new InvalidOperationException("Reference model load reentered its current source factory.");
        var data = _sourcePlayerModelData ?? throw new NotSupportedException("source-reference-data-owner-unbound");
        _enteredReferenceModelLoad = Guid.NewGuid();
        try
        {
            var cold = data.PreparedColdModel;
            if (cold is null)
                WithActualReferenceModel(FalloutReferenceModelDataIngress.ThreeDLoad, invocation =>
                    data.EnsureForThreeDLoad(invocation, ReadReferenceModelParent()));
            var returned = loadActualThirdPerson(cold);
            RequireReferenceModelLifetime();
            returned.RequireCurrent();
            if (cold is not null)
                WithActualReferenceModel(FalloutReferenceModelDataIngress.ColdBinding,
                    invocation => data.BindColdModel(invocation, returned));
            else
                WithActualReferenceModel(FalloutReferenceModelDataIngress.ModelSetter, invocation =>
                    data.StoreReturnedModel(invocation, ReadReferenceModelParent(), returned));
        }
        finally { _enteredReferenceModelLoad = null; }
    }

    internal void ReleaseStandaloneReferenceModel()
    {
        RequireStandalonePlayerSceneRetirement();
        if (_enteredReferenceModelLoad is not null)
            throw new InvalidOperationException("Reference model release reentered its actual source load.");
        var data = _sourcePlayerModelData ?? throw new NotSupportedException("source-reference-data-owner-unbound");
        _enteredReferenceModelLoad = Guid.NewGuid();
        try
        {
            // Final lifetime retirement releases actual retained references and
            // native bindings even after a failed source writer. It does not
            // replay a gameplay setter or erase its committed diagnostic prefix.
            data.Retire();
        }
        finally { _enteredReferenceModelLoad = null; }
    }

    private FalloutReferenceParentCellRead ReadReferenceModelParent() =>
        WithActualReferencePosition(FalloutReferencePositionIngress.CopyOrLoad,
            invocation => new SourceReferencePositionConsumers(this).ReadCurrentParentCell(invocation));

    internal bool RetainsReturnedReferenceModel(FalloutSourceSceneNode node)
    {
        RequireReferenceModelLifetime();
        return _sourcePlayerModelData!.RetainsReturnedModel(node);
    }

    internal void CleanupUnpublishedReferenceModel(FalloutSourceSceneNode node, Action preserve, Action retire)
    {
        RequireReferenceModelLifetime();
        _sourcePlayerModelData!.CleanupUnpublishedModel(node, preserve, retire);
    }

    private void WithActualReferenceModel(FalloutReferenceModelDataIngress ingress,
        Action<FalloutReferenceModelDataInvocation> action)
    {
        if (_enteredReferenceModel is not null)
            throw new InvalidOperationException("Reference data reentered its actual source child.");
        var scene = CampaignStandalonePlayerScene;
        var actor = ActorProcesses.Read(_enginePlayer);
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        var invocation = new FalloutReferenceModelDataInvocation(Guid.NewGuid(), actor.Source,
            scene.Process, scene.Constructor, common.Process, actor.Epoch,
            Environment.CurrentManagedThreadId, ingress);
        _enteredReferenceModel = invocation;
        try { RequireActualReferenceModel(invocation); action(invocation); }
        finally { _enteredReferenceModel = null; }
    }

    private void RequireActualReferenceModel(FalloutReferenceModelDataInvocation invocation)
    {
        RequireReferenceModelLifetime();
        var scene = CampaignStandalonePlayerScene;
        var actor = ActorProcesses.Read(_enginePlayer);
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        if (_enteredReferenceModel != invocation || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            invocation.Actor != actor.Source || invocation.ReferenceProcess != scene.Process ||
            invocation.ReferenceConstructor != scene.Constructor || invocation.CommonProcess != common.Process ||
            invocation.Epoch != actor.Epoch || actor.Retired ||
            invocation.Ingress == FalloutReferenceModelDataIngress.ReferenceConstructor && !_constructingReferenceModelField ||
            invocation.Ingress is FalloutReferenceModelDataIngress.ThreeDLoad or FalloutReferenceModelDataIngress.ModelSetter or FalloutReferenceModelDataIngress.ColdBinding &&
                _enteredReferenceModelLoad is null)
            throw new InvalidOperationException("Reference data left its actual constructor/load/Common/thread scope.");
    }

    private void RequireReferenceModelLifetime()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sourceMutableNativeOwner is null || _sourceMutableNativeThread != Environment.CurrentManagedThreadId ||
            _sourcePlayerModelData is not { } data || _sourcePlayerMoverSource != data.Source ||
            data.Process != CampaignStandalonePlayerScene.Process || data.Constructor != CampaignStandalonePlayerScene.Constructor)
            throw new InvalidOperationException("Reference data lost its current source field owner.");
    }
}
