using OpenNV.Runtime.Content;
using System.Text.Json.Serialization;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerModelControllerCall(FalloutReferenceModelDataInvocation Load,
    string Step, FalloutReferenceControllerExtraRead? Extra, FalloutFormKey? Parent,
    FalloutActorProcessFact<int>? Mode, FalloutActorPhysicalRegistrationTrace? Admission,
    FalloutPlayerThreadModelCacheRead? ModelCache, FalloutSourceSceneNode? Model,
    Guid? Controller, bool Returned, string? Failure)
{
    [JsonInclude] internal FalloutReferenceControllerExtraRead? SecondaryExtra { get; init; }
    [JsonInclude] internal IReadOnlyList<FalloutControllerModelFieldRead> BodyReads { get; init; } = [];
    [JsonInclude] internal IReadOnlyList<string> MotionBodies { get; init; } = [];
    [JsonInclude] internal Guid? PhysicalModel { get; init; }
    [JsonInclude] internal bool? MotionEnabled { get; init; }
    [JsonInclude] internal int? Tier { get; init; }
    [JsonInclude] internal FalloutLivingRagdollBindingSnapshot? RagdollBinding { get; init; }
    [JsonInclude] internal FalloutPlayerRagdollTemplateRead? Template { get; init; }
    [JsonInclude] internal FalloutPlayerCollisionModeRead? CollisionMode { get; init; }
    [JsonInclude] internal FalloutLivingRagdollTemplateSnapshot? RagdollTemplate { get; init; }
    [JsonInclude] internal IReadOnlyList<FalloutRagdollWorldUpdate> WorldUpdates { get; init; } = [];
    [JsonInclude] internal FalloutPlayerRagdollModelKey? RagdollKey { get; init; }
    [JsonInclude] internal FalloutLivingRagdollSkeletonSnapshot? PhysicalSkeleton { get; init; }
    [JsonInclude] internal FalloutLivingRagdollDriveSnapshot? Drive { get; init; }
    [JsonInclude] internal FalloutLivingRagdollAnimationSnapshot? AnimationSkeleton { get; init; }
    [JsonInclude] internal FalloutLivingRagdollMappingSnapshot? Mapping { get; init; }
    [JsonInclude] internal FalloutRagdollArmInitialization? Arms { get; init; }
    [JsonInclude] internal FalloutLivingRagdollControllerSnapshot? RagdollController { get; init; }
    [JsonInclude] internal Guid? ActorRagdollController { get; init; }
    [JsonInclude] internal FalloutActorRagdollControllerFieldSnapshot? RagdollField { get; init; }
    [JsonInclude] internal FalloutRagdollBodyPartPreparation? BodyPartPreparation { get; init; }
    [JsonInclude] internal FalloutRagdollFootIkSnapshot? FootIk { get; init; }
    [JsonInclude] internal FalloutReferenceAnimationNoteExtra? AnimationNotes { get; init; }
    [JsonInclude] internal bool? AnimationNoteRegistrationResult { get; init; }
    [JsonInclude] internal FalloutPlayerThreadModelCacheRead? AnimationNoteModelCache { get; init; }
    [JsonInclude] internal FalloutAnimationNoteSourceRegistration? AnimationNoteSourceRegistration { get; init; }
    [JsonInclude] internal FalloutControllerModelFieldRead? CollisionGroupBodyRead { get; init; }
    [JsonInclude] internal uint? CurrentCollisionFilter { get; init; }
    [JsonInclude] internal string? BodyPartModelPath { get; init; }
    [JsonInclude] internal string? DeathAssetPath { get; init; }
    [JsonInclude] internal FalloutRagdollDeathInitialization? DeathInitialization { get; init; }
    [JsonInclude] internal FalloutControllerModelFieldRead? FinalBodyRead { get; init; }
    [JsonInclude] internal bool ActorConfigurationReturned { get; init; }
    [JsonInclude] internal FalloutPlayerModelAssociationCall? Association { get; init; }
}

internal sealed partial class FalloutReferenceWorld
{
    private readonly FalloutActorPhysicalAdmissionFields _sourcePlayerPhysicalAdmission = FalloutActorPhysicalAdmissionFields.Constructed;
    private FalloutPlayerModelControllerCall? _playerModelControllerCall;
    private readonly FalloutRagdollDescriptorCache _ragdollDescriptors = new();
    private FalloutNpcBodyPartTables? _npcBodyPartTables;

    internal FalloutActorPhysicalAdmissionFields ReadStandalonePlayerPhysicalAdmissionFields()
    { RequireStandaloneControlPublication(); _sourcePlayerPhysicalAdmission.Validate(); return _sourcePlayerPhysicalAdmission; }

    private void ContinueStandalonePlayerModelController(FalloutReferenceModelDataInvocation load, FalloutPlayerReturnedModel returnedModel,
        Func<FalloutReferenceModelDataInvocation, FalloutPlayerReturnedModel, FalloutLivingActorModel> modelPhysics)
    {
        RequireActualPlayerModelLoad(load);
        if (_playerModelControllerCall is not null) throw new InvalidOperationException("Player model controller helper replayed an entered source call.");
        _playerModelControllerCall = new(load, "reference-extra", null, null, null, null, null, null, null, false, null);
        try
        {
            var extra = (_sourceReferenceScriptInitializer ?? throw new NotSupportedException("source-controller-Extra-owner-unbound"))
                .ReadControllerConstructorExtra(FalloutPlayerModelLoadContinuationSource.ControllerAuxiliaryExtra, load, RequireActualPlayerModelLoad);
            _playerModelControllerCall = _playerModelControllerCall with { Extra = extra };
            Step("process-and-ParentCELL");
            var actor = ActorProcesses.Read(_enginePlayer);
            var common = ProcessCommon.ReadAnimationLoadOwner(load);
            _playerModelControllerCall = _playerModelControllerCall with { Tier = common.Tier };
            if (common.Tier is not (0 or 1))
            { Return(); return; }
            var parent = ReadReferenceModelParent().Cell;
            _playerModelControllerCall = _playerModelControllerCall with { Parent = parent };
            if (parent is not null) throw new NotSupportedException("source-controller-helper-current-CELL-state-and-world-consumers-unowned");
            Step("Actor-physical-state");
            var physical = ReadStandalonePlayerPhysicalAdmissionFields();
            if (physical.State is 2 or 4) { Return(); return; }
            Step("physical-registration-predicate");
            _ = ReadStandalonePlayerPhysicalAdmission(ReadModel, () => RequireActualPlayerModelLoad(load),
                admission => _playerModelControllerCall = _playerModelControllerCall with
                { Admission = admission, Mode = admission.Modes.FirstOrDefault() });
            Step("direct-model-preparation");
            var model = ReadModel();
            if (model is not null)
            {
                var actual = _sourcePlayerModelData!.ReadForPlayerLoad(load, RequireActualPlayerModelLoad);
                if (actual is null || actual.Node != model)
                    throw new NotSupportedException("source-controller-cached-model-logical-owner-unbound");
                actual.LogicalScene.PrepareControllerModel(load, RequireActualPlayerModelLoad);
            }
            Step("secondary-model-Extra");
            _playerModelControllerCall = _playerModelControllerCall with
            {
                SecondaryExtra = _sourceReferenceScriptInitializer.ReadControllerConstructorExtra(FalloutPlayerModelLoadContinuationSource.ControllerModelExtra, load, RequireActualPlayerModelLoad)
            };
            Step("current-controller");
            using var controller = ProcessCommon.BorrowEarlyController(_enginePlayer, actor.Epoch);
            if (controller is null) throw new NotSupportedException("source-controller-model-load-null-controller-factory-unowned");
            controller.RequireCurrent();
            _playerModelControllerCall = _playerModelControllerCall with { Controller = controller.Controller.Identity };
            if (_playerModelControllerCall.Admission!.Register == true)
            {
                Step("physical-utility-registration");
                throw new NotSupportedException("source-controller-helper-physical-utility-registration-consumer-unowned");
            }
            Step("controller-model-association");
            var body = controller.Controller.ReadModelLoadField(load, FalloutControllerPostProcessField.FirstBody, RequireActualPlayerModelLoad);
            _playerModelControllerCall = _playerModelControllerCall with { BodyReads = [body] };
            if (body.Value is not null) throw new NotSupportedException("source-controller-body-current-model-property-getter-unowned");
            model = ReadModel();
            if (model is not null)
            {
                Step("controller-null-body-bindings");
                var firstBody = controller.Controller.ReadModelLoadField(load, FalloutControllerPostProcessField.FirstBody, RequireActualPlayerModelLoad);
                var secondBody = controller.Controller.ReadModelLoadField(load, FalloutControllerPostProcessField.SecondBody, RequireActualPlayerModelLoad);
                _playerModelControllerCall = _playerModelControllerCall with { BodyReads = [body, firstBody, secondBody] };
                if (firstBody.Value is not null || secondBody.Value is not null)
                    throw new NotSupportedException("source-controller-body-model-property-setter-unowned");
                Step("model-motion");
                if (model != returnedModel.Node.Node) throw new NotSupportedException("source-controller-cached-model-motion-owner-unbound");
                var enabled = returnedModel.Node.LogicalScene.ReadControllerModelMotionEnabled();
                _playerModelControllerCall = _playerModelControllerCall with { MotionEnabled = enabled };
                if (enabled)
                {
                    var physicalModel = modelPhysics(load, returnedModel);
                    RequireStandalonePlayerModelLoadPhysics(load, returnedModel); physicalModel.RequireCurrent();
                    if (!ReferenceEquals(physicalModel.Source, returnedModel.Source))
                        throw new InvalidOperationException("Model motion replaced the actual retained C# physical assembly.");
                    _playerModelControllerCall = _playerModelControllerCall with { PhysicalModel = physicalModel.Generation };
                    var stores = new List<string>();
                    foreach (var physicalBody in physicalModel.Bodies)
                    {
                        RequireActualPlayerModelLoad(load);
                        physicalBody.PublishModelMotion(_playerModelControllerCall.Admission!.Motion!.Value);
                        stores.Add(physicalBody.Key);
                        _playerModelControllerCall = _playerModelControllerCall with { MotionBodies = stores.ToArray() };
                    }
                }
                Step("Actor-ragdoll-controller-construction");
                var ragdollField = _sourceActorRagdollControllerField ??
                    throw new NotSupportedException("source-Actor-ragdoll-current-field-constructor-owner-unbound");
                var previousRagdoll = ragdollField.Read(load, RequireActualPlayerModelLoad);
                _playerModelControllerCall = _playerModelControllerCall with { RagdollField = ragdollField.Capture() };
                if (previousRagdoll is not null)
                    throw new NotSupportedException("source-Actor-ragdoll-current-controller-replacement-retirement-unowned");
                var rigModel = modelPhysics(load, returnedModel);
                var template = ReadPlayerRagdollTemplate(load, returnedModel);
                var rigKey = ReadPlayerRagdollModelKey(load, returnedModel);
                _playerModelControllerCall = _playerModelControllerCall with { RagdollKey = rigKey };
                var ragdoll = rigModel.BeginRagdollController(load, RequireActualPlayerModelLoad);
                try { _ = rigModel.BindRagdollSkeleton(returnedModel.Node.LogicalScene, load, RequireActualPlayerModelLoad); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollBinding = rigModel.CaptureRagdollBinding() }; }
                Step("Actor-ragdoll-template-and-pose-initialization");
                var logical = returnedModel.Node.LogicalScene;
                try { rigModel.PrepareRagdollTemplate(load, RequireActualPlayerModelLoad); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollTemplate = rigModel.Capture().RagdollTemplate }; }
                logical.ApplyRagdollTemplatePose(load, template, RequireActualPlayerModelLoad);
                Step("Actor-ragdoll-template-root-controller-update");
                var collisionMode = CampaignStandalonePlayerScene.ThreadContext.ReadModelLoadCollisionMode(load, RequireActualPlayerModelLoad);
                _playerModelControllerCall = _playerModelControllerCall with { CollisionMode = collisionMode };
                try
                {
                    logical.UpdateRagdollTemplateWorld(load, RequireActualPlayerModelLoad,
                        node => rigModel.UpdateRagdollTemplateCollision(node, collisionMode, RequireActualPlayerModelLoad));
                }
                finally { _playerModelControllerCall = _playerModelControllerCall with { WorldUpdates = logical.Capture().RagdollWorldUpdates ?? [] }; }
                Step("Actor-ragdoll-physical-skeleton-initialization");
                try { _ = rigModel.InitializePhysicalSkeleton(load, logical, rigKey.Value, _ragdollDescriptors, RequireActualPlayerModelLoad); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { PhysicalSkeleton = rigModel.CapturePhysicalSkeleton() }; }
                Step("Actor-ragdoll-physical-drive-initialization");
                try
                {
                    rigModel.InitializeRagdollDrive(load, name => _playerModelSettings!.NumericIni.Find(FalloutIniCollection.Main, name) ??
                        throw new NotSupportedException("source-ragdoll-current-drive-INI-field-unbound:" + name), RequireActualPlayerModelLoad);
                }
                finally { _playerModelControllerCall = _playerModelControllerCall with { Drive = rigModel.CaptureRagdollDrive() }; }
                Step("Actor-ragdoll-prior-animation-arm-fields");
                try { rigModel.InitializeRagdollArms(load, _ragdollDescriptors, RequireActualPlayerModelLoad); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { Arms = rigModel.CaptureRagdollArms() }; }
                Step("Actor-ragdoll-animation-skeleton-initialization");
                try { rigModel.InitializeAnimationSkeleton(load, logical, _ragdollDescriptors, RequireActualPlayerModelLoad); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { AnimationSkeleton = rigModel.CaptureAnimationSkeleton() }; }
                Step("Actor-ragdoll-skeleton-mapper-initialization");
                try { rigModel.InitializeRagdollMapping(load, _ragdollDescriptors, RequireActualPlayerModelLoad); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { Mapping = rigModel.CaptureRagdollMapping() }; }
                Step("Actor-ragdoll-restored-root-update");
                logical.RestoreRagdollTemplatePose(load, RequireActualPlayerModelLoad);
                var restoredMode = CampaignStandalonePlayerScene.ThreadContext.ReadModelLoadCollisionMode(load, RequireActualPlayerModelLoad);
                try
                {
                    logical.UpdateRagdollTemplateWorld(load, RequireActualPlayerModelLoad,
                        node => rigModel.UpdateRagdollTemplateCollision(node, restoredMode, RequireActualPlayerModelLoad));
                }
                finally { _playerModelControllerCall = _playerModelControllerCall with { WorldUpdates = logical.Capture().RagdollWorldUpdates ?? [] }; }
                Step("Actor-ragdoll-temporary-flags-restore");
                try { rigModel.RestoreRagdollTemplateFlags(load, RequireActualPlayerModelLoad); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollTemplate = rigModel.Capture().RagdollTemplate }; }
                Step("Actor-ragdoll-controller-return");
                try { ragdoll.Return(logical); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollController = ragdoll.Capture() }; }
                Step("Actor-ragdoll-controller-field-publication");
                try { ragdollField.Publish(load, ragdoll, RequireActualPlayerModelLoad); }
                finally
                {
                    _playerModelControllerCall = _playerModelControllerCall with
                    { RagdollField = ragdollField.Capture(), ActorRagdollController = ragdollField.Capture().Controller };
                }
                if (!ragdollField.Read(load, RequireActualPlayerModelLoad)!.ReadReady())
                    throw new NotSupportedException("source-Actor-ragdoll-not-ready-diagnostic-child-unowned");
                Step("Actor-ragdoll-root-transform-configuration");
                try { ragdoll.ConfigureModelAxes(); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollController = ragdoll.Capture() }; }
                Step("Actor-ragdoll-body-part-configuration");
                var baseRecord = ReadPlayerModelCurrentBase(load).Record;
                if (baseRecord.Signature != "NPC_") throw new NotSupportedException("source-ragdoll-non-NPC-body-part-virtual-unowned");
                var bodyPartRecord = (_npcBodyPartTables ??= FalloutNpcBodyPartTables.FromLoadedRecords(records)).Read(baseRecord.FormKey,
                    ReadPlayerModelCurrentBase(load).Record.FormKey);
                try
                {
                    _ = rigModel.PrepareRagdollBodyParts(load, bodyPartRecord,
                        _ => throw new NotSupportedException("source-ragdoll-body-part-tracking-configuration-unowned"), RequireActualPlayerModelLoad);
                }
                finally { _playerModelControllerCall = _playerModelControllerCall with { BodyPartPreparation = rigModel.CaptureRagdollBodyParts() }; }
                Step("Actor-ragdoll-body-part-editor-flags");
                bool prime;
                try { prime = rigModel.ClassifyRagdollBodyPartModel(load, bodyPartRecord, RequireActualPlayerModelLoad); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { BodyPartPreparation = rigModel.CaptureRagdollBodyParts() }; }
                if (prime) throw new NotSupportedException("source-ragdoll-Prime-controller-feature-stores-unowned");
                Step("Actor-ragdoll-foot-IK-initialization");
                try
                {
                    rigModel.InitializeFootIk(load, logical, name => _playerModelSettings!.NumericIni.Find(FalloutIniCollection.Main, name) ??
                    throw new NotSupportedException("source-foot-IK-current-INI-field-unbound:" + name), RequireActualPlayerModelLoad);
                }
                finally { _playerModelControllerCall = _playerModelControllerCall with { FootIk = rigModel.CaptureFootIk() }; }
                Step("Actor-ragdoll-IK-feature-publication");
                try { ragdoll.PublishIkFeatureCopies(load); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollController = ragdoll.Capture() }; }
                if (!ragdoll.ReadIkFeatures().FootEnabled && rigModel.CaptureRagdollBodyParts()!.IkNodes.Count != 0)
                    throw new NotSupportedException("source-Actor-ragdoll-foot-IK-warning-consumer-unowned");
                if (ragdoll.ReadIkFeatures().ArmEnabled)
                {
                    Step("Actor-ragdoll-current-animation-callback-registration");
                    try
                    {
                        var notes = _sourceReferenceScriptInitializer.GetActorAnimationNotes(load, RequireActualPlayerModelLoad);
                        _playerModelControllerCall = _playerModelControllerCall with { AnimationNoteRegistrationResult = notes.RegisterRagdoll(ragdollField) };
                    }
                    finally { _playerModelControllerCall = _playerModelControllerCall with { AnimationNotes = _sourceReferenceScriptInitializer.CaptureAnimationNoteExtra() }; }
                    Step("Actor-ragdoll-current-animation-note-source-registration");
                    var noteCache = CampaignStandalonePlayerScene.ThreadContext.ReadEarlyControllerModelCache(Guid.NewGuid(),
                        _sourcePlayerModelData!.Factory, _enginePlayer, () => RequireActualPlayerModelLoad(load));
                    _playerModelControllerCall = _playerModelControllerCall with { AnimationNoteModelCache = noteCache };
                    var currentModel = _sourcePlayerModelData.ReadForPlayerLoad(load, RequireActualPlayerModelLoad);
                    if (noteCache.Matched && noteCache.Model != currentModel?.Node)
                        throw new NotSupportedException("source-animation-note-cached-model-current-controller-owner-unbound");
                    if (currentModel?.LogicalScene.ReadCurrentRootAnimationController(load, RequireActualPlayerModelLoad) is { } currentManager)
                    {
                        var notes = _sourceReferenceScriptInitializer.GetActorAnimationNotes(load, RequireActualPlayerModelLoad);
                        _playerModelControllerCall = _playerModelControllerCall with
                        { AnimationNoteSourceRegistration = currentManager.RegisterActorAnimationNotes(load, notes, RequireActualPlayerModelLoad) };
                    }
                }
                Step("Actor-ragdoll-controller-post-configuration");
                var groupBody = controller.Controller.ReadModelLoadField(load, FalloutControllerPostProcessField.FirstBody, RequireActualPlayerModelLoad);
                _playerModelControllerCall = _playerModelControllerCall with { CollisionGroupBodyRead = groupBody };
                if (groupBody.Value is not null)
                    throw new NotSupportedException("source-ragdoll-current-character-body-collision-filter-getter-unowned");
                _playerModelControllerCall = _playerModelControllerCall with { CurrentCollisionFilter = 0 };
                try { ragdoll.PublishCollisionGroup(load, 0); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollController = ragdoll.Capture() }; }
                Step("Actor-ragdoll-body-part-model-path");
                var bodyPartModel = FalloutBodyPartData.Read(bodyPartRecord).ModelPath;
                _playerModelControllerCall = _playerModelControllerCall with { BodyPartModelPath = bodyPartModel };
                var deathPath = FalloutRagdollDeathAssetPath.Select(bodyPartModel);
                _playerModelControllerCall = _playerModelControllerCall with { DeathAssetPath = deathPath };
                if (deathPath is not null)
                {
                    Step("Actor-ragdoll-death-asset-initialization");
                    try { rigModel.InitializeRagdollDeathAsset(load, deathPath, RequireActualPlayerModelLoad); }
                    finally
                    {
                        _playerModelControllerCall = _playerModelControllerCall with
                        { DeathInitialization = rigModel.CaptureDeathInitialization(), RagdollController = ragdoll.Capture() };
                    }
                }
                Step("Actor-ragdoll-controller-final-feature-fields");
                if (!load.Actor.EnginePlayer) throw new NotSupportedException("source-ragdoll-body-part-special-transform-binding-unowned");
                try { ragdoll.PublishFinalFeature(load); }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollController = ragdoll.Capture() }; }
                Step("Actor-ragdoll-final-current-controller-body");
                using (var finalController = ProcessCommon.BorrowEarlyController(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch))
                {
                    if (finalController is not null)
                    {
                        var finalBody = finalController.Controller.ReadModelLoadField(load, FalloutControllerPostProcessField.FirstBody, RequireActualPlayerModelLoad);
                        _playerModelControllerCall = _playerModelControllerCall with { FinalBodyRead = finalBody };
                        try { ragdoll.PublishFinalBody(load, finalBody.Value); }
                        finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollController = ragdoll.Capture() }; }
                    }
                }
                _playerModelControllerCall = _playerModelControllerCall with { ActorConfigurationReturned = true };
                Step("Player-ragdoll-controller-policy");
                try
                {
                    ragdollField.Read(load, RequireActualPlayerModelLoad)!.PublishPlayerArmControl(load);
                    ragdollField.Read(load, RequireActualPlayerModelLoad)!.PublishPlayerFlag(load);
                }
                finally { _playerModelControllerCall = _playerModelControllerCall with { RagdollController = ragdoll.Capture() }; }
            }
            Step("controller-world-association");
            var association = ProcessCommon.ReadAnimationLoadOwner(load);
            if (association.Tier == 0 && !load.Actor.EnginePlayer)
                throw new NotSupportedException("source-controller-helper-non-Player-world-association-unowned");
            Step("controller-final-Actor-virtual");
            try { ContinueStandalonePlayerModelAssociation(load); }
            finally { _playerModelControllerCall = _playerModelControllerCall with { Association = _playerModelAssociation }; }
            if (_playerModelControllerCall.Admission?.Register == true)
                throw new NotSupportedException("source-controller-helper-registered-model-Extra-attachment-tail-unowned");
            Return();
        }
        catch (Exception error)
        { _playerModelControllerCall = _playerModelControllerCall with { Failure = error.Message }; throw; }

        FalloutSourceSceneNode? ReadModel()
        {
            RequireActualPlayerModelLoad(load);
            var cache = CampaignStandalonePlayerScene.ThreadContext.ReadEarlyControllerModelCache(Guid.NewGuid(),
                _sourcePlayerModelData!.Factory, _enginePlayer, () => RequireActualPlayerModelLoad(load));
            var model = cache.Matched ? cache.Model : _sourcePlayerModelData.ReadForPlayerLoad(load, RequireActualPlayerModelLoad)?.Node;
            _playerModelControllerCall = _playerModelControllerCall with { ModelCache = cache, Model = model };
            RequireActualPlayerModelLoad(load); return model;
        }
        void Step(string step)
        { RequireActualPlayerModelLoad(load); _playerModelControllerCall = _playerModelControllerCall with { Step = step }; }
        void Return()
        { Step("returned"); _playerModelControllerCall = _playerModelControllerCall with { Returned = true }; }
    }
}
